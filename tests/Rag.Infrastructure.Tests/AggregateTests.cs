using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class AggregateTests
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
    public async Task SaveAsync_ReturnsDocumentAggregate()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test Title", "Test Content", "test-source");
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Chunk 1", 0),
            new DocumentChunk(doc.DocumentId, "Chunk 2", 1)
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.IsType<DocumentAggregate>(result);
        Assert.Equal(doc.DocumentId, result.Document.DocumentId);
        Assert.Equal(2, result.Chunks.Count);
    }

    [Fact]
    public async Task SaveAsync_MetadataRoundTrip()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var metadata = new Dictionary<string, string>
        {
            { "Author", "John Doe" },
            { "Version", "1.0" },
            { "Department", "Engineering" }
        };
        var doc = new Document("Report", "Annual report content", "source", metadata);

        await repo.SaveAsync(doc, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.Document.Metadata.Count);
        Assert.Equal("John Doe", result.Document.Metadata["Author"]);
        Assert.Equal("1.0", result.Document.Metadata["Version"]);
        Assert.Equal("Engineering", result.Document.Metadata["Department"]);
    }

    [Fact]
    public async Task SaveAsync_ChunkMetadataRoundTrip()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var chunk = new DocumentChunk(
            doc.DocumentId,
            "Some text",
            0,
            new ChunkMetadata(pageNumber: 42, section: "Chapter 1", tokenCount: 256));

        await repo.SaveAsync(doc, new[] { chunk }, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Chunks);
        Assert.Equal(42, result.Chunks[0].Metadata.PageNumber);
        Assert.Equal("Chapter 1", result.Chunks[0].Metadata.Section);
        Assert.Equal(256, result.Chunks[0].Metadata.TokenCount);
    }

    [Fact]
    public async Task SaveAsync_EmptyChunks_ReturnsEmptyList()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");

        await repo.SaveAsync(doc, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Chunks);
    }

    [Fact]
    public async Task SaveAsync_ReplacementPreservesNewChunksOnly()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var docId = Guid.NewGuid();

        // Save first version
        var doc1 = new Document(docId, "Title 1", "Content 1", "source1", new Dictionary<string, string>());
        var chunks1 = new[]
        {
            new DocumentChunk(docId, "Old 1", 0),
            new DocumentChunk(docId, "Old 2", 1)
        };
        await repo.SaveAsync(doc1, chunks1, CancellationToken.None);

        // Save second version with different chunks
        var doc2 = new Document(docId, "Title 2", "Content 2", "source2", new Dictionary<string, string>());
        var chunks2 = new[]
        {
            new DocumentChunk(docId, "New 1", 0)
        };
        await repo.SaveAsync(doc2, chunks2, CancellationToken.None);

        var result = await repo.GetAsync(docId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Title 2", result.Document.Title);
        Assert.Single(result.Chunks);
        Assert.Equal("New 1", result.Chunks[0].Text);
    }
}
