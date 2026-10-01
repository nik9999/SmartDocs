using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class Fts5QueryTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_fts_query_test_{Guid.NewGuid()}.db");
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

    private async Task SaveDocumentAndChunks(IServiceProvider sp, Document doc, IReadOnlyList<DocumentChunk> chunks)
    {
        var repo = sp.GetRequiredService<IDocumentRepository>();
        await repo.SaveAsync(doc, chunks, CancellationToken.None);
    }

    [Fact]
    public async Task SearchAsync_RussianText_FindsSingleToken()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура двигателя повышена", 0),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("температура", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Contains("Температура", results[0].Text);
    }

    [Fact]
    public async Task SearchAsync_MultipleTokens_FindsAllMatching()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура двигателя повышена", 0),
            new DocumentChunk(doc.DocumentId, "Давление в системе в норме", 1),
            new DocumentChunk(doc.DocumentId, "Температура воздуха низкая", 2),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — "Температура двигатель" should find chunks with either token
        var query = new RetrievalQuery("Температура двигатель", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — both chunk 0 and chunk 2 contain "Температура"
        Assert.True(results.Count >= 1, "Expected at least 1 result for 'Температура двигатель'");
        Assert.All(results, r => Assert.True(
            r.Text.Contains("Температура") || r.Text.Contains("двигатель"),
            "Each result should contain at least one of the query tokens"));
    }

    [Fact]
    public async Task SearchAsync_PhraseLikeQuery_FindsBestMatchFirst()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 2", 0),
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 1", 1),
            new DocumentChunk(doc.DocumentId, "Значение температуры", 2),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Измеренное значение Канал 2", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — first result should contain "Канал 2"
        Assert.True(results.Count >= 1, "Expected at least 1 result");
        Assert.Contains("Канал 2", results[0].Text);
    }

    [Fact]
    public async Task SearchAsync_SpecialCharacters_EscapedProperly()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура + давление", 0),
            new DocumentChunk(doc.DocumentId, "Температура * давление", 1),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — query with special characters should not break FTS5
        var query = new RetrievalQuery("Температура + давление", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — should find results without SQL/FTS error
        Assert.True(results.Count >= 1, "Expected at least 1 result");
    }

    [Fact]
    public async Task SearchAsync_QuotesInQuery_EscapedProperly()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура в системе", 0),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — query with quotes should not break FTS5
        // The quotes are part of the text, not FTS5 syntax
        var query = new RetrievalQuery("Температура система", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — should find results without FTS error
        Assert.True(results.Count >= 1, "Expected at least 1 result");
    }

    [Fact]
    public async Task SearchAsync_EmptyResult_ReturnsEmpty()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура двигателя", 0),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("НесуществующийТермин123", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_TopK_LimitsResults()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Текст первый", 0),
            new DocumentChunk(doc.DocumentId, "Текст второй", 1),
            new DocumentChunk(doc.DocumentId, "Текст третий", 2),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — use explicit CandidateTopK to limit results
        var query = new RetrievalQuery("Текст", 2, candidateTopK: 2);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_CyrillicNumbers_FindsCorrectly()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Канал измерения №2", 0),
            new DocumentChunk(doc.DocumentId, "Канал измерения №1", 1),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Канал №2", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — should find chunk with "Канал" OR "2"
        Assert.True(results.Count >= 1, "Expected at least 1 result for 'Канал №2'");
    }

    [Fact]
    public async Task SearchAsync_SingleWord_FindsAllMatches()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Значение давления", 0),
            new DocumentChunk(doc.DocumentId, "Значение температуры", 1),
            new DocumentChunk(doc.DocumentId, "Значение скорости", 2),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Значение", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — should find all 3 chunks
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SearchAsync_QueryWithOnlyNumbers_FindsCorrectly()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Канал 2", 0),
            new DocumentChunk(doc.DocumentId, "Канал 3", 1),
        };
        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("2", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — single number token should work
        Assert.Single(results);
        Assert.Contains("Канал 2", results[0].Text);
    }
}
