using Microsoft.Data.Sqlite;
using Rag.Core.Contracts;
using Rag.Core.Retrieval;

namespace Rag.Infrastructure.Search;

/// <summary>
/// FTS5-based sparse retriever for document chunks.
/// Uses SQLite FTS5 with bm25 ranking.
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

        // Escape user query for FTS5 MATCH to prevent syntax errors.
        // Wrap terms in quotes for exact phrase matching.
        var escapedQuery = EscapeFts5Query(query.Text);

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
        command.Parameters.AddWithValue("@query", escapedQuery);
        command.Parameters.AddWithValue("@topK", query.TopK);

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
    /// Escapes user input for safe FTS5 MATCH usage.
    /// Wraps the entire query in quotes for phrase matching,
    /// and escapes any quotes within the text.
    /// </summary>
    private static string EscapeFts5Query(string query)
    {
        // Escape double quotes by doubling them (FTS5 syntax)
        var escaped = query.Replace("\"", "\"\"");
        // Wrap in quotes for phrase matching
        return $"\"{escaped}\"";
    }
}
