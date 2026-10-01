namespace Rag.Core.Retrieval;

/// <summary>
/// Input query for retrieval operations.
/// </summary>
public sealed class RetrievalQuery
{
    /// <summary>
    /// The query text to search for.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// The number of final results to return after reranking.
    /// </summary>
    public int TopK { get; }

    /// <summary>
    /// The number of candidates to retrieve from each retriever before fusion.
    /// Must be greater than or equal to TopK.
    /// Default: 50.
    /// </summary>
    public int CandidateTopK { get; }

    /// <summary>
    /// The number of final results to return after reranking.
    /// Defaults to TopK if not explicitly specified.
    /// </summary>
    public int FinalTopK { get; }

    /// <summary>
    /// Structured filters for retrieval queries.
    /// </summary>
    public RetrievalFilters Filters { get; }

    /// <summary>
    /// Creates a new retrieval query.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="topK">Number of final results to return.</param>
    /// <param name="candidateTopK">Number of candidates to retrieve from each retriever.
    /// Must be >= topK. Defaults to 50.</param>
    /// <param name="filters">Optional filters.</param>
    public RetrievalQuery(
        string text,
        int topK,
        int? candidateTopK = null,
        RetrievalFilters? filters = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Query text must not be null or empty.", nameof(text));

        if (topK <= 0)
            throw new ArgumentException("TopK must be greater than zero.", nameof(topK));

        var candidateTop = candidateTopK ?? RetrievalConstants.DefaultCandidateTopK;

        if (candidateTop < topK)
            throw new ArgumentException(
                $"CandidateTopK ({candidateTop}) must be greater than or equal to TopK ({topK}).",
                nameof(candidateTopK));

        Text = text;
        TopK = topK;
        CandidateTopK = candidateTop;
        FinalTopK = topK;
        Filters = filters ?? new RetrievalFilters();
    }
}
