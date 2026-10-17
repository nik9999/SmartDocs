using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Embeddings;
using SmartDocs.Core.Numerics;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Multimodal;
using SmartDocs.Ingestion.Preprocessing;
using SmartDocs.GigaChat;
using SmartDocs.Samples.ChunkingStrategy;
using SmartDocs.Samples.ChunkingStrategy.Parsers;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("================================================================================");
Console.WriteLine("   SmartDocs: Multimodal & Contextual Chunking Strategy for Complex Content     ");
Console.WriteLine("   (PDF + Tables + Images + XLSX/DOCX + Vision LLM + Local BGE-M3 Embeddings)   ");
Console.WriteLine("================================================================================");
Console.WriteLine();

// 1. Resolve Directories & Ensure Sample Data Exists
var baseDir = AppContext.BaseDirectory;
var projectRoot = ResolveProjectRoot(baseDir);
var dataDir = Path.Combine(projectRoot, "data");
var extractedImagesDir = Path.Combine(projectRoot, "data", ".extracted_images");
Directory.CreateDirectory(extractedImagesDir);

Console.WriteLine($"[1/6] Ensuring corporate sample documents in: {dataDir}");
SampleDataGenerator.EnsureSampleDataExists(dataDir);

// 2. Initialize Local BGE-M3 ONNX Embeddings & TikToken Counter
var bgeModelPath = ResolveModelPath("models/bge-m3/model.onnx");
var bgeSpmPath = ResolveModelPath("models/bge-m3/sentencepiece.bpe.model");

if (!File.Exists(bgeModelPath) || !File.Exists(bgeSpmPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] BGE-M3 model files not found at: {bgeModelPath}");
    Console.ResetColor();
    return;
}

Console.WriteLine($"[2/6] Loading local multilingual BGE-M3 ONNX model (1024-dim)...");
using var embeddingGenerator = new EmbeddingGenerator(bgeModelPath, bgeSpmPath);
Console.WriteLine("      Model initialized successfully with CPU execution provider.");

var tokenCounter = new TokenCounter();
Console.WriteLine($"      Tokenizer initialized with TikToken '{tokenCounter.EncodingName}'.");

// 3. Setup Vision LLM & Contextual Chat Client (User Secrets / GigaChat / Ollama)
var config = new ConfigurationBuilder()
    .SetBasePath(projectRoot)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true)
    .AddEnvironmentVariables()
    .Build();

var gigaAuthKey = config["SmartDocs:Llm:AuthorizationKey"]
    ?? config["GigaChat:AuthorizationKey"]
    ?? config["GIGACHAT_AUTH_KEY"]
    ?? config["GIGACHAT_AUTHORIZATION_KEY"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_AUTH_KEY")
    ?? Environment.GetEnvironmentVariable("GIGACHAT_AUTHORIZATION_KEY");

var gigaClientId = config["SmartDocs:Llm:ClientId"]
    ?? config["GigaChat:ClientId"]
    ?? config["GIGACHAT_CLIENT_ID"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_CLIENT_ID");

var gigaClientSecret = config["SmartDocs:Llm:ClientSecret"]
    ?? config["GigaChat:ClientSecret"]
    ?? config["GIGACHAT_CLIENT_SECRET"]
    ?? Environment.GetEnvironmentVariable("GIGACHAT_CLIENT_SECRET");

var gigaModel = config["SmartDocs:Llm:ChatModel"] ?? GigaChatModels.GigaChat2;
IChatClient? liveChat = null;

if (!string.IsNullOrWhiteSpace(gigaAuthKey) || (!string.IsNullOrWhiteSpace(gigaClientId) && !string.IsNullOrWhiteSpace(gigaClientSecret)))
{
    try
    {
        var gigaClient = !string.IsNullOrWhiteSpace(gigaAuthKey)
            ? new GigaChatClient(gigaAuthKey)
            : new GigaChatClient(gigaClientId!, gigaClientSecret!);
        liveChat = gigaClient.AsChatClient(gigaModel);
        Console.WriteLine($"[3/6] Connected to live Sber GigaChat API (model: {gigaModel}).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[3/6] Failed to initialize GigaChat client: {ex.Message}");
    }
}

if (liveChat == null)
{
    var ollamaEndpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
    bool isOllamaRunning = await CheckOllamaAsync(ollamaEndpoint).ConfigureAwait(false);
    if (isOllamaRunning)
    {
        liveChat = new OllamaApiClient(ollamaEndpoint, "llama3.2");
        Console.WriteLine($"[3/6] Connected to live Ollama at {ollamaEndpoint} (model: llama3.2).");
    }
    else
    {
        Console.WriteLine($"[3/6] LLM endpoints (GigaChat / Ollama) not configured or offline. Using built-in Vision & Situational Context enricher.");
    }
}

using var chatClient = new StrategyEnrichmentChatClient(liveChat);
var imageCaptioner = new ImageCaptioner(chatClient);

// 4. Discover and Parse Heterogeneous Documents
Console.WriteLine();
Console.WriteLine("[4/6] Parsing heterogeneous corporate documents (PDF, DOCX, XLSX, Markdown)...");

var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".docx", ".xlsx", ".pdf", ".md"
};

var docFiles = Directory.GetFiles(dataDir, "*.*", SearchOption.AllDirectories)
    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)) && !f.Contains(".extracted_images", StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f)
    .ToList();

