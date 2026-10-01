namespace Rag.Core.Embeddings;

/// <summary>
/// Persists and retrieves embedding vectors from SQLite.
/// </summary>
public interface IEmbeddingRepository
{
    /// <summary>
    /// Saves an embedding vector for the given chunk.
    /// </summary>
    Task SaveAsync(
        Guid chunkId,
        Embedding embedding,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an embedding vector for the given chunk.
    /// </summary>
    /// <returns>The embedding, or null if not found.</returns>
    Task<Embedding?> GetAsync(
        Guid chunkId,
        CancellationToken cancellationToken);
}
