namespace Rag.Application.Generation;

using Rag.Core.Generation;

/// <summary>
/// Application use case for RAG ask.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Answers a user query using retrieval-augmented generation.
    /// </summary>
    Task<RagResponse> AskAsync(
        string query,
        CancellationToken cancellationToken);
}
