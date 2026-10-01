namespace Rag.Application.Retrieval;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;

/// <summary>
/// Orchestrates hybrid retrieval: sparse + dense fusion + reranking.
/// </summary>
public sealed class RetrievalService : IRetrievalService
{
    private readonly ISparseRetriever _sparseRetriever;
    private readonly IVectorRetriever _vectorRetriever;
    private readonly IResultFusion _fusion;
    private readonly IReranker _reranker;

    public RetrievalService(
        ISparseRetriever sparseRetriever,
        IVectorRetriever vectorRetriever,
        IResultFusion fusion,
        IReranker reranker)
    {
        _sparseRetriever = sparseRetriever;
        _vectorRetriever = vectorRetriever;
        _fusion = fusion;
        _reranker = reranker;
    }

    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken)
    {
        // Execute sparse and vector retrieval in parallel
        var sparseTask = _sparseRetriever.SearchAsync(query, cancellationToken);
        var vectorTask = _vectorRetriever.SearchAsync(query, cancellationToken);

        await Task.WhenAll(sparseTask, vectorTask).ConfigureAwait(false);

        var sparseResults = sparseTask.Result;
        var vectorResults = vectorTask.Result;

        // Fuse results from both retrievers
        var fusedResults = _fusion.Fuse(
            new List<IReadOnlyList<RetrievalResult>> { sparseResults, vectorResults },
            query.TopK);

        // Rerank fused candidates
        var rerankedResults = await _reranker.RerankAsync(
            query.Text,
            fusedResults,
            query.TopK,
            cancellationToken).ConfigureAwait(false);

        return rerankedResults;
    }
}
