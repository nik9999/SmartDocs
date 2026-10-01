using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class DocumentPersistenceTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_test_{Guid.NewGuid()}.db");
    }

    private IServiceProvider CreateServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(connectionString));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDocumentRepository, Infrastructure.Persistence.Repositories.DocumentRepository>(sp =>
            new Infrastructure.Persistence.Repositories.DocumentRepository(connectionString));
        return services.BuildServiceProvider();
    }

    private async Task<IServiceProvider> CreateInitializedServiceProvider()
    {
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var sp = CreateServiceProvider(connectionString);
        var initializer = sp.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();
        return sp;
    }

    [Fact]
    public async Task SaveAsync_SavesDocument()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test Title", "Test Content", "test-source");

        await repo.SaveAsync(doc, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Test Title", result.Document.Title);
        Assert.Equal("Test Content", result.Document.Content);
        Assert.Equal("test-source", result.Document.Source);
    }

    [Fact]
    public async Task SaveAsync_SavesChunks()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk(doc.DocumentId, "Chunk 1 text", 0),
            new DocumentChunk(doc.DocumentId, "Chunk 2 text", 1),
            new DocumentChunk(doc.DocumentId, "Chunk 3 text", 2)
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal("Chunk 1 text", result.Chunks[0].Text);
        Assert.Equal("Chunk 2 text", result.Chunks[1].Text);
        Assert.Equal("Chunk 3 text", result.Chunks[2].Text);
    }

    [Fact]
    public async Task SaveAsync_ChunksReturnedInPositionOrder()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk(doc.DocumentId, "Third", 2),
            new DocumentChunk(doc.DocumentId, "First", 0),
            new DocumentChunk(doc.DocumentId, "Second", 1)
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("First", result.Chunks[0].Text);
        Assert.Equal("Second", result.Chunks[1].Text);
        Assert.Equal("Third", result.Chunks[2].Text);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForNonExistentDocument()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var nonExistentId = Guid.NewGuid();

        var result = await repo.GetAsync(nonExistentId, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_RoundTripWithMetadataAndChunks()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var doc = new Document("Title", "Content", "source", metadata);
        var chunk = new DocumentChunk(
            doc.DocumentId,
            "chunk text",
            0,
            new ChunkMetadata(pageNumber: 5, section: "Intro", tokenCount: 100));

        await repo.SaveAsync(doc, new[] { chunk }, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Title", result.Document.Title);
        Assert.Single(result.Chunks);
        Assert.Equal(5, result.Chunks[0].Metadata.PageNumber);
        Assert.Equal("Intro", result.Chunks[0].Metadata.Section);
        Assert.Equal(100, result.Chunks[0].Metadata.TokenCount);
    }
}
