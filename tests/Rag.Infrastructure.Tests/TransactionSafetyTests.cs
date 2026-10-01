using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class TransactionSafetyTests
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
    public async Task SaveAsync_ValidData_PersistsAll()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Chunk 1", 0),
            new DocumentChunk(doc.DocumentId, "Chunk 2", 1)
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, result.Chunks.Count);
    }

    [Fact]
    public async Task SaveAsync_RollbackOnInvalidChunkDocumentId()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var wrongDocId = Guid.NewGuid();
        var chunks = new[]
        {
            new DocumentChunk(wrongDocId, "Chunk 1", 0), // Wrong DocumentId
            new DocumentChunk(doc.DocumentId, "Chunk 2", 1)
        };

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            repo.SaveAsync(doc, chunks, CancellationToken.None));

        // Verify nothing was saved
        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.Null(result);
    }
}
