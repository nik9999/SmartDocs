namespace Rag.Core.Contracts;

using Rag.Core.Documents;

/// <summary>
/// Splits a document into chunks.
/// </summary>
public interface IChunker
{
    /// <summary>
    /// Chunks the given document.
    /// </summary>
    IReadOnlyList<DocumentChunk> Chunk(Document document);
}
