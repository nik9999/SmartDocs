using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Preprocessing;

/// <summary>
/// Preprocesses and normalizes document text prior to chunking and embedding.
/// Eliminates extraction artifacts such as word-wrap hyphens, blank/boilerplate pages,
/// dot leaders, and repeating running headers/footers.
/// </summary>
public interface IDocumentPreprocessor
{
    /// <summary>
    /// Cleans and normalizes the given <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The source document to preprocess.</param>
    /// <returns>A new <see cref="Document"/> instance with sanitized content, or the original if unchanged.</returns>
    Document Preprocess(Document document);
}
