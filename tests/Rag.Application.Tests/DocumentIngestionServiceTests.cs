using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Xunit;

namespace Rag.Application.Tests;

public class DocumentIngestionServiceTests
{
    private sealed class FakeParser : IDocumentParser
    {
        public string? LastContentType { get; private set; }

        public bool CanParse(string contentType)
        {
            LastContentType = contentType;
            return true;
        }

        public Task<Document> ParseAsync(DocumentSource source, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Document("Test", "Content", source.Name));
        }
    }

    private sealed class FakeChunker : IChunker
    {
        public Document? LastDocument { get; private set; }

        public IReadOnlyList<DocumentChunk> Chunk(Document document)
        {
            LastDocument = document;
            return new[] { new DocumentChunk(document.DocumentId, "Chunk text", 0) };
        }
    }

    [Fact]
    public async Task IngestAsync_ReturnsIngestionResultWithDocumentAndChunks()
    {
        // Arrange
        var parser = new FakeParser();
        var chunker = new FakeChunker();
        var service = new DocumentIngestionService(new[] { parser }, chunker);
        var source = new DocumentSource("test.pdf", "application/pdf", Array.Empty<byte>());

        // Act
        var result = await service.IngestAsync(source, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.Document.Title);
        Assert.Single(result.Chunks);
        Assert.Equal("Chunk text", result.Chunks[0].Text);
    }

    [Fact]
    public async Task IngestAsync_UsesCorrectParser()
    {
        // Arrange
        var parser = new FakeParser();
        var chunker = new FakeChunker();
        var service = new DocumentIngestionService(new[] { parser }, chunker);
        var source = new DocumentSource("test.txt", "text/plain", Array.Empty<byte>());

        // Act
        await service.IngestAsync(source, CancellationToken.None);

        // Assert
        Assert.Equal("text/plain", parser.LastContentType);
    }

    [Fact]
    public async Task IngestAsync_ThrowsWhenNoParserFound()
    {
        // Arrange
        var service = new DocumentIngestionService(Array.Empty<IDocumentParser>(), new FakeChunker());
        var source = new DocumentSource("test.xyz", "application/xyz", Array.Empty<byte>());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.IngestAsync(source, CancellationToken.None));
        Assert.Contains("No parser found", exception.Message);
    }

    [Fact]
    public async Task IngestAsync_ChunkerReceivesParsedDocument()
    {
        // Arrange
        var parser = new FakeParser();
        var chunker = new FakeChunker();
        var service = new DocumentIngestionService(new[] { parser }, chunker);
        var source = new DocumentSource("test.pdf", "application/pdf", Array.Empty<byte>());

        // Act
        await service.IngestAsync(source, CancellationToken.None);

        // Assert
        Assert.NotNull(chunker.LastDocument);
        Assert.Equal("Test", chunker.LastDocument!.Title);
    }
}
