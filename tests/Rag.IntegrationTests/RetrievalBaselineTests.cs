using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Application.Evaluation;
using Rag.Application.Retrieval;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Configuration;
using Rag.Infrastructure.Embeddings;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Persistence.Repositories;
using Rag.Infrastructure.Search;
using Xunit;
using Xunit.Abstractions;

namespace Rag.IntegrationTests;

/// <summary>
/// Integration test that runs the full retrieval pipeline against a real SQLite database
/// with seed documents and computes baseline evaluation metrics.
///
/// IMPORTANT: This test requires the ONNX embedding model to be present.
/// Expected model directory: models/paraphrase-multilingual-MiniLM-L12-v2
/// Required files: model.onnx, (vocab.txt OR tokenizer.json)
///
/// If the model is not available, the test is skipped (like other embedding tests).
/// See: EmbeddingGeneratorTests.cs for the same pattern.
/// </summary>
public class RetrievalBaselineTests : IDisposable
{
    private static readonly string ModelPath = Path.Combine(
        Path.GetDirectoryName(typeof(RetrievalBaselineTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "models", "paraphrase-multilingual-MiniLM-L12-v2");

    static RetrievalBaselineTests()
    {
        ModelPath = Path.GetFullPath(ModelPath);
    }

    private readonly ITestOutputHelper _output;
    private string? _dbPath;
    private IServiceProvider? _serviceProvider;

    public RetrievalBaselineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool ModelExists()
    {
        var modelFile = Path.Combine(ModelPath, "model.onnx");
        if (!File.Exists(modelFile))
            return false;

        // vocab.txt OR tokenizer.json
        var vocabFile = Path.Combine(ModelPath, "vocab.txt");
        var tokenizerFile = Path.Combine(ModelPath, "tokenizer.json");
        return File.Exists(vocabFile) || File.Exists(tokenizerFile);
    }

    [Fact]
    public async Task Baseline_RealPipeline_MetricsComputed()
    {
        // Skip if embedding model is not available
        if (!ModelExists())
        {
            _output.WriteLine("SKIPPED: ONNX embedding model not found at 'models/paraphrase-multilingual-MiniLM-L12-v2'.");
            _output.WriteLine("Required files: model.onnx, (vocab.txt OR tokenizer.json)");
            _output.WriteLine("This is a blocking dependency for the full hybrid retrieval pipeline.");
            return;
        }

        // Arrange: setup real pipeline with seed data
        var setupResult = await SetupBaselineAsync();
        _output.WriteLine($"Setup complete: {setupResult.DocumentCount} documents, {setupResult.ChunkCount} chunks, {setupResult.EmbeddingCount} embeddings");

        // Act: run evaluation
        var runner = _serviceProvider!.GetRequiredService<RetrievalEvaluationRunner>();
        var queries = BaselineGoldenQueries.GetQueries();
        const int k = 5;

        _output.WriteLine("\n=== Retrieval Evaluation Baseline ===");
        _output.WriteLine($"Queries: {queries.Count}");
        _output.WriteLine($"K: {k}");
        _output.WriteLine("");

        var metrics = await runner.RunAsync(queries, k, CancellationToken.None);

        // Output metrics
        _output.WriteLine(metrics.FormatBaseline());

        // Output per-query diagnostics
        await OutputPerQueryDiagnosticsAsync(runner, queries, k);

        // Assert: metrics should be computed (not throw)
        Assert.True(metrics.QueryCount == queries.Count);
        Assert.True(metrics.K == k);
        Assert.True(metrics.RecallAtK >= 0 && metrics.RecallAtK <= 1);
        Assert.True(metrics.PrecisionAtK >= 0 && metrics.PrecisionAtK <= 1);
        Assert.True(metrics.Mrr >= 0 && metrics.Mrr <= 1);
        Assert.True(metrics.NdcgAtK >= 0 && metrics.NdcgAtK <= 1);

        _output.WriteLine("");
        _output.WriteLine($"Baseline completed successfully.");
        _output.WriteLine($"Recall@{k}: {metrics.RecallAtK:F4}");
        _output.WriteLine($"Precision@{k}: {metrics.PrecisionAtK:F4}");
        _output.WriteLine($"MRR: {metrics.Mrr:F4}");
        _output.WriteLine($"nDCG@{k}: {metrics.NdcgAtK:F4}");
    }

    [Fact]
    public async Task Baseline_Reproducibility_ResultsIdentical()
    {
        // Skip if embedding model is not available
        if (!ModelExists())
        {
            _output.WriteLine("SKIPPED: ONNX embedding model not found. Reproducibility test requires the full pipeline.");
            return;
        }

        // Arrange: setup real pipeline with seed data
        await SetupBaselineAsync();

        var runner = _serviceProvider!.GetRequiredService<RetrievalEvaluationRunner>();
        var queries = BaselineGoldenQueries.GetQueries();
        const int k = 5;

        // Act: run evaluation twice
        var metrics1 = await runner.RunAsync(queries, k, CancellationToken.None);
        var metrics2 = await runner.RunAsync(queries, k, CancellationToken.None);

        // Assert: results should be identical (deterministic pipeline)
        Assert.Equal(metrics1.RecallAtK, metrics2.RecallAtK);
        Assert.Equal(metrics1.PrecisionAtK, metrics2.PrecisionAtK);
        Assert.Equal(metrics1.Mrr, metrics2.Mrr);
        Assert.Equal(metrics1.NdcgAtK, metrics2.NdcgAtK);

        _output.WriteLine("Reproducibility verified: two runs produced identical metrics.");
    }

    private async Task<SetupStatistics> SetupBaselineAsync()
    {
        // Create temp database
        _dbPath = Path.Combine(Path.GetTempPath(), $"rag_baseline_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        // Build DI container with real services
        var services = new ServiceCollection();

        // Register SQLite database
        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(connectionString));

        // Register infrastructure services
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDocumentRepository, DocumentRepository>(sp =>
            new DocumentRepository(connectionString));
        services.AddScoped<ISparseRetriever, Fts5Search>(sp =>
            new Fts5Search(connectionString));

        // Register embedding generator
        var embeddingOptions = new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        };
        services.AddSingleton<IEmbeddingGenerator, LocalOnnxEmbeddingGenerator>(sp =>
            new LocalOnnxEmbeddingGenerator(embeddingOptions));
        services.AddScoped<IEmbeddingRepository, EmbeddingRepository>(sp =>
            new EmbeddingRepository(connectionString));
        services.AddScoped<IVectorRetriever, SqliteVectorRetriever>(sp =>
            new SqliteVectorRetriever(connectionString, sp.GetRequiredService<IEmbeddingGenerator>()));

        // Register fusion and reranker
        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();

        // Register application services
        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddScoped<RetrievalEvaluationRunner>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize database schema
        var initializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
        await initializer.InitializeAsync();

        // Seed documents with real content matching golden queries
        var embeddingGen = _serviceProvider.GetRequiredService<IEmbeddingGenerator>();
        var docRepo = _serviceProvider.GetRequiredService<IDocumentRepository>();
        var embRepo = _serviceProvider.GetRequiredService<IEmbeddingRepository>();

        var seedDocuments = CreateSeedDocuments();
        var stats = new SetupStatistics
        {
            DocumentCount = 0,
            ChunkCount = 0,
            EmbeddingCount = 0
        };

        foreach (var seedDoc in seedDocuments)
        {
            // Save document and chunks
            await docRepo.SaveAsync(seedDoc.Document, seedDoc.Chunks, CancellationToken.None);

            // Generate and save embeddings for each chunk
            foreach (var chunk in seedDoc.Chunks)
            {
                var embedding = await embeddingGen.GenerateAsync(chunk.Text, CancellationToken.None);
                await embRepo.SaveAsync(chunk.ChunkId, embedding, CancellationToken.None);
                stats.EmbeddingCount++;
            }
            stats.ChunkCount += seedDoc.Chunks.Count;
            stats.DocumentCount++;
        }

        return stats;
    }

    private async Task OutputPerQueryDiagnosticsAsync(
        RetrievalEvaluationRunner runner,
        IReadOnlyList<GoldenQuery> queries,
        int k)
    {
        _output.WriteLine("=== Per-Query Diagnostics ===");
        _output.WriteLine("");

        for (var i = 0; i < queries.Count; i++)
        {
            var query = queries[i];
            var retrievalQuery = new Rag.Core.Retrieval.RetrievalQuery(query.Query, k);
            var results = await _serviceProvider!
                .GetRequiredService<IRetrievalService>()
                .SearchAsync(retrievalQuery, CancellationToken.None);

            var retrievedDocIds = results
                .Select(r => r.DocumentId)
                .Distinct()
                .ToList();

            var firstRelevantRank = 0;
            for (var r = 0; r < results.Count; r++)
            {
                if (query.ExpectedDocumentIds.Contains(results[r].DocumentId))
                {
                    firstRelevantRank = r + 1;
                    break;
                }
            }

            _output.WriteLine($"Query {i + 1:D2}");
            _output.WriteLine($"  Query: \"{query.Query}\"");
            _output.WriteLine($"  Expected: [{string.Join(", ", query.ExpectedDocumentIds.Select(id => id.ToString("D")))}]");
            _output.WriteLine($"  Retrieved [{retrievedDocIds.Count} docs]:");

            for (var r = 0; r < Math.Min(results.Count, k); r++)
            {
                var isRelevant = query.ExpectedDocumentIds.Contains(results[r].DocumentId);
                var marker = isRelevant ? " <-- RELEVANT" : "";
                _output.WriteLine($"    {r + 1}. {results[r].DocumentId:D} (score: {results[r].Score:F4}){marker}");
            }

            _output.WriteLine($"  First relevant rank: {firstRelevantRank}");
            _output.WriteLine("");
        }
    }

    private IReadOnlyList<SeedDocument> CreateSeedDocuments()
    {
        // Create documents with content matching the golden query terminology
        // Each document uses a specific DocumentId so golden dataset can reference them
        return new List<SeedDocument>
        {
            // Document 1: Sensor documentation (for queries about "Кана�� 2", "датчик")
            CreateSeedDocument(
                Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567891"),
                "Техническая документация датчиков",
                new[]
                {
                    "Датчик представляет собой устройство для измерения физических величин." +
                    " Измеренное значение Канал 2 отображается в реальном времени на панели мониторинга." +
                    " Каждый канал датчика имеет свой диапазон измерения и точность.",
                    "Техническая документация датчика включает описание подключений, калибровку и" +
                    " инструкции по обслуживанию. Канал 1, Канал 2, Канал 3 — это стандартные" +
                    " интерфейсы для передачи данных с датчика на систему мониторинга.",
                    "Штатный режим работы датчика предполагает непрерывный сбор данных с частотой" +
                    " 1 Гц. При отклонении параметров от нормы система генерирует предупреждение."
                }),

            // Document 2: System pressure and monitoring (for queries about "давление", "температура", "аварийный сигнал")
            CreateSeedDocument(
                Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567892"),
                "Мониторинг давления и температуры",
                new[]
                {
                    "Давление в системе МПа измеряется с помощью тензометрических датчиков." +
                    " Нормальное рабочее давление составляет 0,5-1,2 МПа. Превышение порога" +
                    " давления приводит к активации аварийного сигнала.",
                    "Температура технологического процесса контролируется в диапазоне от -40 до" +
                    " +150 градусов Цельсия. Температура влияет на вязкость рабочей жидкости" +
                    " и давление в замкнутой системе.",
                    "Аварийный сигнал превышение порога генерируется при достижении критических" +
                    " значений давления или температуры. Канал связи штатный режим означает" +
                    " нормальную работу системы оповещения.",
                    "Данные мониторинга сохраняются в базе данных для последующего анализа." +
                    " Система мониторинга обеспечивает непрерывный контроль параметров."
                }),

            // Document 3: Voltage and electrical monitoring (for queries about "мониторинг", "напряжение фаза")
            CreateSeedDocument(
                Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567893"),
                "Электрический мониторинг напряжения",
                new[]
                {
                    "Напряжение фаза A B C измеряется трансформаторами напряжения." +
                    " Номинальное напряжение составляет 220/380 В. Мониторинг напряжения" +
                    " выполняется непрерывно для всех трёх фаз.",
                    "Данные мониторинга электрических параметров используются для расчёта" +
                    " активной и реактивной мощности. Отклонение напряжения от нормы более" +
                    " 10% считается аварийной ситуацией.",
                    "Система мониторинга фиксирует перекос фаз, короткое замыкание и другие" +
                    " аномалии. Канал 1, Канал 2, Канал 3 используются для передачи данных" +
                    " о напряжении на диспетчерский пункт."
                }),

            // Document 4: Power metrics (for queries about "частота сети", "активная мощность", "реактивная мощность")
            CreateSeedDocument(
                Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567894"),
                "Расчёт мощности и частоты",
                new[]
                {
                    "Частота сети герц в российской энергосистеме составляет 50 Гц." +
                    " Отклонение частоты от номинала более 0,2 Гц считается отклонением." +
                    " Частота измеряется с помощью частотомеров класса точности 0,5.",
                    "Активная мощность кВт коэффициент — это полезная мощность, потребляемая" +
                    " нагрузкой. Коэффициент мощности (cos фи) показывает соотношение активной" +
                    " и полной мощности в цепи переменного тока.",
                    "Реактивная мощность варcos фи необходима для создания магнитного поля" +
                    " в электродвигателях и трансформаторах. Реактивная мощность не совершает" +
                    " полезной работы, но увеличивает нагрузку на сеть."
                }),

            // Document 5: Protection systems (for queries about "ток короткого замыкания", "дифференциальная защита")
            CreateSeedDocument(
                Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567895"),
                "Защита и токи короткого замыкания",
                new[]
                {
                    "Ток короткого замыкания номинальный рассчитывается для выбора аппаратов" +
                    " защиты. Номинальный ток — это максимальный ток, который оборудование" +
                    " может длительно выдерживать без перегрева.",
                    "Дифференциальная защита ток утечки применяется для защиты людей от" +
                    " поражения электрическим током. Утечка тока через изоляцию может" +
                    " привести к пожару или травме.",
                    "Принцип работы дифференциальной защиты основан на сравнении тока в" +
                    " фазном и нулевом проводнике. При обнаружении разницы (утечки тока)" +
                    " защита мгновенно отключает цепь."
                })
        };
    }

    private static SeedDocument CreateSeedDocument(Guid documentId, string title, string[] chunkTexts)
    {
        var chunks = new List<DocumentChunk>();
        for (var i = 0; i < chunkTexts.Length; i++)
        {
            chunks.Add(new DocumentChunk(documentId, chunkTexts[i], i, ChunkMetadata.Empty));
        }

        var document = new Document(
            documentId,
            title,
            string.Join(" ", chunkTexts),
            "seed",
            new Dictionary<string, string>());
        return new SeedDocument(document, chunks);
    }

    public void Dispose()
    {
        // Clean up temp database
        try
        {
            (_serviceProvider as IDisposable)?.Dispose();
            if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private sealed record SeedDocument(
        Core.Documents.Document Document,
        IReadOnlyList<DocumentChunk> Chunks);

    private sealed class SetupStatistics
    {
        public int DocumentCount { get; set; }
        public int ChunkCount { get; set; }
        public int EmbeddingCount { get; set; }
    }
}
