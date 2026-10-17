// Chunking Playground Sample for SmartDocs.
//
// Based on Ch04_ChunkingPlayground, adapted to use a self-hosted local ONNX
// BGE-M3 model for embeddings (no Ollama required for semantic chunking).
//
// Runs every chunking strategy side by side:
//   - FixedSize, Sentence, Recursive on English and Russian prose documents,
//   - CodeFile on the .cs document (Roslyn AST-based),
//   - Semantic chunking powered by local ONNX BGE-M3 (multilingual embeddings),
//   - Contextual(Recursive) when an LLM chat endpoint is reachable.
//
// Run:
//   dotnet run --project samples/ChunkingPlayground

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OllamaSharp;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Core.Embeddings;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const int PreviewCount = 8;

Console.WriteLine("==================================================================");
Console.WriteLine("             SmartDocs Chunking Playground (BGE-M3 ONNX)          ");
Console.WriteLine("==================================================================");

// --- 1. Reference documents (English, Russian, and C# code) -------------

var hrText =
    "## Annual Leave Policy\n\n" +
    "Employees at the Montreal office receive 20 paid vacation days per fiscal year, accrued monthly. " +
    "Unused days roll over up to a maximum of 10 into the next fiscal year. " +
    "Long-term leave beyond 30 days requires VP approval.\n\n" +
    "## Sick Leave\n\n" +
    "Sick leave is unlimited for employees in good standing; please notify your manager within 24 hours. " +
    "Long-term sick leave beyond 10 days requires a medical certificate.\n\n" +
    "## Remote Work\n\n" +
    "Remote work is allowed up to 3 days per week with prior manager approval.";

var hrMeta = new DocumentMetadata("hr-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
    2026, "Author", new DateOnly(2026, 1, 1), "Annual Leave Policy");
var hrDoc = new Document(hrMeta, hrText, "data/hr-policies/hr-001.md");

var contractText =
    "## 1. Scope of Services\n\n" +
    "The Vendor shall provide cloud hosting and support services to Contoso Ltd. for the term of " +
    "this agreement. Services include 99.9% uptime, 24/7 monitoring, and quarterly disaster-recovery " +
    "drills. Any change to the scope must be agreed in writing by both parties.\n\n" +
    "## 2. Payment Terms\n\n" +
    "Contoso shall pay the Vendor within 30 days of each invoice date. Late payments accrue interest at " +
    "1.5% per month. Fees are reviewed annually and may increase by no more than the prior year's CPI.\n\n" +
    "## 3. Termination\n\n" +
    "Either party may terminate this agreement with 60 days' written notice. Upon termination the Vendor " +
    "shall return or destroy all Contoso data within 15 days and certify destruction in writing.";

var contractMeta = new DocumentMetadata("contract-001", "legal-contracts", "Legal", "Paris", "Confidential",
    "Contract", 2026, "Author", new DateOnly(2026, 1, 1), "Master Services Agreement");
var contractDoc = new Document(contractMeta, contractText, "data/legal-contracts/contract-001.md");

var hrRuText =
    "## Регламент ежегодного отпуска\n\n" +
    "Сотрудникам офиса предоставляется 28 календарных дней оплачиваемого отпуска в год с ежемесячным начислением. " +
    "Неиспользованные дни переносятся на следующий финансовый год в объеме не более 14 дней. " +
    "Длительный отпуск свыше 30 дней требует письменного согласования с вице-президентом компании.\n\n" +
    "## Больничные и нетрудоспособность\n\n" +
    "При наступлении временной нетрудоспособности сотрудник обязан уведомить руководителя в течение 24 часов. " +
    "Больничный свыше 3 рабочих дней оформляется официальным листком нетрудоспособности.\n\n" +
    "## Дистанционный режим\n\n" +
    "Удаленная работа допускается до 3 рабочих дней в неделю по согласованию с непосредственным руководителем.";

var hrRuMeta = new DocumentMetadata("hr-ru-001", "hr-policies", "HR", "Moscow", "Internal", "Policy",
    2026, "Автор", new DateOnly(2026, 1, 1), "Регламент отпусков и больничных");
var hrRuDoc = new Document(hrRuMeta, hrRuText, "data/hr-policies/hr-ru-001.md");

var csText =
    "namespace SampleNs;\n\n" +
    "public class Calculator\n{\n" +
    "    public int Add(int a, int b) => a + b;\n" +
    "    public int Sub(int a, int b) => a - b;\n" +
    "    public int Mul(int a, int b) => a * b;\n" +
    "}\n";
var csMeta = new DocumentMetadata("code-001", "technical-docs", "Engineering", "Paris", "Internal", "Reference",
    2026, "Author", new DateOnly(2026, 1, 1), "Calculator.cs");
var csDoc = new Document(csMeta, csText, "src/Calculator.cs");

