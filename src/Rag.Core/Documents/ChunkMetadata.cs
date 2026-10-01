namespace Rag.Core.Documents;

/// <summary>
/// Immutable metadata for a document chunk.
/// </summary>
public sealed record ChunkMetadata
{
    public static ChunkMetadata Empty { get; } = new();

    public int? PageNumber { get; init; }
    public string? Section { get; init; }
    public int? TokenCount { get; init; }

    public ChunkMetadata(
        int? pageNumber = null,
        string? section = null,
        int? tokenCount = null)
    {
        PageNumber = pageNumber;
        Section = section;
        TokenCount = tokenCount;
    }
}
