using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// Re-orders an initial retrieval result list by a higher-quality
/// (typically cross-encoder) relevance score. Sits between retrieval and
/// generation in the production-default <em>retrieve-broadly,
/// rerank-precisely</em> pattern.
/// </summary>
public interface IReranker
{
    /// <summary>Short, stable identifier for the reranker (e.g. <c>cohere-rerank-3</c>, <c>bge-v2-m3</c>).</summary>
    string Implementation { get; }

    /// <summary>Re-score and re-sort <paramref name="candidates"/>.</summary>
    Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default);
}
