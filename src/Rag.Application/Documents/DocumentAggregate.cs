namespace Rag.Application.Documents;

using Rag.Core.Documents;

/// <summary>
/// Aggregate root for a document with its chunks.
/// </summary>
public sealed class DocumentAggregate
{
    public Document Document { get; init; }
    public IReadOnlyList<DocumentChunk> Chunks { get; init; }

    public DocumentAggregate(
        Document document,
        IReadOnlyList<DocumentChunk> chunks)
    {
        if (document is null)
            throw new ArgumentNullException(nameof(document));

        if (chunks is null)
            throw new ArgumentNullException(nameof(chunks));

        Document = document;
        Chunks = chunks;
    }
}
