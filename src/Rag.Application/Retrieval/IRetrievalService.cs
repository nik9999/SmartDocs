namespace Rag.Application.Retrieval;

using Rag.Core.Retrieval;

/// <summary>
/// Application use case for retrieval.
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Searches for relevant document chunks using hybrid retrieval.
    /// </summary>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken);
}
