namespace Rag.Core.Contracts;

using Rag.Core.Retrieval;

/// <summary>
/// Reranks a set of candidate retrieval results.
/// </summary>
public interface IReranker
{
    /// <summary>
    /// Reranks the given candidates and returns the top-K results.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="candidates">Candidate results to rerank.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken);
}
