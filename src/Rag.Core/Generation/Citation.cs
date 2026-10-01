namespace Rag.Core.Generation;

/// <summary>
/// Links a generated answer to a specific retrieved chunk.
/// </summary>
public sealed class Citation
{
    public int CitationId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid ChunkId { get; init; }
    public string Text { get; init; }
    public string Source { get; init; }

    public Citation(
        int citationId,
        Guid documentId,
        Guid chunkId,
        string text,
        string source)
    {
        if (citationId <= 0)
            throw new ArgumentException("CitationId must be greater than zero.", nameof(citationId));

        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId must not be empty.", nameof(documentId));

        if (chunkId == Guid.Empty)
            throw new ArgumentException("ChunkId must not be empty.", nameof(chunkId));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be null or empty.", nameof(text));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source must not be null or empty.", nameof(source));

        CitationId = citationId;
        DocumentId = documentId;
        ChunkId = chunkId;
        Text = text;
        Source = source;
    }
}
