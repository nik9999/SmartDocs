namespace Rag.Core.Retrieval;

/// <summary>
/// Indicates the origin of a retrieval result.
/// </summary>
public enum RetrievalSource
{
    /// <summary>
    /// Result from sparse/lexical retrieval (e.g., FTS5).
    /// </summary>
    Sparse,

    /// <summary>
    /// Result from dense/vector retrieval.
    /// </summary>
    Dense,

    /// <summary>
    /// Result from hybrid retrieval (fusion of sparse and dense).
    /// </summary>
    Hybrid,

    /// <summary>
    /// Result after reranking.
    /// </summary>
    Reranked
}
