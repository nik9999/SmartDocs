namespace Rag.Application.Documents;

using Rag.Core.Documents;

/// <summary>
/// Application-level result of document ingestion.
/// </summary>
public sealed record IngestionResult(
    Document Document,
    IReadOnlyList<DocumentChunk> Chunks);
