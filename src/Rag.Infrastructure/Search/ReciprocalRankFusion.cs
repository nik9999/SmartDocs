namespace Rag.Infrastructure.Search;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF) implementation of IResultFusion.
/// Combines results from multiple retrievers (sparse + dense) using RRF scoring.
/// Uses 1-based ranking: 1 / (K + rank) where rank = 1, 2, 3, ...
/// </summary>
public sealed class ReciprocalRankFusion : IResultFusion
{
    /// <summary>
    /// RRF constant. A larger k means lower-ranked results decay faster.
    /// 60 is the standard value recommended in the original RRF paper.
    /// </summary>
    private const double RrfConstant = 60.0;

    /// <summary>
    /// Fuses multiple result sets into one ordered list using Reciprocal Rank Fusion.
    /// </summary>
    /// <param name="resultSets">Result sets from different retrievers.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <returns>Fused and deduplicated results sorted by RRF score descending.</returns>
    public IReadOnlyList<RetrievalResult> Fuse(
        IReadOnlyList<IReadOnlyList<RetrievalResult>> resultSets,
        int topK)
    {
        if (resultSets == null)
            throw new ArgumentNullException(nameof(resultSets));

        if (topK < 1)
            throw new ArgumentException("topK must be at least 1.", nameof(topK));

        // Dictionary: ChunkId -> accumulated RRF score
        var rrfScores = new Dictionary<Guid, double>();

        // Accumulate RRF scores from each result set
        foreach (var resultSet in resultSets)
        {
            if (resultSet == null)
                continue;

            // Use 1-based rank: position 0 in the list = rank 1
            for (int position = 0; position < resultSet.Count; position++)
            {
                var result = resultSet[position];
                var chunkId = result.ChunkId;

                // RRF contribution: 1 / (k + rank) where rank is 1-based
                double rank = position + 1;
                double contribution = 1.0 / (RrfConstant + rank);

                if (rrfScores.TryGetValue(chunkId, out var existingScore))
                {
                    rrfScores[chunkId] = existingScore + contribution;
                }
                else
                {
                    rrfScores[chunkId] = contribution;
                }
            }
        }

        // If no results at all, return empty
        if (rrfScores.Count == 0)
            return Array.Empty<RetrievalResult>();

        // Get the best result for each ChunkId (the one with highest original score)
        var bestResults = new Dictionary<Guid, RetrievalResult>();
        foreach (var resultSet in resultSets)
        {
            if (resultSet == null)
                continue;

            foreach (var result in resultSet)
            {
                if (!bestResults.TryGetValue(result.ChunkId, out var best) ||
                    result.Score > best.Score)
                {
                    bestResults[result.ChunkId] = result;
                }
            }
        }

        // Sort by RRF score descending, then by ChunkId for deterministic ordering
        var sorted = rrfScores
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(topK)
            .Select(x =>
            {
                // Update the source to Hybrid for fused results
                var best = bestResults[x.Key];
                return new RetrievalResult(
                    best.DocumentId,
                    best.ChunkId,
                    best.Text,
                    best.Score,
                    best.Rank,
                    RetrievalSource.Hybrid,
                    best.Metadata);
            })
            .ToList();

        return sorted;
    }
}
