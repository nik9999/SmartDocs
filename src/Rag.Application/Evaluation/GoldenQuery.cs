namespace Rag.Application.Evaluation;

/// <summary>
/// Represents a single golden (ground-truth) query used for retrieval evaluation.
/// </summary>
/// <param name="Query">The user query text to evaluate.</param>
/// <param name="ExpectedDocumentIds">Set of document IDs that should be considered relevant for this query.</param>
/// <param name="ReferenceAnswer">Optional reference answer text. Not used by retrieval metrics.</param>
public sealed record GoldenQuery(
    string Query,
    IReadOnlySet<Guid> ExpectedDocumentIds,
    string? ReferenceAnswer = null)
{
    /// <summary>
    /// Creates a new instance of <see cref="GoldenQuery"/> with validation.
    /// </summary>
    /// <param name="query">The user query text to evaluate.</param>
    /// <param name="expectedDocumentIds">Set of document IDs that should be considered relevant for this query.</param>
    /// <param name="referenceAnswer">Optional reference answer text. Not used by retrieval metrics.</param>
    /// <returns>A new <see cref="GoldenQuery"/> instance.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="expectedDocumentIds"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="query"/> is empty or whitespace.
    /// </exception>
    public static GoldenQuery Create(string query, IReadOnlySet<Guid> expectedDocumentIds, string? referenceAnswer = null)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty or whitespace.", nameof(query));
        if (expectedDocumentIds == null)
            throw new ArgumentNullException(nameof(expectedDocumentIds));

        return new GoldenQuery(query, expectedDocumentIds, referenceAnswer);
    }
}
