using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// Pass-through reranker — preserves order, only truncates to <c>topK</c>.
/// Useful as the dev-time default and as the control arm of A/B tests.
/// </summary>
public sealed class NoOpReranker : IReranker
{
    public string Implementation => "no-op";

    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        IReadOnlyList<RetrievalResult> truncated = candidates.Take(topK).ToList();
        return Task.FromResult(truncated);
    }
}
