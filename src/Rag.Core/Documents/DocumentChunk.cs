namespace Rag.Core.Documents;

/// <summary>
/// Represents a fragment of a document.
/// </summary>
public sealed class DocumentChunk
{
    public Guid ChunkId { get; init; }
    public Guid DocumentId { get; init; }
    public string Text { get; init; }
    public int Position { get; init; }
    public ChunkMetadata Metadata { get; init; }

    public DocumentChunk(
        Guid chunkId,
        Guid documentId,
        string text,
        int position,
        ChunkMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be null or empty.", nameof(text));

        if (position < 0)
            throw new ArgumentException("Position must not be negative.", nameof(position));

        ChunkId = chunkId;
        DocumentId = documentId;
        Text = text;
        Position = position;
        Metadata = metadata;
    }

    public DocumentChunk(Guid documentId, string text, int position, ChunkMetadata? metadata = null)
        : this(Guid.NewGuid(), documentId, text, position, metadata ?? ChunkMetadata.Empty)
    {
    }
}