var parsedDocuments = new List<ParsedDocument>();
foreach (var file in docFiles)
{
    var ext = Path.GetExtension(file);
    ParsedDocument parsed = ext.ToLowerInvariant() switch
    {
        ".docx" => WordDocumentParser.Parse(file, extractedImagesDir),
        ".xlsx" => ExcelDocumentParser.Parse(file),
        ".pdf" => await PdfDocumentParser.ParseAsync(file, extractedImagesDir).ConfigureAwait(false),
        ".md" => MarkdownDocumentParser.Parse(file),
        _ => throw new NotSupportedException($"Unsupported extension: {ext}")
    };

    parsedDocuments.Add(parsed);
    var relPath = Path.GetRelativePath(projectRoot, file);
    Console.WriteLine($"  * [{parsed.Document.Metadata.DocumentType,-4}] {relPath,-55} | Tables: {parsed.Tables.Count} | Images: {parsed.Images.Count}");
}

// 5. Multimodal Vision Captioning & Contextual Chunking
Console.WriteLine();
Console.WriteLine("[5/6] Executing Multimodal Inlining & Anthropic Contextual Retrieval...");

var allEnrichedChunks = new List<DocumentChunk>();

foreach (var parsed in parsedDocuments)
{
    var doc = parsed.Document;
    Console.WriteLine();
    Console.WriteLine($"--- Document: {doc.Metadata.Title} ({doc.Metadata.DocumentType}) ---");

    // A. Vision LLM enrichment for extracted figures
    IReadOnlyList<ExtractedImage> captionedImages = Array.Empty<ExtractedImage>();
    if (parsed.Images.Count > 0)
    {
        Console.WriteLine($"  [Vision LLM] Generating descriptions for {parsed.Images.Count} figure(s)...");
        captionedImages = await imageCaptioner.CaptionAllAsync(parsed.Images).ConfigureAwait(false);
        foreach (var img in captionedImages)
        {
            Console.WriteLine($"    [Figure {img.ImageId}]: {img.Caption}");
        }
    }

    // B. Preprocessing + Contextual Chunker + TokenAware Chunker + Recursive Character Chunker
    var baseChunker = new RecursiveCharacterChunker(maxChunkSize: 1200, separators: ["\n## ", "\n# ", "\n\n", ". "]);
    var tokenChunker = new TokenAwareChunker(baseChunker, tokenCounter, maxTokens: 450);
    var contextualChunker = new ContextualChunker(tokenChunker, chatClient);
    var preprocessedChunker = new PreprocessingChunker(contextualChunker);

    // C. Multimodal Chunker (inlines figure captions into text chunks)
    var mmChunker = new MultimodalChunker(preprocessedChunker, captionedImages, Array.Empty<ExtractedTable>());

    int docChunkCount = 0;
    await foreach (var mmChunk in mmChunker.ChunkAsync(doc).ConfigureAwait(false))
    {
        allEnrichedChunks.Add(mmChunk.Chunk);
        docChunkCount++;

        Console.WriteLine($"    === Chunk #{mmChunk.Chunk.ChunkIndex} ({mmChunk.Chunk.Text.Length} chars) ===\n{mmChunk.Chunk.Text}\n    ================================================");
    }

    Console.WriteLine($"  Produced {docChunkCount} enriched chunk(s) (with Markdown tables and situational context).");
}

// 6. Generate 1024-dim Dense Embeddings for all Chunks
Console.WriteLine();
Console.WriteLine($"[6/6] Generating BGE-M3 embeddings for {allEnrichedChunks.Count} chunks...");

var embeddedChunks = new List<EmbeddedChunk>(allEnrichedChunks.Count);
foreach (var chunk in allEnrichedChunks)
{
    var embeddingResult = await embeddingGenerator.GenerateAsync([chunk.Text]).ConfigureAwait(false);
    var vector = embeddingResult[0].Vector;
    embeddedChunks.Add(new EmbeddedChunk(chunk, vector, "bge-m3-onnx"));
}

Console.WriteLine($"      Generated {embeddedChunks.Count} normalized 1024-dimensional vectors.");
Console.WriteLine();

// 7. Cross-Lingual & Multi-Format Semantic Retrieval Demonstration
Console.WriteLine("================================================================================");
Console.WriteLine("              Semantic Search Across Heterogeneous Corporate Data               ");
Console.WriteLine("================================================================================");

