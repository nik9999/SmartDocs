namespace Rag.Core.Retrieval;

/// <summary>
/// Structured filters for retrieval queries.
/// </summary>
public sealed class RetrievalFilters
{
    public IReadOnlyDictionary<string, string> Items { get; }

    public RetrievalFilters(IReadOnlyDictionary<string, string>? items = null)
    {
        Items = items ?? new Dictionary<string, string>();
    }
}
