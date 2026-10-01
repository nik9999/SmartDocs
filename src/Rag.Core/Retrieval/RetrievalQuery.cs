namespace Rag.Core.Retrieval;

/// <summary>
/// Input query for retrieval operations.
/// </summary>
public sealed class RetrievalQuery
{
    public string Text { get; }
    public int TopK { get; }
    public RetrievalFilters Filters { get; }

    public RetrievalQuery(
        string text,
        int topK,
        RetrievalFilters? filters = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Query text must not be null or empty.", nameof(text));

        if (topK <= 0)
            throw new ArgumentException("TopK must be greater than zero.", nameof(topK));

        Text = text;
        TopK = topK;
        Filters = filters ?? new RetrievalFilters();
    }
}
