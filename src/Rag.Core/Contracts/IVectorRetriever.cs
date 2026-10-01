namespace Rag.Core.Contracts;

using Rag.Core.Retrieval;

/// <summary>
/// Retrieves results via dense/vector matching.
/// </summary>
public interface IVectorRetriever
{
    /// <summary>
    /// Searches for matching chunks using vector similarity.
    /// </summary>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken);
}
