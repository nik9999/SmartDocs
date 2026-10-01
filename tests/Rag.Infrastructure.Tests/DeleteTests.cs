using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class DeleteTests
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
    public async Task DeleteAsync_RemovesDocument()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var doc = new Document("Test", "Content", "source");
        var chunks = new[] { new DocumentChunk(doc.DocumentId, "Chunk text", 0) };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Verify document exists
        var before = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.NotNull(before);

        // Delete
        await repo.DeleteAsync(doc.DocumentId, CancellationToken.None);

        // Verify document is gone
        var after = await repo.GetAsync(doc.DocumentId, CancellationToken.None);
        Assert.Null(after);
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent()
    {
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var nonExistentId = Guid.NewGuid();

        // Should not throw
        await repo.DeleteAsync(nonExistentId, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteAsync_CascadesToMetadataAndChunks()
    {
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var sp = CreateServiceProvider(connectionString);
        var initializer = sp.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var db = sp.GetRequiredService<SqliteDatabase>();

        var metadata = new Dictionary<string, string> { { "key", "value" } };
        var doc = new Document("Title", "Content", "source", metadata);
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Chunk 1", 0),
            new DocumentChunk(doc.DocumentId, "Chunk 2", 1)
        };

        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Delete the document
        await repo.DeleteAsync(doc.DocumentId, CancellationToken.None);

        // Verify via direct SQL that cascade deleted metadata and chunks
        await using var connection = db.CreateConnection();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM DocumentMetadata WHERE DocumentId = @docId";
            cmd.Parameters.AddWithValue("@docId", doc.DocumentId.ToString("D"));
            var metadataCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, metadataCount);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Chunks WHERE DocumentId = @docId";
            cmd.Parameters.AddWithValue("@docId", doc.DocumentId.ToString("D"));
            var chunksCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, chunksCount);
        }
    }
}
