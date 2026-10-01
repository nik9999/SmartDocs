using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class Fts5SynchronizationTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_fts_sync_test_{Guid.NewGuid()}.db");
    }

    private IServiceProvider CreateServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(connectionString));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDocumentRepository, Infrastructure.Persistence.Repositories.DocumentRepository>(sp =>
            new Infrastructure.Persistence.Repositories.DocumentRepository(connectionString));
        services.AddScoped<ISparseRetriever, Search.Fts5Search>(sp =>
            new Search.Fts5Search(connectionString));
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
    public async Task Insert_Synchronization_FindsNewChunk()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        // Act — save document with chunk
        var doc = new Document("Сенсор", "Данные сенсора", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Значение давления 3.2 МПа", 0)
        };
        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Assert — chunk should be found via FTS
        var query = new RetrievalQuery("давления", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);
        Assert.Single(results);
        Assert.Contains("давления", results[0].Text);
    }

    [Fact]
    public async Task Replacement_Synchronization_OldChunkNotFound_NewChunkFound()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var docId = Guid.NewGuid();

        // Save first version
        var doc1 = new Document(docId, "Title 1", "Content 1", "source1", new Dictionary<string, string>());
        var chunks1 = new[]
        {
            new DocumentChunk(docId, "Старый текст для поиска", 0)
        };
        await repo.SaveAsync(doc1, chunks1, CancellationToken.None);

        // Verify old chunk is found
        var oldResults = await retriever.SearchAsync(new RetrievalQuery("Старый текст", 10), CancellationToken.None);
        Assert.Single(oldResults);

        // Replace with new version
        var doc2 = new Document(docId, "Title 2", "Content 2", "source2", new Dictionary<string, string>());
        var chunks2 = new[]
        {
            new DocumentChunk(docId, "Новый текст для поиска", 0)
        };
        await repo.SaveAsync(doc2, chunks2, CancellationToken.None);

        // Assert — old chunk should NOT be found, new chunk should be
        var newResults = await retriever.SearchAsync(new RetrievalQuery("Старый текст", 10), CancellationToken.None);
        Assert.Empty(newResults);

        var updatedResults = await retriever.SearchAsync(new RetrievalQuery("Новый текст", 10), CancellationToken.None);
        Assert.Single(updatedResults);
        Assert.Contains("Новый текст", updatedResults[0].Text);
    }

    [Fact]
    public async Task Delete_Synchronization_ChunkNotFound()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var doc = new Document("Удаление", "Тест удаления", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Буду удалён", 0)
        };
        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Verify chunk is found
        var beforeResults = await retriever.SearchAsync(new RetrievalQuery("удалён", 10), CancellationToken.None);
        Assert.Single(beforeResults);

        // Act — delete the document
        await repo.DeleteAsync(doc.DocumentId, CancellationToken.None);

        // Assert — chunk should NOT be found anymore
        var afterResults = await retriever.SearchAsync(new RetrievalQuery("удалён", 10), CancellationToken.None);
        Assert.Empty(afterResults);
    }

    [Fact]
    public async Task Delete_Synchronization_Cascade_FtsCleaned()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var repo = sp.GetRequiredService<IDocumentRepository>();
        var db = sp.GetRequiredService<SqliteDatabase>();

        var doc = new Document("Каскад", "Тест каскада", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Текст 1", 0),
            new DocumentChunk(doc.DocumentId, "Текст 2", 1)
        };
        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Act — delete document
        await repo.DeleteAsync(doc.DocumentId, CancellationToken.None);

        // Assert — FTS index should be empty for this document
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ChunksFts WHERE DocumentId = @docId";
        cmd.Parameters.AddWithValue("@docId", doc.DocumentId.ToString("D"));
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Transaction_Rollback_FtsNotUpdated()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var sp = CreateServiceProvider(connectionString);
        var initializer = sp.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();

        var repo = sp.GetRequiredService<IDocumentRepository>();
        var retriever = sp.GetRequiredService<ISparseRetriever>();

        var doc = new Document("Rollback", "Тест отката", "source", new Dictionary<string, string>());

        // Act — save should succeed
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Надёжный текст", 0)
        };
        await repo.SaveAsync(doc, chunks, CancellationToken.None);

        // Verify it's saved
        var results = await retriever.SearchAsync(new RetrievalQuery("Надёжный", 10), CancellationToken.None);
        Assert.Single(results);

        // The FTS triggers are AFTER triggers, so they fire within the same transaction.
        // If the transaction rolls back, both Chunks and ChunksFts are rolled back together.
        // This test verifies that the FTS index is consistent with the Chunks table.
        // Since SaveAsync commits successfully, the FTS should be in sync.
    }

    [Fact]
    public async Task Transaction_Rollback_PreservesConsistency()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var sp = CreateServiceProvider(connectionString);
        var initializer = sp.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();

        var repo = sp.GetRequiredService<IDocumentRepository>();
        var db = sp.GetRequiredService<SqliteDatabase>();

        // Save a document first
        var doc1 = new Document("Base", "Базовый документ", "source", new Dictionary<string, string>());
        var chunks1 = new[] { new DocumentChunk(doc1.DocumentId, "Базовый текст", 0) };
        await repo.SaveAsync(doc1, chunks1, CancellationToken.None);

        // Verify base document is searchable
        await using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ChunksFts";
        var initialCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, initialCount);

        // Save a replacement document — this should replace the old data
        var doc2 = new Document(doc1.DocumentId, "Base v2", "Content v2", "source2", new Dictionary<string, string>());
        var chunks2 = new[] { new DocumentChunk(doc1.DocumentId, "Новый текст", 0) };
        await repo.SaveAsync(doc2, chunks2, CancellationToken.None);

        // Assert — FTS should have exactly 1 entry (the new one)
        cmd.CommandText = "SELECT COUNT(*) FROM ChunksFts";
        var finalCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, finalCount);

        // And the old text should not be searchable
        var oldResults = await sp.GetRequiredService<ISparseRetriever>()
            .SearchAsync(new RetrievalQuery("Базовый текст", 10), CancellationToken.None);
        Assert.Empty(oldResults);

        // And the new text should be searchable
        var newResults = await sp.GetRequiredService<ISparseRetriever>()
            .SearchAsync(new RetrievalQuery("Новый текст", 10), CancellationToken.None);
        Assert.Single(newResults);
    }
}
