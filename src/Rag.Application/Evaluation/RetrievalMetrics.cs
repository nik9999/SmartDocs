namespace Rag.Application.Evaluation;

/// <summary>
/// Contains retrieval evaluation metrics computed by <see cref="RetrievalEvaluator"/>.
/// </summary>
/// <param name="RecallAtK">Recall@K: fraction of expected documents retrieved within top-K results. Range: 0..1.</param>
/// <param name="PrecisionAtK">Precision@K: fraction of retrieved documents within top-K that are relevant. Range: 0..1.</param>
/// <param name="Mrr">Mean Reciprocal Rank: 1/rank of the first relevant document, averaged across queries. Range: 0..1.</param>
/// <param name="NdcgAtK">nDCG@K: normalized discounted cumulative gain with binary relevance. Range: 0..1.</param>
/// <param name="K">The cutoff parameter used during evaluation.</param>
/// <param name="QueryCount">Total number of queries evaluated.</param>
public sealed record RetrievalMetrics(
    double RecallAtK,
    double PrecisionAtK,
    double Mrr,
    double NdcgAtK,
    int K,
    int QueryCount)
{
    /// <summary>
    /// Returns a formatted baseline report suitable for console output.
    /// </summary>
    /// <returns>A human-readable string with all metrics.</returns>
    public string FormatBaseline() =>
        "Retrieval Evaluation\r\n" +
        "====================\r\n" +
        "\r\n" +
        $"Queries: {QueryCount}\r\n" +
        $"K: {K}\r\n" +
        "\r\n" +
        $"Recall@{K}:      {RecallAtK:F4}\r\n" +
        $"Precision@{K}:   {PrecisionAtK:F4}\r\n" +
        $"MRR:             {Mrr:F4}\r\n" +
        $"nDCG@{K}:        {NdcgAtK:F4}";

    /// <summary>
    /// Returns a string representation of the metrics.
    /// </summary>
    public override string ToString() =>
        $"K={K}, Queries={QueryCount}, Recall@K={RecallAtK:F4}, Precision@K={PrecisionAtK:F4}, MRR={Mrr:F4}, nDCG@K={NdcgAtK:F4}";
}
