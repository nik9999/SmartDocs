using Rag.Core.Retrieval;

namespace Rag.Application.Evaluation;

/// <summary>
/// Provides offline retrieval evaluation capabilities.
/// </summary>
/// <remarks>
/// <para>
/// All metrics are computed at the <c>DocumentId</c> level, not at the chunk level.
/// Multiple chunks belonging to the same document are treated as a single document.
/// The ranking is derived from the order of first appearance of each unique document
/// in the retrieved results.
/// </para>
/// <para>
/// nDCG uses binary relevance: a document is either relevant (1) or not relevant (0).
/// </para>
/// <para>
/// <c>K</c> is the cutoff parameter for retrieval evaluation — only the top-K results
/// (after deduplication to unique documents) are considered.
/// </para>
/// </remarks>
public static class RetrievalEvaluator
{
    /// <summary>
    /// Evaluates retrieval quality for a set of golden queries.
    /// </summary>
    /// <param name="samples">
    /// A list of tuples, each containing a <see cref="GoldenQuery"/> and the corresponding
    /// retrieved <see cref="RetrievalResult"/> hits from the retrieval system.
    /// </param>
    /// <param name="k">
    /// The cutoff parameter: only the top-K unique documents from each hit list are considered.
    /// Must be greater than zero.
    /// </param>
    /// <returns>
    /// A <see cref="RetrievalMetrics"/> instance containing Recall@K, Precision@K, MRR, and nDCG@K.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="samples"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="k"/> is less than or equal to zero.
    /// </exception>
    /// <remarks>
    /// <para>
    /// When <paramref name="samples"/> is empty, returns metrics with all values set to 0
    /// and <c>QueryCount</c> set to 0. No exception is thrown.
    /// </para>
    /// <para>
    /// Evaluation is performed at the document level: multiple chunks from the same document
    /// are treated as a single document. The ranking order is determined by the first
    /// appearance of each unique document in the retrieved results.
    /// </para>
    /// </remarks>
    public static RetrievalMetrics Evaluate(
        IEnumerable<(GoldenQuery Gold, IReadOnlyList<RetrievalResult> Hits)> samples,
        int k)
    {
        if (samples == null)
            throw new ArgumentNullException(nameof(samples));
        if (k <= 0)
            throw new ArgumentOutOfRangeException(nameof(k), "K must be greater than zero.");

        var samplesList = samples as IReadOnlyList<(GoldenQuery, IReadOnlyList<RetrievalResult>)> ?? samples.ToList();

        if (samplesList.Count == 0)
            return new RetrievalMetrics(0, 0, 0, 0, k, 0);

        var recallSum = 0.0;
        var precisionSum = 0.0;
        var mrrSum = 0.0;
        var ndcgSum = 0.0;
        var queryCount = 0;

        foreach (var (gold, hits) in samplesList)
        {
            var docIds = ExtractUniqueDocumentIds(hits);
            var topKDocIds = docIds.Take(k).ToList();

            var recall = ComputeRecall(topKDocIds, gold.ExpectedDocumentIds);
            var precision = ComputePrecision(topKDocIds, gold.ExpectedDocumentIds, k, docIds);
            var mrr = ComputeMrr(topKDocIds, gold.ExpectedDocumentIds);
            var ndcg = ComputeNdcg(topKDocIds, gold.ExpectedDocumentIds);

            recallSum += recall;
            precisionSum += precision;
            mrrSum += mrr;
            ndcgSum += ndcg;
            queryCount++;
        }

        var n = (double)queryCount;
        return new RetrievalMetrics(
            recallSum / n,
            precisionSum / n,
            mrrSum / n,
            ndcgSum / n,
            k,
            queryCount);
    }

    /// <summary>
    /// Extracts unique document IDs from retrieval results, preserving the order of first appearance.
    /// </summary>
    /// <param name="hits">The retrieved results (may contain multiple chunks per document).</param>
    /// <returns>
    /// A list of unique document IDs in the order they first appear in <paramref name="hits"/>.
    /// </returns>
    private static List<Guid> ExtractUniqueDocumentIds(IReadOnlyList<RetrievalResult> hits)
    {
        var seen = new HashSet<Guid>();
        var uniqueDocs = new List<Guid>();

        foreach (var hit in hits)
        {
            if (seen.Add(hit.DocumentId))
                uniqueDocs.Add(hit.DocumentId);
        }

        return uniqueDocs;
    }

