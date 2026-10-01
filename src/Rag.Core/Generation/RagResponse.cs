namespace Rag.Core.Generation;

/// <summary>
/// Final result of the RAG pipeline.
/// </summary>
public sealed class RagResponse
{
    public string Answer { get; }
    public IReadOnlyList<Citation> Citations { get; }
    public string? Query { get; }

    public RagResponse(
        string answer,
        IReadOnlyList<Citation> citations,
        string? query = null)
    {
        if (string.IsNullOrWhiteSpace(answer))
            throw new ArgumentException("Answer must not be null or empty.", nameof(answer));

        if (citations is null)
            throw new ArgumentNullException(nameof(citations));

        Answer = answer;
        Citations = citations;
        Query = query;
    }
}
