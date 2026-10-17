using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;

namespace SmartDocs.Retrieval.Filtering;

/// <summary>
/// Result of compiling a <see cref="MetadataFilter"/> into a parameterized SQL condition.
/// </summary>
public sealed class SqliteCompiledFilter
{
    /// <summary>
    /// Parameterized SQL condition for use in a WHERE clause (e.g. <c>(json_extract(metadata_json, '$.Department') = @flt_0)</c>).
    /// </summary>
    public string SqlWhere { get; }

    /// <summary>
    /// Named parameters and their values required to execute <see cref="SqlWhere"/>.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    public SqliteCompiledFilter(string sqlWhere, IReadOnlyDictionary<string, object?> parameters)
    {
        SqlWhere = sqlWhere;
        Parameters = parameters;
    }
}

/// <summary>
/// Compiles a <see cref="MetadataFilter"/> into a parameterized SQLite SQL WHERE clause
/// using SQLite's built-in <c>json_extract</c> function.
/// Enables true database-side pre-filtering in SQLite so candidate elimination occurs
/// before SIMD vector distance calculation and sorting.
/// Returns <see langword="null"/> when encountering unsupported expressions,
/// signaling the caller to fall back to in-process evaluation via <see cref="MetadataFilter.Matches"/>.
/// </summary>
public static class SqliteFilterCompiler
{
    /// <summary>
    /// Compiles <paramref name="filter"/> into a <see cref="SqliteCompiledFilter"/>, or returns <see langword="null"/>
    /// if the filter expression contains nodes not supported by this compiler.
    /// </summary>
    /// <param name="filter">The metadata filter to compile.</param>
    /// <param name="jsonColumn">The name of the column storing serialized metadata JSON (default <c>"metadata_json"</c>).</param>
    /// <param name="tableAlias">Optional table alias (e.g. <c>"v"</c>). When omitted or null, column name is unqualified.</param>
    public static SqliteCompiledFilter? TryCompile(
        MetadataFilter filter,
        string jsonColumn = "metadata_json",
        string? tableAlias = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonColumn);

