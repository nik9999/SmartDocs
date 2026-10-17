using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// IRetriever decorator that runs the inner retriever with a wider
/// candidate count, then reranks down to the user's requested topK.
/// Implements the production-default
/// <em>retrieve-broadly, rerank-precisely</em> pattern.
/// <para>
/// An optional <see cref="MinScore"/> relevance floor (default <c>0.0</c> =
/// off) drops every reranked result whose score is below the threshold. The
/// floor can return <em>fewer</em> than <c>topK</c> results — or an empty
/// list when nothing clears the bar. That is intentional: an empty source
/// set lets the generator abstain with the grounded "I don't know" answer
/// rather than ground a response in weak passages.
/// </para>
/// </summary>
public sealed class RerankingMiddleware : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IReranker _reranker;

    /// <summary>How many candidates to pull from the inner retriever before reranking.</summary>
    public int CandidateCount { get; }

    /// <summary>
    /// Minimum reranked relevance score a result must reach to survive. A value
    /// of <c>0.0</c> (the default) disables the floor and preserves the prior
    /// behavior. When greater than zero, results scoring below it are dropped,
    /// which may yield fewer than <c>topK</c> results or an empty list.
    /// </summary>
    public double MinScore { get; }

    public RerankingMiddleware(IRetriever inner, IReranker reranker, int candidateCount = 20, double minScore = 0.0)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        ArgumentOutOfRangeException.ThrowIfNegative(minScore);
        _inner = inner;
        _reranker = reranker;
        CandidateCount = candidateCount;
        MinScore = minScore;
    }

    public string Strategy => $"{_inner.Strategy}+rerank({_reranker.Implementation})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var candidateK = Math.Max(CandidateCount, topK);
        var candidates = await _inner.RetrieveAsync(query, candidateK, cancellationToken).ConfigureAwait(false);
        var reranked = await _reranker.RerankAsync(query, candidates, topK, cancellationToken).ConfigureAwait(false);

        if (MinScore > 0.0)
        {
            // Drop weak passages below the floor, preserving the reranked order.
            // May return fewer than topK — or empty — so the generator can abstain.
            return [.. reranked.Where(r => r.Score >= MinScore)];
        }

        return reranked;
    }
}
