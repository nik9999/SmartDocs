using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Demo.Cli;
using SmartDocs.GigaChat;
using SmartDocs.GigaChat.Extensions;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.DependencyInjection;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Ingestion.Multimodal;
using SmartDocs.Ingestion.Preprocessing;
using SmartDocs.Reranking;
using SmartDocs.Reranking.Onnx.DependencyInjection;
using SmartDocs.Retrieval;
using Microsoft.Data.Sqlite;
using SmartDocs.Retrieval.Hybrid;
using SmartDocs.Retrieval.VectorStores;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

const string SqliteDbFile = "smartdocs_contravt.db";
const string SqliteConn = $"Data Source={SqliteDbFile};Cache=Shared";

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddUserSecrets<Program>(optional: true)
    .AddUserSecrets("smartdocs-chunking-strategy")
    .AddEnvironmentVariables();

var gigaAuthKey = builder.Configuration["SmartDocs:Llm:AuthorizationKey"]
    ?? builder.Configuration["GigaChat:AuthorizationKey"]
    ?? builder.Configuration["GIGACHAT_AUTH_KEY"]
    ?? builder.Configuration["GIGACHAT_AUTHORIZATION_KEY"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_AUTH_KEY")
    ?? Environment.GetEnvironmentVariable("GIGACHAT_AUTHORIZATION_KEY");

var gigaClientId = builder.Configuration["SmartDocs:Llm:ClientId"]
    ?? builder.Configuration["GigaChat:ClientId"]
    ?? builder.Configuration["GIGACHAT_CLIENT_ID"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_CLIENT_ID");

var gigaClientSecret = builder.Configuration["SmartDocs:Llm:ClientSecret"]
    ?? builder.Configuration["GigaChat:ClientSecret"]
    ?? builder.Configuration["GIGACHAT_CLIENT_SECRET"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_CLIENT_SECRET");

var gigaScope = builder.Configuration["SmartDocs:Llm:Scope"]
    ?? builder.Configuration["GigaChat:Scope"]
    ?? builder.Configuration["GIGACHAT_SCOPE"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_SCOPE")
    ?? GigaChatScope.Personal;

// Конфигурация для SmartDocs.Core и SmartDocs.Ingestion
builder.Configuration["SmartDocs:Llm:Provider"] = "GigaChat";
builder.Configuration["SmartDocs:Llm:ChatModel"] = GigaChatModels.GigaChat2Pro;
builder.Configuration["SmartDocs:Llm:Scope"] = gigaScope;
builder.Configuration["SmartDocs:Llm:Endpoint"] = "https://gigachat.devices.sberbank.ru/api/v1";
builder.Configuration["SmartDocs:Embedding:ModelPath"] = "models/bge-m3/model.onnx";
builder.Configuration["SmartDocs:Embedding:Dimensions"] = "1024";

if (!string.IsNullOrWhiteSpace(gigaAuthKey))
{
    builder.Configuration["SmartDocs:Llm:AuthorizationKey"] = gigaAuthKey;
}
if (!string.IsNullOrWhiteSpace(gigaClientId))
{
    builder.Configuration["SmartDocs:Llm:ClientId"] = gigaClientId;
}
if (!string.IsNullOrWhiteSpace(gigaClientSecret))
{
    builder.Configuration["SmartDocs:Llm:ClientSecret"] = gigaClientSecret;
}

// 1. Штатная регистрация ядра SmartDocs.Core (TokenCounter, EmbeddingGenerator, EmbeddingOptions и т.д.)
builder.Services.AddSmartDocsCore(builder.Configuration);

// 2. Штатная регистрация GigaChat (для Vision ImageCaptioner и ContextualChunker)
builder.Services.AddGigaChatClient(options =>
{
    options.AuthorizationKey = gigaAuthKey;
    options.ClientId = gigaClientId;
    options.ClientSecret = gigaClientSecret;
    options.Scope = gigaScope;
    options.DefaultModel = GigaChatModels.GigaChat2Pro;
});
builder.Services.AddSingleton<IChatClient>(sp =>
    sp.GetRequiredService<GigaChatClient>().AsChatClient(GigaChatModels.GigaChat2Pro));

// 3. Штатные сервисы Ingestion (BatchEmbeddingPipeline, DocumentPreprocessor, EmbeddingService)
builder.Services.AddSmartDocsIngestion(builder.Configuration);

builder.Services.AddSingleton<IEmbeddingService>(sp =>
    new EmbeddingService(
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        embeddingModel: "bge-m3",
        dimensions: 1024,
        logger: sp.GetRequiredService<ILogger<EmbeddingService>>()));

builder.Services.AddSingleton<BatchEmbeddingPipeline>(sp =>
    new BatchEmbeddingPipeline(
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        embeddingModel: "bge-m3",
        options: new BatchEmbeddingOptions { BatchSize = 16, MaxConcurrency = 4 },
        logger: sp.GetRequiredService<ILogger<BatchEmbeddingPipeline>>()));

// Переопределяем загрузчик PDF и оборачиваем PdfPigImageExtractor фильтром дубликатов
builder.Services.AddHttpClient<IDocumentLoader, ContravtPdfDocumentLoader>();
builder.Services.AddSingleton<PdfPigImageExtractor>();
builder.Services.AddSingleton<IPdfImageExtractor>(sp =>
    new FilteredPdfImageExtractor(sp.GetRequiredService<PdfPigImageExtractor>(), minWidth: 120, minHeight: 120));

builder.Services.AddSingleton<ImageCaptioner>();

// Собираем цепочку IChunker строго по схеме:
// RecursiveCharacter -> TokenAware -> AtomicMarkdownTable -> MultimodalChunker -> ContextualChunker
builder.Services.AddSingleton<IChunker>(sp =>
{
    var tokenCounter = sp.GetRequiredService<ITokenCounter>();
    var extractor = sp.GetRequiredService<IPdfImageExtractor>();
    var captioner = sp.GetRequiredService<ImageCaptioner>();

    IChunker baseChunker = new TokenAwareChunker(
        new RecursiveCharacterChunker(maxChunkSize: 1800),
        tokenCounter,
        maxTokens: 500);

    IChunker tableAwareChunker = new AtomicMarkdownTableChunker(baseChunker, tokenCounter, maxChunkTokens: 550);
    IChunker multimodalChunker = new DocumentMultimodalChunker(
        tableAwareChunker,
        extractor,
        captioner,
        tokenCounter,
        maxTokens: 450,
        gigaChatClient: sp.GetService<GigaChatClient>());

    return new ContextualChunker(multimodalChunker, sp.GetRequiredService<IChatClient>());
});

// 4. Настройка SQLite из SmartDocs.Retrieval (SqliteVectorStore + SqliteHybridRetriever)
builder.Services.Configure<SqliteVectorStoreOptions>(opt =>
{
    opt.ConnectionString = SqliteConn;
    opt.TableName = "document_chunks";
});

builder.Services.Configure<SqliteHybridOptions>(opt =>
{
    opt.ConnectionString = SqliteConn;
    opt.VectorsTableName = "document_chunks";
    opt.FtsTableName = "document_chunks_fts";
    opt.RrfConstant = 60;
});

builder.Services.AddSingleton<SqliteVectorStore>(sp =>
    new SqliteVectorStore(sp.GetRequiredService<IOptions<SqliteVectorStoreOptions>>().Value));
builder.Services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<SqliteVectorStore>());

// 5. Подключаем штатный ONNX Reranker и SqliteHybridRetriever
builder.Services.AddSmartDocsOnnxCrossEncoder(
    "models/bge-reranker-v2-m3/model.onnx",
    "models/bge-reranker-v2-m3/sentencepiece.bpe.model");

builder.Services.AddSingleton<SqliteHybridRetriever>(sp =>
    new SqliteHybridRetriever(
        sp.GetRequiredService<IEmbeddingService>(),
        SqliteConn,
        options: sp.GetRequiredService<IOptions<SqliteHybridOptions>>().Value));

builder.Services.AddSingleton<IRetriever>(sp =>
{
    var sqliteHybrid = sp.GetRequiredService<SqliteHybridRetriever>();
    var reranker = sp.GetRequiredService<IReranker>();

    return new RerankingMiddleware(
        inner: new MinScoreFilter(sqliteHybrid, minScore: 0.01),
        reranker: reranker,
        candidateCount: 10,
        minScore: 0.01);
});

using IHost host = builder.Build();

// =========================================================================
// ЗАПУСК ИНДЕКСАЦИИ И ПОИСКА НА ДОКУМЕНТЕ ПИМФ.426439.004 РЭ
// =========================================================================
var loader = host.Services.GetRequiredService<IDocumentLoader>();
var preprocessor = host.Services.GetRequiredService<IDocumentPreprocessor>();
var chunker = host.Services.GetRequiredService<IChunker>();
var embeddingPipeline = host.Services.GetRequiredService<BatchEmbeddingPipeline>();
var vectorStore = host.Services.GetRequiredService<IVectorStore>();
var retriever = host.Services.GetRequiredService<IRetriever>();

bool forceReindex = args.Contains("--reindex", StringComparer.OrdinalIgnoreCase) ||
                    args.Contains("--force", StringComparer.OrdinalIgnoreCase);
bool skipIngest = args.Contains("--skip-ingest", StringComparer.OrdinalIgnoreCase);

int existingChunkCount = 0;
if (File.Exists(SqliteDbFile) && !forceReindex)
{
    try
    {
        using var conn = new SqliteConnection(SqliteConn);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='document_chunks';";
        var tableExists = Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
        if (tableExists)
        {
            cmd.CommandText = "SELECT count(*) FROM document_chunks;";
            existingChunkCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
    }
    catch (SqliteException)
    {
        existingChunkCount = 0;
    }
}

if (skipIngest || (existingChunkCount > 0 && !forceReindex))
{
    Console.WriteLine($"[INFO] В локальной базе '{SqliteDbFile}' уже сохранено {existingChunkCount} чанков.");
    Console.WriteLine("       Пропуск шагов [1/4]–[3/4] (повторные сетевые вызовы к Vision LLM и GigaChat не требуются).");
    Console.WriteLine("       Для принудительной переиндексации используйте флаг --reindex\n");
}
else
{
    if (forceReindex && File.Exists(SqliteDbFile))
    {
        Console.WriteLine($"[INFO] Запрошена переиндексация (--reindex). Удаление старой базы '{SqliteDbFile}'...");
        try
        {
            File.Delete(SqliteDbFile);
        }
        catch (IOException)
        {
        }
    }

    string pdfPath = ResolvePdfPath();
    Console.WriteLine($"[1/4] Загрузка и парсинг локального PDF: {pdfPath}");
    Document rawDoc = await loader.LoadAsync(pdfPath);
    Document cleanDoc = preprocessor.Preprocess(rawDoc);

    Console.WriteLine("[2/4] Выполнение мультимодального чанкинга и Contextual Retrieval...");
    Console.WriteLine("      (Для каждого чанка вызывается Contextual Retrieval через LLM)...");
    var chunks = new List<DocumentChunk>();
    var chunkSw = Stopwatch.StartNew();
    int chunkIndex = 0;

    await foreach (var chunk in chunker.ChunkAsync(cleanDoc))
    {
        chunks.Add(chunk);
        chunkIndex++;

        var firstLine = chunk.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        if (firstLine.Length > 80)
        {
            firstLine = string.Concat(firstLine.AsSpan(0, 80), "...");
        }

        Console.WriteLine($"      [{chunkSw.Elapsed:mm\\:ss}] Чанк #{chunkIndex:D2} готов (ID: {chunk.ChunkId}, {chunk.Text.Length} симв.) | {firstLine}");
    }
    Console.WriteLine($"      Всего получено чанков: {chunks.Count} за {chunkSw.Elapsed:mm\\:ss}");

    Console.WriteLine("[3/4] Генерация эмбеддингов через штатный EmbeddingGenerator (SmartDocs.Core) и запись в SQLite...");
    var embeddedChunks = new List<EmbeddedChunk>();
    var embedSw = Stopwatch.StartNew();
    int embeddedCount = 0;

    await foreach (var ec in embeddingPipeline.EmbedAsync(ToAsyncEnumerable(chunks)))
    {
        embeddedChunks.Add(ec);
        embeddedCount++;
        if (embeddedCount % 5 == 0 || embeddedCount == chunks.Count)
        {
            Console.WriteLine($"      [{embedSw.Elapsed:mm\\:ss}] Эмбеддинги: {embeddedCount}/{chunks.Count}...");
        }
    }
    await vectorStore.UpsertAsync(embeddedChunks);
    Console.WriteLine($"      Векторы сохранены в SQLite за {embedSw.Elapsed:mm\\:ss}.\n");
}

Console.WriteLine("\n[4/4] Проверка гибридного поиска (SqliteHybridRetriever + RRF + ONNX Reranker):\n");

string[] queries =
[
    "Какая схема подключения термопар и датчиков тока 4-20 мА к модулю MDS-AI-8UI?",
    "Карта регистров Modbus RTU и настройки скорости интерфейса RS-485",
    "Как сбросить настройки модуля на заводские с помощью перемычки и программы SetMaker?"
];

foreach (var query in queries)
{
    Console.WriteLine($"Запрос: «{query}»");
    var results = await retriever.RetrieveAsync(query, topK: 3);

    foreach (var hit in results)
    {
        Console.WriteLine($"  -> [Score: {hit.Score.ToString("F4", CultureInfo.InvariantCulture)}] {hit.Chunk.ChunkId}:");
        Console.WriteLine($"     {hit.Chunk.Text[..Math.Min(220, hit.Chunk.Text.Length)].Replace("\n", " ", StringComparison.Ordinal)}...\n");
    }
}

static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
{
    foreach (var item in items)
    {
        yield return item;
    }
    await Task.CompletedTask;
}

static string ResolvePdfPath()
{
    string[] candidateDirs =
    [
        Path.Combine(AppContext.BaseDirectory, "Documents"),
        Path.Combine(Directory.GetCurrentDirectory(), "Documents"),
        Path.Combine(Directory.GetCurrentDirectory(), "samples", "SmartDocs.Demo.Cli", "Documents")
    ];

    foreach (var dir in candidateDirs)
    {
        if (Directory.Exists(dir))
        {
            var pdfFile = Directory.GetFiles(dir, "*.pdf").FirstOrDefault();
            if (!string.IsNullOrEmpty(pdfFile))
            {
                return Path.GetFullPath(pdfFile);
            }
        }
    }

    throw new FileNotFoundException(
        $"PDF-файл руководства не найден в каталоге 'Documents' ({Path.Combine(AppContext.BaseDirectory, "Documents")}). " +
        "Поместите PDF-файл документации в эту папку.");
}