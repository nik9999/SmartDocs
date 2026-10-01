using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class UpsertTests
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
    public async Task SaveAsync_SameDocumentId_ReplacesPreviousData()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var docId = Guid.NewGuid();

        var doc1 = new Document(docId, "Title 1", "Content 1", "source1", new Dictionary<string, string>());
        var chunks1 = new[] { new DocumentChunk(docId, "Old chunk", 0) };
        await repo.SaveAsync(doc1, chunks1, CancellationToken.None);

        var doc2 = new Document(docId, "Title 2", "Content 2", "source2", new Dictionary<string, string>());
        var chunks2 = new[]
        {
            new DocumentChunk(docId, "New chunk", 0),
            new DocumentChunk(docId, "New chunk 2", 1)
        };
        await repo.SaveAsync(doc2, chunks2, CancellationToken.None);

        var result = await repo.GetAsync(docId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("Title 2", result.Document.Title);
        Assert.Equal("Content 2", result.Document.Content);
        Assert.Equal("source2", result.Document.Source);
        Assert.Equal(2, result.Chunks.Count);
        Assert.Equal("New chunk", result.Chunks[0].Text);
        Assert.Equal("New chunk 2", result.Chunks[1].Text);
    }

    [Fact]
    public async Task SaveAsync_SameDocumentId_DeletesOldChunks()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var docId = Guid.NewGuid();

        var doc1 = new Document(docId, "Title 1", "Content 1", "source1", new Dictionary<string, string>());
        var chunks1 = new[]
        {
            new DocumentChunk(docId, "Chunk A", 0),
            new DocumentChunk(docId, "Chunk B", 1),
            new DocumentChunk(docId, "Chunk C", 2)
        };
        await repo.SaveAsync(doc1, chunks1, CancellationToken.None);

        var doc2 = new Document(docId, "Title 2", "Content 2", "source2", new Dictionary<string, string>());
        var chunks2 = new[] { new DocumentChunk(docId, "Only chunk", 0) };
        await repo.SaveAsync(doc2, chunks2, CancellationToken.None);

        var result = await repo.GetAsync(docId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Chunks);
        Assert.Equal("Only chunk", result.Chunks[0].Text);
    }

    [Fact]
    public async Task SaveAsync_SameDocumentId_DeletesOldMetadata()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var docId = Guid.NewGuid();

        var metadata1 = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
        var doc1 = new Document(docId, "Title 1", "Content 1", "source1", metadata1);
        await repo.SaveAsync(doc1, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var metadata2 = new Dictionary<string, string> { { "key3", "value3" } };
        var doc2 = new Document(docId, "Title 2", "Content 2", "source2", metadata2);
        await repo.SaveAsync(doc2, Array.Empty<DocumentChunk>(), CancellationToken.None);

        var result = await repo.GetAsync(docId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result.Chunks);
        Assert.Single(result.Document.Metadata);
        Assert.Equal("value3", result.Document.Metadata["key3"]);
        Assert.DoesNotContain("key1", result.Document.Metadata.Keys);
        Assert.DoesNotContain("key2", result.Document.Metadata.Keys);
    }
}
