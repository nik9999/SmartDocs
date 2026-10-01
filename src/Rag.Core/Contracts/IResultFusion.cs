namespace Rag.Core.Contracts;

using Rag.Core.Retrieval;

/// <summary>
/// Fuses results from multiple retrievers into a single ordered list.
/// </summary>
public interface IResultFusion
{
    /// <summary>
    /// Fuses multiple result sets into one.
    /// </summary>
    /// <param name="resultSets">Result sets from different retrievers.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    IReadOnlyList<RetrievalResult> Fuse(
        IReadOnlyList<IReadOnlyList<RetrievalResult>> resultSets,
        int topK);
}
