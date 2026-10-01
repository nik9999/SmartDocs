namespace Rag.Core.Retrieval;

/// <summary>
/// Represents a single retrieval result.
/// </summary>
public sealed class RetrievalResult
{
    public Guid DocumentId { get; init; }
    public Guid ChunkId { get; init; }
    public string Text { get; init; }
    public double Score { get; init; }
    public int Rank { get; init; }
    public RetrievalSource Source { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    public RetrievalResult(
        Guid documentId,
        Guid chunkId,
        string text,
        double score,
        int rank,
        RetrievalSource source,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("DocumentId must not be empty.", nameof(documentId));

        if (chunkId == Guid.Empty)
            throw new ArgumentException("ChunkId must not be empty.", nameof(chunkId));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be null or empty.", nameof(text));

        if (rank <= 0)
            throw new ArgumentException("Rank must be greater than zero.", nameof(rank));

        DocumentId = documentId;
        ChunkId = chunkId;
        Text = text;
        Score = score;
        Rank = rank;
        Source = source;
        Metadata = metadata ?? new Dictionary<string, string>();
    }
}
