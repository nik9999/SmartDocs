using Microsoft.Data.Sqlite;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Embeddings;
using Rag.Core.Retrieval;

namespace Rag.Infrastructure.Search;

/// <summary>
/// Brute-force cosine similarity vector search over SQLite embeddings.
/// Sequential scan — reference implementation, not optimized.
/// </summary>
public sealed class SqliteVectorRetriever : IVectorRetriever
{
    private readonly string _connectionString;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public SqliteVectorRetriever(
        string connectionString,
        IEmbeddingGenerator embeddingGenerator)
    {
        _connectionString = connectionString;
        _embeddingGenerator = embeddingGenerator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken)
    {
        var results = new List<ScoredCandidate>();

        if (string.IsNullOrWhiteSpace(query.Text))
            return Array.Empty<RetrievalResult>();

        if (query.TopK <= 0)
            return Array.Empty<RetrievalResult>();

        // Generate query embedding
        var queryEmbedding = await _embeddingGenerator
            .GenerateAsync(query.Text, cancellationToken).ConfigureAwait(false);

        var queryVector = queryEmbedding.Vector;
        var queryModel = queryEmbedding.Model;
        var queryDimensions = queryEmbedding.Dimensions;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Load all embeddings with chunk info
        var sql = """
            SELECT
                e.ChunkId,
                c.DocumentId,
                c.Text,
                e.Model,
                e.Dimensions,
                e.Vector,
                c.PageNumber,
                c.Section,
                c.TokenCount
            FROM Embeddings e
            INNER JOIN Chunks c ON c.ChunkId = e.ChunkId
            ORDER BY e.ChunkId
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunkId = Guid.Parse(reader.GetString(0));
            var documentId = Guid.Parse(reader.GetString(1));
            var text = reader.GetString(2);
            var storedModel = reader.GetString(3);
            var storedDimensions = reader.GetInt32(4);
            var vectorBlob = (byte[])reader.GetValue(5);

            // Model mismatch — skip
            if (storedModel != queryModel)
                continue;

            // Dimension mismatch — skip
            if (storedDimensions != queryDimensions)
                continue;

            // Corrupted BLOB — skip
            if (vectorBlob.Length % sizeof(float) != 0)
                continue;

            var candidateVector = DeserializeVector(vectorBlob, storedDimensions);

            // Compute cosine similarity
            var similarity = CosineSimilarity(queryVector, candidateVector);

            // Build metadata
            var pageNumber = reader.IsDBNull(6) ? (int?)null : (int?)reader.GetInt32(6);
            var section = reader.IsDBNull(7) ? null : reader.GetString(7);
            var tokenCount = reader.IsDBNull(8) ? (int?)null : (int?)reader.GetInt32(8);

            var metadata = new Dictionary<string, string>();
            if (pageNumber.HasValue)
                metadata["PageNumber"] = pageNumber.Value.ToString();
            if (!string.IsNullOrEmpty(section))
                metadata["Section"] = section;
            if (tokenCount.HasValue)
                metadata["TokenCount"] = tokenCount.Value.ToString();

            results.Add(new ScoredCandidate(
                documentId,
                chunkId,
                text,
                similarity,
                metadata));
        }

        // Sort by score descending, then by ChunkId ascending for deterministic tie-breaking
        results.Sort((a, b) =>
        {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0)
                return scoreCompare;
            return a.ChunkId.CompareTo(b.ChunkId);
        });

        // Take TopK
        var topK = Math.Min(results.Count, query.TopK);
        var finalResults = new List<RetrievalResult>();

        for (var i = 0; i < topK; i++)
        {
            var candidate = results[i];
            finalResults.Add(new RetrievalResult(
                candidate.DocumentId,
                candidate.ChunkId,
                candidate.Text,
                candidate.Score,
                i + 1,
                RetrievalSource.Dense,
                candidate.Metadata));
        }

        return finalResults;
    }

    /// <summary>
    /// Computes cosine similarity between two vectors using double precision.
    /// Returns 0 for zero vectors to avoid NaN/Infinity.
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        double dot = 0;
        double normASquared = 0;
        double normBSquared = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * (double)b[i];
            normASquared += (double)a[i] * (double)a[i];
            normBSquared += (double)b[i] * (double)b[i];
        }

        if (normASquared == 0 || normBSquared == 0)
            return 0;

        return dot / (Math.Sqrt(normASquared) * Math.Sqrt(normBSquared));
    }

    /// <summary>
    /// Deserializes a byte array to a float array (IEEE-754 float32 little-endian).
    /// Same implementation as EmbeddingRepository for consistency.
    /// </summary>
    private static float[] DeserializeVector(byte[] bytes, int dimensions)
    {
        var vector = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            var fBytes = new byte[sizeof(float)];
            Buffer.BlockCopy(bytes, i * sizeof(float), fBytes, 0, sizeof(float));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(fBytes);
            }
            vector[i] = BitConverter.ToSingle(fBytes, 0);
        }
        return vector;
    }

    /// <summary>
    /// Internal record for intermediate scoring.
    /// </summary>
    private sealed record ScoredCandidate(
        Guid DocumentId,
        Guid ChunkId,
        string Text,
        double Score,
        IReadOnlyDictionary<string, string> Metadata);
}