var docTypeMap = parsedDocuments.ToDictionary(
    p => p.Document.Metadata.Id,
    p => p.Document.Metadata.DocumentType,
    StringComparer.OrdinalIgnoreCase);

var testQueries = new[]
{
    new { Query = "Каковы условия отпуска и больничного для инженеров?", Lang = "RU", Target = "PDF/MD Vacation Policies (Инженеры: 28 дней, 100% оклада)" },
    new { Query = "Каковы технические характеристики и напряжение питания модуля МВА8?", Lang = "RU", Target = "re_mva8.pdf (Модуль ввода аналоговый: 90...245 В, 8 каналов, RS-485)" },
    new { Query = "Схема подключения датчиков к клеммной колодке прибора МВА8", Lang = "RU", Target = "re_mva8.pdf (Приложение Б: клеммная колодка, термопреобразователи)" },
    new { Query = "Условия эксплуатации и правила монтажа прибора ПИМФ", Lang = "RU", Target = "ПИМФ.421729.006 РЭ (Руководство по эксплуатации: монтаж, безопасность)" },
    new { Query = "What was the enterprise cloud revenue and net profit in Q3 2026?", Lang = "EN", Target = "Excel Financial Spreadsheet ($45.2M / $14.8M)" },
    new { Query = "Microservices architecture with Redis cache and API Gateway", Lang = "EN", Target = "Word Document + Architecture Diagram" },
    new { Query = "Server configurations with 8x NVIDIA GPUs and high memory for AI", Lang = "EN", Target = "Markdown Product Catalog (DGX-H100 / HGX-L40S)" },
    new { Query = "Guaranteed SLA uptime tiers and monthly penalty credits for service breach", Lang = "EN", Target = "Markdown Legal MSA Agreement (99.99% / 20% credit)" }
};

foreach (var test in testQueries)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Query [{test.Lang}]: \"{test.Query}\"");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"Expected target: {test.Target}");
    Console.ResetColor();

    var qEmbedding = await embeddingGenerator.GenerateAsync([test.Query]).ConfigureAwait(false);
    var qVector = qEmbedding[0].Vector;

    var ranked = new List<(EmbeddedChunk Item, double Similarity)>(embeddedChunks.Count);
    foreach (var ec in embeddedChunks)
    {
        var sim = CosineKernel.Cosine(qVector.Span, ec.Vector.Span);
        ranked.Add((ec, sim));
    }

    var topRanked = ranked.OrderByDescending(r => r.Similarity).Take(3).ToList();

    int rank = 1;
    foreach (var match in topRanked)
    {
        var docId = match.Item.Chunk.DocumentId;
        docTypeMap.TryGetValue(docId, out var docType);
        var badge = docType?.ToUpperInvariant() switch
        {
            "WORD" or "DOCX" => "[DOCX]",
            "EXCEL" or "XLSX" => "[XLSX]",
            "PDF" => "[PDF ]",
            _ => "[MD  ]"
        };

        var scorePct = match.Similarity * 100.0;
        var scoreColor = rank == 1 ? ConsoleColor.Green : ConsoleColor.Gray;

        Console.ForegroundColor = scoreColor;
        Console.Write($"  #{rank} ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"{badge} ");
        Console.ForegroundColor = scoreColor;
        Console.Write($"Score: {match.Similarity:F4} ({scorePct:F1}%) ");
        Console.ResetColor();
        Console.WriteLine($"| Doc: {docId} (Chunk #{match.Item.Chunk.ChunkIndex})");

        var preview = match.Item.Chunk.Text.Replace('\r', ' ');
        var lines = preview.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var previewSnippet = string.Join(" \u25aa ", lines.Take(2));
        if (previewSnippet.Length > 130)
        {
            previewSnippet = previewSnippet[..130] + "...";
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"     Preview: {previewSnippet}");
        Console.ResetColor();

        rank++;
    }
}

Console.WriteLine();
Console.WriteLine("================================================================================");
Console.WriteLine(" Chunking Strategy Verification Complete: All heterogeneous formats processed! ");
Console.WriteLine("================================================================================");

// --- Helpers ---

static string ResolveProjectRoot(string baseDir)
{
    var dir = baseDir;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "SmartDocs.slnx")) || File.Exists(Path.Combine(dir, "Directory.Build.props")))
        {
            return dir;
        }
        var parent = Directory.GetParent(dir);
        if (parent == null)
        {
            break;
        }
        dir = parent.FullName;
    }
    return Directory.GetCurrentDirectory();
}

static string ResolveModelPath(string relativePath)
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        var candidate = Path.Combine(dir, relativePath);
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }
        var parent = Directory.GetParent(dir);
        if (parent == null)
        {
            break;
        }
        dir = parent.FullName;
    }
    return Path.GetFullPath(relativePath);
}

static async Task<bool> CheckOllamaAsync(Uri endpoint)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        using var response = await client.GetAsync(endpoint).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}
