using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class Fts5SearchTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_fts_search_test_{Guid.NewGuid()}.db");
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
    public async Task SearchAsync_FindsCyrillicText()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Датчик", "Техническая документация", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 2 составляет 15.4 В.", 0)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Измеренное значение Канал 2", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal("Измеренное значение Канал 2 составляет 15.4 В.", results[0].Text);
        Assert.Equal(RetrievalSource.Sparse, results[0].Source);
        Assert.Equal(1, results[0].Rank);
    }

    [Fact]
    public async Task SearchAsync_FindsMultipleRussianTerms()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Мониторинг", "Данные мониторинга", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Давление в системе составляет 2.5 МПа.", 0),
            new DocumentChunk(doc.DocumentId, "Температура технологического процесса 85 градусов.", 1),
            new DocumentChunk(doc.DocumentId, "Аварийный сигнал активирован при превышении порога.", 2),
            new DocumentChunk(doc.DocumentId, "Канал связи работает в штатном режиме.", 3)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act & Assert — each term should find its chunk
        var pressureQuery = new RetrievalQuery("давление", 10);
        var pressureResults = await retriever.SearchAsync(pressureQuery, CancellationToken.None);
        Assert.Single(pressureResults);
        Assert.Contains("давление", pressureResults[0].Text.ToLower());

        var tempQuery = new RetrievalQuery("температура", 10);
        var tempResults = await retriever.SearchAsync(tempQuery, CancellationToken.None);
        Assert.Single(tempResults);
        Assert.Contains("температура", tempResults[0].Text.ToLower());

        var alarmQuery = new RetrievalQuery("аварийный сигнал", 10);
        var alarmResults = await retriever.SearchAsync(alarmQuery, CancellationToken.None);
        Assert.Single(alarmResults);
        Assert.Contains("аварийный", alarmResults[0].Text.ToLower());

        var channelQuery = new RetrievalQuery("Канал", 10);
        var channelResults = await retriever.SearchAsync(channelQuery, CancellationToken.None);
        Assert.Single(channelResults);
        Assert.Contains("Канал", channelResults[0].Text);
    }

    [Fact]
    public async Task SearchAsync_MultipleChunks_ReturnsRelevant()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Каналы", "Данные каналов", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 1", 0),
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 2", 1),
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 3", 2),
            new DocumentChunk(doc.DocumentId, "Температура технологического процесса", 3)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — search for "Канал 2"
        var query = new RetrievalQuery("Канал 2", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal("Измеренное значение Канал 2", results[0].Text);
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
            new DocumentChunk(doc.DocumentId, "Текст четвёртый", 3),
            new DocumentChunk(doc.DocumentId, "Текст пятый", 4)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — TopK = 2
        var query = new RetrievalQuery("Текст", 2);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_TopK_1_ReturnsSingleResult()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Текст первый", 0),
            new DocumentChunk(doc.DocumentId, "Текст второй", 1)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Текст", 1);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public async Task SearchAsync_Ranking_BestResultFirst()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура технологического процесса", 0),
            new DocumentChunk(doc.DocumentId, "Температура воздуха", 1),
            new DocumentChunk(doc.DocumentId, "Давление в системе", 2)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — search for "Температура" which matches 2 chunks
        var query = new RetrievalQuery("Температура", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — at least 2 results, first should have better (higher) score
        Assert.True(results.Count >= 2, "Expected at least 2 results for 'Температура'");
        Assert.True(results[0].Score >= results[1].Score,
            $"Best result should have higher score: {results[0].Score} >= {results[1].Score}");
    }

    [Fact]
    public async Task SearchAsync_UnknownQuery_ReturnsEmpty()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Измеренное значение Канал 2", 0)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("НесуществующийТермин123", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyText_ReturnsEmpty()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();

        // Act & Assert — RetrievalQuery constructor validates text is not empty/whitespace
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            retriever.SearchAsync(new RetrievalQuery("", 10), CancellationToken.None));
        Assert.Contains("Query text", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceText_ReturnsEmpty()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            retriever.SearchAsync(new RetrievalQuery("   ", 10), CancellationToken.None));
        Assert.Contains("Query text", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_ResultMapping_IsCorrect()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunkId = Guid.NewGuid();
        var chunks = new[]
        {
            new DocumentChunk(chunkId, doc.DocumentId, "Тестовый текст", 0, ChunkMetadata.Empty)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act
        var query = new RetrievalQuery("Тестовый", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal(doc.DocumentId, results[0].DocumentId);
        Assert.Equal(chunkId, results[0].ChunkId);
        Assert.Equal("Тестовый текст", results[0].Text);
        Assert.True(results[0].Score > 0, "Score should be positive (higher = better)");
        Assert.Equal(1, results[0].Rank);
        Assert.Equal(RetrievalSource.Sparse, results[0].Source);
        Assert.NotNull(results[0].Metadata);
    }

    [Fact]
    public async Task SearchAsync_ScoreHigherIsBetter()
    {
        // Arrange
        var sp = await CreateInitializedServiceProvider();
        var retriever = sp.GetRequiredService<ISparseRetriever>();
        var doc = new Document("Тест", "Тестовые данные", "source", new Dictionary<string, string>());
        var chunks = new[]
        {
            new DocumentChunk(doc.DocumentId, "Температура технологического процесса", 0),
            new DocumentChunk(doc.DocumentId, "Температура воздуха", 1)
        };

        await SaveDocumentAndChunks(sp, doc, chunks);

        // Act — "Температура" matches both, exact match should rank higher
        var query = new RetrievalQuery("Температура", 10);
        var results = await retriever.SearchAsync(query, CancellationToken.None);

        // Assert — bm25 returns lower values for better results,
        // we negate to make higher score = better
        Assert.True(results.Count >= 2);
        Assert.True(results[0].Score > results[1].Score,
            "First result should have higher score than second");
    }
}
