namespace Rag.Core.Generation;

/// <summary>
/// Context passed to the generation step.
/// </summary>
public sealed class RagContext
{
    public string Query { get; }
    public IReadOnlyList<Retrieval.RetrievalResult> Results { get; }
    public IReadOnlyList<Citation> Citations { get; }

    public RagContext(
        string query,
        IReadOnlyList<Retrieval.RetrievalResult> results,
        IReadOnlyList<Citation> citations)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query must not be null or empty.", nameof(query));

        if (results is null)
            throw new ArgumentNullException(nameof(results));

        if (citations is null)
            throw new ArgumentNullException(nameof(citations));

        Query = query;
        Results = results;
        Citations = citations;
    }
}