var proseDocs = new[] { hrDoc, contractDoc, hrRuDoc };

// --- 2. Initializing Local BGE-M3 ONNX Embeddings ------------------------

var bgeModelPath = ResolveModelPath("models/bge-m3/model.onnx");
var bgeSpmPath = ResolveModelPath("models/bge-m3/sentencepiece.bpe.model");

EmbeddingGenerator? bgeGenerator = null;
if (File.Exists(bgeModelPath) && File.Exists(bgeSpmPath))
{
    Console.WriteLine($"[OK] Found local BGE-M3 model at {bgeModelPath}");
    bgeGenerator = new EmbeddingGenerator(bgeModelPath, bgeSpmPath);
    Console.WriteLine("     Loaded BGE-M3 ONNX runtime session (1024-dim, multilingual).");
}
else
{
    Console.WriteLine($"[WARN] BGE-M3 model files not found at {bgeModelPath}.");
    Console.WriteLine("       Run `python models/download_models.py` to download them.");
}

// --- 3. Optional Chat Client for Contextual Chunker ----------------------

var ollamaEndpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
const string ChatModel = "llama3.2";
bool ollamaOnline = await IsOllamaReachableAsync(ollamaEndpoint).ConfigureAwait(false);
IChatClient? chatClient = null;

if (ollamaOnline)
{
    chatClient = new OllamaApiClient(ollamaEndpoint, ChatModel);
    Console.WriteLine($"[OK] Ollama reachable at {ollamaEndpoint} (model: {ChatModel}) — Contextual chunking enabled.");
}
else
{
    Console.WriteLine($"[INFO] Ollama is not active at {ollamaEndpoint} (Contextual chunking will be skipped).");
}

Console.WriteLine();

// --- 4. Strategy runs ----------------------------------------------------

// Classic char-based strategies (FixedSize, Sentence, Recursive)
foreach (var doc in proseDocs)
{
    await RunAsync("FixedSize(200, 50)", new FixedSizeChunker(200, 50), doc).ConfigureAwait(false);
    await RunAsync("Sentence(2)", new SentenceChunker(2), doc).ConfigureAwait(false);
    await RunAsync("Recursive(200)", new RecursiveCharacterChunker(200), doc).ConfigureAwait(false);
}

// Code-aware strategy on C# document (Roslyn)
await RunAsync("CodeFile (C# Roslyn)", new CodeFileChunker(), csDoc).ConfigureAwait(false);

// Semantic Chunker powered by BGE-M3 ONNX
if (bgeGenerator != null)
{
    Console.WriteLine();
    Console.WriteLine("=== Running SemanticChunker powered by Local BGE-M3 ONNX ===");
    foreach (var doc in proseDocs)
    {
        await RunAsync("Semantic(0.75) [BGE-M3]", new SemanticChunker(bgeGenerator, 0.75), doc).ConfigureAwait(false);
    }
}

// Contextual Chunker (requires IChatClient)
if (chatClient != null)
{
    Console.WriteLine();
    Console.WriteLine("=== Running ContextualChunker (Anthropic Contextual Retrieval) ===");
    foreach (var doc in proseDocs)
    {
        await RunAsync(
            "Contextual(Recursive(200))",
            new ContextualChunker(new RecursiveCharacterChunker(200), chatClient),
            doc).ConfigureAwait(false);
    }
}
else
{
    Console.WriteLine();
    Console.WriteLine("=== Contextual(Recursive) — SKIPPED ===");
    Console.WriteLine("  Contextual chunking requires an LLM chat client (e.g. Ollama or GigaChat).");
}

Console.WriteLine();
Console.WriteLine("All chunking playground runs completed successfully.");

// --- Helper methods ------------------------------------------------------

static async Task RunAsync(string name, IChunker chunker, Document doc)
{
    Console.WriteLine();
    Console.WriteLine($"--- {name} on {doc.SourcePath} ---");
    var chunks = new List<DocumentChunk>();
    await foreach (var c in chunker.ChunkAsync(doc).ConfigureAwait(false))
    {
        chunks.Add(c);
    }

    var avgLen = chunks.Count == 0 ? 0 : chunks.Average(c => c.Text.Length);
    Console.WriteLine($"  strategy={chunker.Strategy,-25}  count={chunks.Count,-3}  avgLen={avgLen:F0} chars");
    foreach (var c in chunks.Take(PreviewCount))
    {
        var preview = c.Text.Replace('\n', ' ');
        if (preview.Length > 65)
        {
            preview = preview[..65] + "...";
        }

        Console.WriteLine($"  [{c.ChunkIndex}] @{c.StartCharOffset,3}-{c.EndCharOffset,3}: {preview}");
    }
}

static async Task<bool> IsOllamaReachableAsync(Uri endpoint)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        using var response = await http.GetAsync(endpoint).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
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
