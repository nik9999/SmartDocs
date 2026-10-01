using Microsoft.Data.Sqlite;
using Rag.Core.Contracts;
using Rag.Core.Retrieval;

namespace Rag.Infrastructure.Search;

/// <summary>
/// FTS5-based sparse retriever for document chunks.
/// Uses SQLite FTS5 with bm25 ranking and lexical (token-based) search.
/// </summary>
public sealed class Fts5Search : ISparseRetriever
{
    private readonly string _connectionString;

    public Fts5Search(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken)
    {
        var results = new List<RetrievalResult>();

        if (string.IsNullOrWhiteSpace(query.Text))
            return results;

        // Build FTS5 query: lexical search with individual token matching.
        // If user explicitly uses FTS5 operators (AND, OR, NOT, NEAR, phrase), use as-is.
        // Otherwise, split into tokens and join with OR.
        var fts5Query = BuildFts5Query(query.Text);

        var sql = """
            SELECT
                DocumentId,
                ChunkId,
                Text,
                bm25(ChunksFts) AS rank
            FROM ChunksFts
            WHERE ChunksFts MATCH @query
            ORDER BY rank ASC
            LIMIT @topK
            """;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@query", fts5Query);
        command.Parameters.AddWithValue("@topK", query.CandidateTopK);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rank = 1;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var documentId = Guid.Parse(reader.GetString(0));
            var chunkId = Guid.Parse(reader.GetString(1));
            var text = reader.GetString(2);
            var bm25Rank = reader.GetDouble(3);

            // bm25 returns lower values for better results.
            // Convert to a score where higher is better: score = -rank.
            var score = -bm25Rank;

            results.Add(new RetrievalResult(
                documentId,
                chunkId,
                text,
                score,
                rank,
                RetrievalSource.Sparse));

            rank++;
        }

        return results;
    }

    /// <summary>
    /// Builds a safe FTS5 MATCH query from user input.
    /// Uses lexical (token-based) search by default.
    /// If the query contains explicit FTS5 operators, uses it as-is.
    /// </summary>
    private static string BuildFts5Query(string userQuery)
    {
        // Check if the query already contains FTS5 operators — if so, use as-is
        var upperQuery = userQuery.ToUpperInvariant();
        if (ContainsFts5Operator(upperQuery))
            return EscapeFts5Tokens(userQuery);

        // Lexical search: split into tokens and join with OR
        var tokens = SplitIntoTokens(userQuery);

        if (tokens.Length == 0)
            return "*"; // Match everything

        // Escape tokens, filtering out FTS5 operators
        var escapedTokens = tokens
            .Select(t => EscapeFts5Token(t))
            .Where(t => t != null)
            .ToArray();

        if (escapedTokens.Length == 0)
            return "*"; // All tokens were operators

        if (escapedTokens.Length == 1)
            return escapedTokens[0]!;

        // Multiple tokens: OR search
        return string.Join(" OR ", escapedTokens);
    }

    /// <summary>
    /// Checks if the query text contains explicit FTS5 operators.
    /// Only considers well-formed FTS5 syntax (not user quotes in regular text).
    /// </summary>
    private static bool ContainsFts5Operator(string upperQuery)
    {
        // Check for explicit FTS5 syntax patterns
        // AND, OR, NOT as standalone operators (not part of words)
        // NEAR operator
        var operators = new[] { " AND ", " OR ", " NOT ", " NEAR " };
        foreach (var op in operators)
        {
            if (upperQuery.Contains(op))
                return true;
        }

        // Check for NOT at end: "word NOT"
        if (upperQuery.EndsWith(" NOT"))
            return true;

        // Check for NEAR syntax: "word NEAR/word"
        if (upperQuery.Contains("NEAR/"))
            return true;

        // Check for well-formed phrase query: starts and ends with quotes
        // e.g., "word word" — this is a valid FTS5 phrase query
        if (upperQuery.StartsWith("\"") && upperQuery.EndsWith("\""))
        {
            // Make sure it's not just a single quote in the middle
            var inner = upperQuery[1..^1];
            if (!inner.Contains("\""))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits user text into tokens, preserving quoted phrases.
    /// </summary>
    private static string[] SplitIntoTokens(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '"')
            {
                if (inQuote && current.Length > 0)
                {
                    // End of quoted phrase — treat as single token
                    tokens.Add(current.ToString());
                    current.Clear();
                    inQuote = false;
                }
                else if (!inQuote)
                {
                    // Start of quoted phrase
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    inQuote = true;
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }

    /// <summary>
    /// Escapes a single token for safe FTS5 usage.
    /// Escapes special characters: " → "", * → \*, : → \:
    /// Skips pure FTS5 operators (+, NOT, AND, OR, NEAR) as they would cause syntax errors.
    /// </summary>
    private static string? EscapeFts5Token(string token)
    {
        // Skip pure FTS5 operators that would cause syntax errors
        var upper = token.ToUpperInvariant();
        if (upper == "+" || upper == "NOT" || upper == "AND" || upper == "OR" || upper == "NEAR")
            return null;

        var escaped = token
            .Replace("\"", "\"\"")
            .Replace("*", "\\*")
            .Replace(":", "\\:");

        return escaped;
    }

    /// <summary>
    /// Escapes tokens in a query that will be used as-is (when user provides FTS5 operators).
    /// </summary>
    private static string EscapeFts5Tokens(string query)
    {
        // For queries with explicit FTS5 operators, we still need to escape
        // special characters within unquoted terms.
        // Simple approach: escape the known special chars.
        return query
            .Replace("\\", "\\\\")
            .Replace("*", "\\*")
            .Replace("+", "\\+")
            .Replace("\"", "\"\"");
    }
}
