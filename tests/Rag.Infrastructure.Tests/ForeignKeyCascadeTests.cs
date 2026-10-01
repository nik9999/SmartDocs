using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class ForeignKeyCascadeTests
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
    public async Task ForeignKeys_AreEnabled()
    {
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);

        using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys";
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        Assert.Equal("1", result.ToString());
    }

    [Fact]
    public async Task SaveAsync_CascadesMetadataAndChunks()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Title", "Content", "source");
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Chunk text", 0,
                new ChunkMetadata(pageNumber: 1, section: "Intro", tokenCount: 50))
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        var result = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Single(result.Chunks);
    }

    [Fact]
    public async Task SaveAsync_WithEmbeddingsTable_SchemaReady()
    {
        var sp = await CreateInitializedServiceProvider();
        var db = sp.GetRequiredService<SqliteDatabase>();

        using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Embeddings'";
        var scalar = await cmd.ExecuteScalarAsync();
        var count = Convert.ToInt64(scalar);
        Assert.Equal(1, count);
    }
}
