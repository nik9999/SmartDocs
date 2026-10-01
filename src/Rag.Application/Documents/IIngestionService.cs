namespace Rag.Application.Documents;

using Rag.Core.Contracts;
using Rag.Core.Documents;

/// <summary>
/// Application use case for document ingestion.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingests a document source and returns the parsed document with its chunks.
    /// </summary>
    Task<IngestionResult> IngestAsync(
        DocumentSource source,
        CancellationToken cancellationToken);
}
