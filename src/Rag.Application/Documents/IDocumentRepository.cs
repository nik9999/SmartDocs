namespace Rag.Application.Documents;

using Rag.Core.Documents;

/// <summary>
/// Port for document persistence operations.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>
    /// Saves or replaces a document aggregate (document + metadata + chunks) atomically.
    /// </summary>
    Task SaveAsync(
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads a document with its chunks and metadata by ID.
    /// Returns null if the document does not exist.
    /// </summary>
    Task<DocumentAggregate?> GetAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a document and all associated metadata and chunks.
    /// Idempotent — succeeds even if the document does not exist.
    /// </summary>
    Task DeleteAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}
