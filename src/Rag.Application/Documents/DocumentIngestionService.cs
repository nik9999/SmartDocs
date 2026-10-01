namespace Rag.Application.Documents;

using Rag.Core.Contracts;
using Rag.Core.Documents;

/// <summary>
/// Orchestrates document ingestion: parsing and chunking.
/// </summary>
public sealed class DocumentIngestionService : IIngestionService
{
    private readonly IEnumerable<IDocumentParser> _parsers;
    private readonly IChunker _chunker;

    public DocumentIngestionService(
        IEnumerable<IDocumentParser> parsers,
        IChunker chunker)
    {
        _parsers = parsers;
        _chunker = chunker;
    }

    public async Task<IngestionResult> IngestAsync(
        DocumentSource source,
        CancellationToken cancellationToken)
    {
        var parser = _parsers.FirstOrDefault(p => p.CanParse(source.ContentType))
            ?? throw new InvalidOperationException(
                $"No parser found for content type '{source.ContentType}'.");

        var document = await parser.ParseAsync(source, cancellationToken)
            .ConfigureAwait(false);

        var chunks = _chunker.Chunk(document);

        return new IngestionResult(document, chunks);
    }
}