    /// <summary>
    /// Computes Recall@K: the fraction of expected documents that appear in the top-K results.
    /// </summary>
    /// <param name="retrievedDocIds">Unique document IDs from top-K retrieval results.</param>
    /// <param name="expectedDocIds">Set of expected relevant document IDs.</param>
    /// <returns>
    /// A value in range 0..1. Returns 0 if there are no expected documents.
    /// </returns>
    private static double ComputeRecall(IReadOnlyList<Guid> retrievedDocIds, IReadOnlySet<Guid> expectedDocIds)
    {
        if (expectedDocIds.Count == 0)
            return 0;

        var relevantRetrieved = retrievedDocIds.Count(d => expectedDocIds.Contains(d));
        return (double)relevantRetrieved / expectedDocIds.Count;
    }

    /// <summary>
    /// Computes Precision@K: the fraction of retrieved documents in top-K that are relevant.
    /// </summary>
    /// <param name="retrievedDocIds">Unique document IDs from top-K retrieval results.</param>
    /// <param name="expectedDocIds">Set of expected relevant document IDs.</param>
    /// <param name="k">The cutoff parameter.</param>
    /// <param name="allUniqueDocIds">All unique document IDs from the full hit list (before K cutoff).</param>
    /// <returns>
    /// A value in range 0..1.
    /// <para>
    /// The denominator is <c>K</c> (not the actual number of retrieved documents) to penalize
    /// retrievers that return fewer than K results. This is a conservative interpretation:
    /// Precision@K = (relevant docs in top-K) / K.
    /// </para>
    /// </returns>
    private static double ComputePrecision(
        IReadOnlyList<Guid> retrievedDocIds,
        IReadOnlySet<Guid> expectedDocIds,
        int k,
        IReadOnlyList<Guid> allUniqueDocIds)
    {
        var relevantInTopK = retrievedDocIds.Count(d => expectedDocIds.Contains(d));

        // Use K as denominator to penalize retrievers that return fewer than K results.
        // This ensures that returning fewer results than requested does not artificially
        // inflate precision.
        return (double)relevantInTopK / k;
    }

    /// <summary>
    /// Computes MRR (Mean Reciprocal Rank) for a single query.
    /// </summary>
    /// <param name="topKDocIds">Unique document IDs from top-K retrieval results.</param>
    /// <param name="expectedDocIds">Set of expected relevant document IDs.</param>
    /// <returns>
    /// 1/rank of the first relevant document, or 0 if no relevant document is found.
    /// Rank is 1-based position in the top-K list.
    /// </returns>
    private static double ComputeMrr(IReadOnlyList<Guid> topKDocIds, IReadOnlySet<Guid> expectedDocIds)
    {
        for (var i = 0; i < topKDocIds.Count; i++)
        {
            if (expectedDocIds.Contains(topKDocIds[i]))
                return 1.0 / (i + 1);
        }

        return 0;
    }

    /// <summary>
    /// Computes nDCG@K (normalized Discounted Cumulative Gain at K) with binary relevance.
    /// </summary>
    /// <param name="topKDocIds">Unique document IDs from top-K retrieval results.</param>
    /// <param name="expectedDocIds">Set of expected relevant document IDs.</param>
    /// <returns>
    /// nDCG@K value in range 0..1. Returns 0 if there are no expected documents.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Binary relevance: each document is either relevant (1) or not relevant (0).
    /// </para>
    /// <para>
    /// DCG = sum(relevance_i / log2(i + 1)) for i = 1..K
    /// </para>
    /// <para>
    /// IDCG is computed as the DCG of an ideal ranking where all relevant documents
    /// appear first. If IDCG is 0 (no expected documents), nDCG is 0.
    /// </para>
    /// </remarks>
    private static double ComputeNdcg(IReadOnlyList<Guid> topKDocIds, IReadOnlySet<Guid> expectedDocIds)
    {
        if (expectedDocIds.Count == 0)
            return 0;

        var dcg = 0.0;
        for (var i = 0; i < topKDocIds.Count; i++)
        {
            var relevance = expectedDocIds.Contains(topKDocIds[i]) ? 1 : 0;
            dcg += relevance / Math.Log2(i + 2); // i+2 because i is 0-based and log2(rank+1)
        }

        // IDCG: ideal case — all expected documents at the top
        var idealCount = Math.Min(expectedDocIds.Count, topKDocIds.Count);
        var idcg = 0.0;
        for (var i = 0; i < idealCount; i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        if (idcg == 0)
            return 0;

        return dcg / idcg;
    }
}
