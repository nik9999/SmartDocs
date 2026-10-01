using Rag.Application.Retrieval;
using Rag.Core.Retrieval;

namespace Rag.Application.Evaluation;

/// <summary>
/// Orchestrates retrieval evaluation by executing golden queries against
/// an <see cref="IRetrievalService"/> and computing aggregate metrics
/// via <see cref="RetrievalEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runner performs the following steps for each golden query:
/// <list type="number">
/// <item>Creates a <see cref="RetrievalQuery"/> with the specified <c>K</c> value.</item>
/// <item>Calls <see cref="IRetrievalService.SearchAsync"/> to retrieve results.</item>
/// <item>Collects the <see cref="RetrievalResult"/> hits.</item>
/// </list>
/// </para>
/// <para>
/// After all queries are executed, the runner passes the collected samples
/// to <see cref="RetrievalEvaluator.Evaluate"/> which computes Recall@K,
/// Precision@K, MRR, and nDCG@K.
/// </para>
/// <para>
/// The runner does NOT implement metric calculations itself — it only
/// prepares the <c>(GoldenQuery, RetrievalResult[])</c> tuples and delegates
/// to the existing <see cref="RetrievalEvaluator"/>.
/// </para>
/// </remarks>
public sealed class RetrievalEvaluationRunner
{
    private readonly IRetrievalService _retrievalService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetrievalEvaluationRunner"/> class.
    /// </summary>
    /// <param name="retrievalService">The retrieval service to evaluate.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="retrievalService"/> is null.
    /// </exception>
    public RetrievalEvaluationRunner(IRetrievalService retrievalService)
    {
        _retrievalService = retrievalService ?? throw new ArgumentNullException(nameof(retrievalService));
    }

    /// <summary>
    /// Runs evaluation against all golden queries.
    /// </summary>
    /// <param name="queries">The golden queries to evaluate.</param>
    /// <param name="k">
    /// The cutoff parameter: the number of top results to consider for each query.
    /// Must be greater than zero.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Aggregate <see cref="RetrievalMetrics"/> across all queries.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="queries"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="k"/> is less than or equal to zero.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If <paramref name="queries"/> is empty, returns <see cref="RetrievalMetrics"/>
    /// with all metric values set to 0 and <c>QueryCount</c> set to 0.
    /// </para>
    /// <para>
    /// If a retrieval call throws an exception, the exception propagates —
    /// the runner does not mask retrieval errors as successful evaluation.
    /// </para>
    /// </remarks>
    public async Task<RetrievalMetrics> RunAsync(
        IReadOnlyList<GoldenQuery> queries,
        int k,
        CancellationToken cancellationToken = default)
    {
        if (queries == null)
            throw new ArgumentNullException(nameof(queries));
        if (k <= 0)
            throw new ArgumentOutOfRangeException(nameof(k), "K must be greater than zero.");

        if (queries.Count == 0)
            return new RetrievalMetrics(0, 0, 0, 0, k, 0);

        var samples = new (GoldenQuery Gold, IReadOnlyList<RetrievalResult> Hits)[queries.Count];

        for (var i = 0; i < queries.Count; i++)
        {
            var gold = queries[i];
            var retrievalQuery = new RetrievalQuery(gold.Query, k);
            var hits = await _retrievalService.SearchAsync(retrievalQuery, cancellationToken).ConfigureAwait(false);
            samples[i] = (gold, hits);
        }

        return RetrievalEvaluator.Evaluate(samples, k);
    }
}