        try
        {
            var context = new CompilerContext(jsonColumn, tableAlias);
            var root = InvocationInliner.Inline(filter.Predicate.Body, filter.Predicate.Parameters[0]);
            var sql = context.Visit(root, filter.Predicate.Parameters[0]);

            return new SqliteCompiledFilter(sql, context.Parameters);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed class CompilerContext(string jsonColumn, string? tableAlias)
    {
        private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);
        private int _paramCounter;

        public IReadOnlyDictionary<string, object?> Parameters => _parameters;

        public string ColumnExpr(string propertyName)
        {
            var col = string.IsNullOrWhiteSpace(tableAlias) ? jsonColumn : $"{tableAlias}.{jsonColumn}";
            if (string.Equals(propertyName, nameof(DocumentMetadata.FiscalYear), StringComparison.Ordinal))
            {
                return $"CAST(json_extract({col}, '$.FiscalYear') AS INTEGER)";
            }

            return $"json_extract({col}, '$.{propertyName}')";
        }

        public string AddParameter(object? value)
        {
            var name = $"@flt_{_paramCounter++}";
            if (value is DateOnly d)
            {
                _parameters[name] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                _parameters[name] = value;
            }
            return name;
        }

        public string Visit(Expression node, ParameterExpression param) => node.NodeType switch
        {
            ExpressionType.AndAlso => Combine((BinaryExpression)node, "AND", param),
            ExpressionType.OrElse => Combine((BinaryExpression)node, "OR", param),
            ExpressionType.Equal => BuildComparison((BinaryExpression)node, "=", param),
            ExpressionType.NotEqual => BuildComparison((BinaryExpression)node, "!=", param),
            ExpressionType.LessThan => BuildComparison((BinaryExpression)node, "<", param),
            ExpressionType.LessThanOrEqual => BuildComparison((BinaryExpression)node, "<=", param),
            ExpressionType.GreaterThan => BuildComparison((BinaryExpression)node, ">", param),
            ExpressionType.GreaterThanOrEqual => BuildComparison((BinaryExpression)node, ">=", param),
            ExpressionType.Not => BuildNot((UnaryExpression)node, param),
            ExpressionType.Call => BuildCall((MethodCallExpression)node, param),
            ExpressionType.Constant when node is ConstantExpression { Value: bool b } => b ? "1 = 1" : "1 = 0",
            _ => throw new NotSupportedException($"Unsupported SQLite filter node: {node.NodeType}"),
        };

        private string Combine(BinaryExpression bin, string op, ParameterExpression param)
        {
            var left = Visit(bin.Left, param);
            var right = Visit(bin.Right, param);
            return $"({left} {op} {right})";
        }

        private string BuildNot(UnaryExpression u, ParameterExpression param)
        {
            var inner = Visit(u.Operand, param);
            return $"NOT ({inner})";
        }

        private string BuildComparison(BinaryExpression bin, string op, ParameterExpression param)
        {
            if (TryExtractMember(bin.Left, param, out var prop) && TryExtractValue(bin.Right, out var value))
            {
                return RenderComparison(prop, op, value);
            }
            if (TryExtractMember(bin.Right, param, out prop) && TryExtractValue(bin.Left, out value))
            {
                return RenderComparison(prop, FlipOp(op), value);
            }

            throw new NotSupportedException($"Unsupported comparison: {bin}");
        }

        private string RenderComparison(string property, string op, object? value)
        {
            var colExpr = ColumnExpr(property);

            if (value is null)
            {
                return op switch
                {
                    "=" => $"{colExpr} IS NULL",
                    "!=" => $"{colExpr} IS NOT NULL",
                    _ => throw new NotSupportedException($"Cannot perform range comparison against null on {property}")
                };
            }

            var pName = AddParameter(value);
            if (op == "!=")
            {
                // In SQL: (col != @p OR col IS NULL) to mirror C# != semantics
                return $"({colExpr} != {pName} OR {colExpr} IS NULL)";
            }

            return $"({colExpr} {op} {pName})";
        }

        private string BuildCall(MethodCallExpression call, ParameterExpression param)
        {
            // 1. String methods: m.Title.Contains("x"), StartsWith("x"), EndsWith("x"), Equals("x")
            if (call.Method.DeclaringType == typeof(string))
            {
                if (call.Method.Name == nameof(string.Equals))
                {
                    if (call.Object is not null && call.Arguments.Count >= 1 &&
                        TryExtractMember(call.Object, param, out var prop) &&
                        TryExtractValue(call.Arguments[0], out var val))
                    {
                        return RenderComparison(prop, "=", val);
                    }
                    if (call.Object is null && call.Arguments.Count >= 2 &&
                        TryExtractMember(call.Arguments[0], param, out prop) &&
                        TryExtractValue(call.Arguments[1], out val))
                    {
                        return RenderComparison(prop, "=", val);
                    }
                }

                if (call.Object is not null && call.Arguments.Count >= 1 &&
                    TryExtractMember(call.Object, param, out var stringProp) &&
                    TryExtractValue(call.Arguments[0], out var matchObj) &&
                    matchObj is string matchStr)
                {
                    var colExpr = ColumnExpr(stringProp);
                    var escaped = EscapeLikePattern(matchStr);

                    switch (call.Method.Name)
                    {
                        case nameof(string.Contains):
                            var pNameContains = AddParameter($"%{escaped}%");
                            return $"({colExpr} LIKE {pNameContains} ESCAPE '\\')";

                        case nameof(string.StartsWith):
                            var pNameStarts = AddParameter($"{escaped}%");
                            return $"({colExpr} LIKE {pNameStarts} ESCAPE '\\')";

                        case nameof(string.EndsWith):
                            var pNameEnds = AddParameter($"%{escaped}");
                            return $"({colExpr} LIKE {pNameEnds} ESCAPE '\\')";
                    }
                }
            }

            // 2. Set membership: collection.Contains(m.Prop) or Enumerable.Contains(coll, m.Prop)
            if (call.Method.Name == "Contains")
            {
                Expression? memberArg = null;
                Expression? collectionExpr = null;

                if (call.Object is not null && call.Arguments.Count == 1)
                {
                    collectionExpr = call.Object;
                    memberArg = call.Arguments[0];
                }
                else if (call.Object is null && call.Arguments.Count >= 2)
                {
                    collectionExpr = call.Arguments[0];
                    memberArg = call.Arguments[1];
                }

                collectionExpr = UnwrapSpanConversion(collectionExpr);

                if (memberArg is not null && collectionExpr is not null &&
                    TryExtractMember(memberArg, param, out var prop) &&
                    TryExtractValue(collectionExpr, out var rawColl) &&
                    rawColl is IEnumerable enumerable && rawColl is not string)
                {
                    var colExpr = ColumnExpr(prop);
                    var items = enumerable.Cast<object?>().Where(v => v is not null).ToList();

                    if (items.Count == 0)
                    {
                        return "1 = 0";
                    }

                    var paramNames = items.Select(AddParameter);
                    return $"({colExpr} IN ({string.Join(", ", paramNames)}))";
                }
            }

            throw new NotSupportedException($"Unsupported method call: {call.Method.Name}");
        }

        private static string EscapeLikePattern(string input)
        {
            return input
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        private static string FlipOp(string op) => op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => op
        };
    }

