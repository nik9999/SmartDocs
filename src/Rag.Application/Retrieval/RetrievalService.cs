namespace Rag.Application.Retrieval;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;

/// <summary>
/// Orchestrates hybrid retrieval: sparse + dense fusion + reranking.
///
/// Pipeline:
/// 1. Retrieve CandidateTopK candidates from each retriever (FTS5 + Vector)
/// 2. Fuse results using RRF
/// 3. Rerank fused candidates
/// 4. Return FinalTopK results
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
        // Execute sparse and vector retrieval in parallel with CandidateTopK
        var sparseTask = _sparseRetriever.SearchAsync(query, cancellationToken);
        var vectorTask = _vectorRetriever.SearchAsync(query, cancellationToken);

        await Task.WhenAll(sparseTask, vectorTask).ConfigureAwait(false);

        var sparseResults = sparseTask.Result;
        var vectorResults = vectorTask.Result;

        // Fuse results from both retrievers with CandidateTopK
        var fusedResults = _fusion.Fuse(
            new List<IReadOnlyList<RetrievalResult>> { sparseResults, vectorResults },
            query.CandidateTopK);

        // Rerank fused candidates and return FinalTopK
        var rerankedResults = await _reranker.RerankAsync(
            query.Text,
            fusedResults,
            query.FinalTopK,
            cancellationToken).ConfigureAwait(false);

        return rerankedResults;
    }
}
