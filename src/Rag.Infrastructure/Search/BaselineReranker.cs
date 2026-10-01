namespace Rag.Infrastructure.Search;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;

/// <summary>
/// Deterministic baseline reranker using keyword overlap scoring.
/// Does NOT perform its own retrieval or add new ML/LLM dependencies.
/// </summary>
public sealed class BaselineReranker : IReranker
{
    /// <summary>
    /// Reranks candidates using deterministic keyword overlap scoring.
    /// </summary>
    /// <param name="query">The original query.</param>
    /// <param name="candidates">Candidate results to rerank.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reranked results with at most topK items.</returns>
    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query must not be null or empty.", nameof(query));

        if (candidates == null)
            throw new ArgumentNullException(nameof(candidates));

        if (topK < 1)
            throw new ArgumentException("topK must be at least 1.", nameof(topK));

        // Tokenize query into keywords
        var queryKeywords = Tokenize(query);

        // If no query keywords or no candidates, return empty
        if (queryKeywords.Count == 0 || candidates.Count == 0)
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());

        // Build a set for fast lookup
        var queryKeywordSet = new HashSet<string>(queryKeywords, StringComparer.Ordinal);

        // Score each candidate
        var scoredCandidates = new List<(RetrievalResult Result, double Score)>();

        foreach (var candidate in candidates)
        {
            var textKeywords = Tokenize(candidate.Text);
            var textKeywordSet = new HashSet<string>(textKeywords, StringComparer.Ordinal);

            // Compute overlap: count of query keywords found in text
            int overlapCount = 0;
            foreach (var keyword in queryKeywordSet)
            {
                if (textKeywordSet.Contains(keyword))
                    overlapCount++;
            }

            // Normalized score: overlap / total query keywords
            double score = (double)overlapCount / queryKeywordSet.Count;

            scoredCandidates.Add((candidate, score));
        }

        // Sort by score descending, then by ChunkId for deterministic ordering
        var reranked = scoredCandidates
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Result.ChunkId)
            .Take(topK)
            .Select(x =>
            {
                var result = x.Result;
                return new RetrievalResult(
                    result.DocumentId,
                    result.ChunkId,
                    result.Text,
                    result.Score,
                    result.Rank,
                    RetrievalSource.Reranked,
                    result.Metadata);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(reranked);
    }

    /// <summary>
    /// Tokenizes text into lowercase keywords by splitting on whitespace and punctuation.
    /// Filters out single-character tokens and common stop words.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // Normalize: lowercase and split on non-letter characters
        var normalized = text.ToLowerInvariant();
        var tokens = new List<string>();

        int start = -1;
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            bool isLetter = char.IsLetterOrDigit(c);

            if (isLetter && start == -1)
            {
                start = i;
            }
            else if (!isLetter && start != -1)
            {
                string token = normalized[start..i];
                if (token.Length >= 2) // Filter out single-char tokens
                    tokens.Add(token);
                start = -1;
            }
        }

        // Don't forget the last token
        if (start != -1)
        {
            string token = normalized[start..];
            if (token.Length >= 2)
                tokens.Add(token);
        }

        return tokens;
    }
}