    private static bool TryExtractMember(Expression expr, ParameterExpression param, out string property)
    {
        if (expr is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            expr = u.Operand;
        }

        if (expr is MemberExpression me && me.Expression == param)
        {
            property = me.Member.Name;
            return true;
        }

        property = string.Empty;
        return false;
    }

    private static bool TryExtractValue(Expression expr, out object? value)
    {
        if (expr is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            expr = u.Operand;
        }

        if (expr is ConstantExpression ce)
        {
            value = ce.Value;
            return true;
        }

        if (expr is MemberExpression me && me.Expression is ConstantExpression target)
        {
            value = me.Member switch
            {
                FieldInfo f => f.GetValue(target.Value),
                PropertyInfo p => p.GetValue(target.Value),
                _ => null,
            };
            return true;
        }

        if (!ReferencesParameter(expr))
        {
            try
            {
                value = Expression.Lambda(Expression.Convert(expr, typeof(object))).Compile().DynamicInvoke();
                return true;
            }
            catch (InvalidOperationException)
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    private static bool ReferencesParameter(Expression expr)
    {
        var finder = new ParameterFinder();
        finder.Visit(expr);
        return finder.Found;
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return base.VisitParameter(node);
        }
    }

    private static Expression? UnwrapSpanConversion(Expression? expr)
    {
        if (expr is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } conv)
        {
            expr = conv.Arguments[0];
        }
        if (expr is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            expr = u.Operand;
        }
        return expr;
    }

    private sealed class InvocationInliner(ParameterExpression target) : ExpressionVisitor
    {
        private readonly Dictionary<ParameterExpression, Expression> _substitutions = [];

        public static Expression Inline(Expression node, ParameterExpression target)
            => new InvocationInliner(target).Visit(node);

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            if (node.Expression is not LambdaExpression lambda)
            {
                return base.VisitInvocation(node);
            }

            for (var i = 0; i < lambda.Parameters.Count; i++)
            {
                _substitutions[lambda.Parameters[i]] =
                    i < node.Arguments.Count ? Visit(node.Arguments[i]) : target;
            }

            return Visit(lambda.Body);
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => _substitutions.TryGetValue(node, out var replacement) ? replacement : node;
    }
}
