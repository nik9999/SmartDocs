namespace Rag.Core.Contracts;

using Rag.Core.Retrieval;

/// <summary>
/// Retrieves results via sparse/lexical matching.
/// </summary>
public interface ISparseRetriever
{
    /// <summary>
    /// Searches for matching chunks using sparse retrieval.
    /// </summary>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken);
}
