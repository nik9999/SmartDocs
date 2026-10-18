using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Embeddings;
using SmartDocs.Core.Tokens;
using SmartDocs.GigaChat;
using SmartDocs.GigaChat.Extensions;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Loaders;
using SmartDocs.Ingestion.Preprocessing;
using SmartDocs.Samples.StrategyTest.Cli.Benchmarking;
using SmartDocs.Samples.StrategyTest.Cli.Cost;
using SmartDocs.Samples.StrategyTest.Cli.Models;
using SmartDocs.Samples.StrategyTest.Cli.Reports;

namespace SmartDocs.Samples.StrategyTest.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        PrintHeader();

        // 1. Конфигурация и секреты (для GigaChat и настроек стоимости)
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets("smartdocs-chunking-strategy")
            .AddEnvironmentVariables()
            .Build();

        var tokenCounter = new TokenCounter();
        var pricingModel = new CostPricingModel();

        // 2. Поиск и выбор PDF документа
        string? pdfPath = ResolvePdfPath(args);
        if (pdfPath == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ОШИБКА] Не найден ни один PDF-файл в папке 'Documents' и не передан путь в аргументах.");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[1/5] Загрузка и предобработка PDF документа: {Path.GetFileName(pdfPath)}");
        Console.ResetColor();

        // 3. Загрузка через PdfDocumentLoader
        var loader = new PdfDocumentLoader(new PdfDocumentLoaderOptions
        {
            ThrowIfNoTextExtracted = true,
            LineClusteringTolerance = 3.0
        });

        Document rawDoc;
        try
        {
            rawDoc = await loader.LoadAsync(pdfPath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ОШИБКА] Сбой при загрузке PDF: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        // 4. Предобработка через DocumentPreprocessor
        var preprocessor = new DocumentPreprocessor(new DocumentPreprocessorOptions
        {
            Dehyphenate = true,
            FilterEmptyPages = true,
            RemoveDotLeaders = true,
            StripRepeatingHeadersAndFooters = false
        });

        var doc = preprocessor.Preprocess(rawDoc);
        int totalTokens = tokenCounter.CountTokens(doc.Content);

        Console.WriteLine($"      [Метаданные] Id: {doc.Metadata.Id} | Заголовок: {doc.Metadata.Title}");
        Console.WriteLine($"      [Метаданные] Автор: {doc.Metadata.Author} | Год: {doc.Metadata.FiscalYear} | Офис: {doc.Metadata.Office} | Тип: {doc.Metadata.DocumentType}");
        Console.WriteLine($"      [Объем] Текст: {rawDoc.Content.Length:N0} -> {doc.Content.Length:N0} символов (~{totalTokens:N0} токенов)");

        // 5. Инициализация моделей (локальный BGE-M3 и GigaChat)
        var bgeGenerator = TryInitBgeM3();
        var chatClient = TryInitChatClient(configuration);

        // 6. Формирование пула стратегий чанкинга
        var strategySuite = BuildStrategySuite(tokenCounter, bgeGenerator);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[2/5] Выполнение чанкинга для {strategySuite.Count} стратегий и формирование Отчёта 1 (Chunking Quality)...");
        Console.ResetColor();

        var chunkMetrics = new List<ChunkingQualityMetric>();

        foreach (var (name, description, chunker) in strategySuite)
        {
            var sw = Stopwatch.StartNew();
            var chunks = new List<DocumentChunk>();

            try
            {
                await foreach (var chunk in chunker.ChunkAsync(doc))
                {
                    chunks.Add(chunk);
                }
                sw.Stop();

                var metric = ChunkingQualityAnalyzer.Analyze(name, description, doc, chunks, tokenCounter, sw.ElapsedMilliseconds);
                chunkMetrics.Add(metric);
                Console.WriteLine($"  * [{name,-32}] получено {chunks.Count,3} чанков за {sw.ElapsedMilliseconds,4} мс (дефектов: {metric.BrokenTablesCount + metric.OrphanedTableRowsCount + metric.OverBudget256Count,2})");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  * [{name,-32}] Сбой выполнения: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Вывод сводной таблицы качества чанкинга в консоль
        PrintChunkingQualityTable(chunkMetrics);

        // 7. Отчёт 2: Retrieval Benchmark (Recall@5, Recall@10, MRR, nDCG@10, Zero-Hit Rate)
        var benchmarkQueries = BenchmarkDataset.GetPassportQueries();
        var retrievalMetrics = new List<RetrievalBenchmarkMetric>();

        if (bgeGenerator != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[3/5] Запуск Retrieval Benchmark (Отчёт 2) на {benchmarkQueries.Count} контрольных вопросах через BGE-M3...");
            Console.ResetColor();

            foreach (var metric in chunkMetrics)
            {
                Console.Write($"  * Оценка поиска для [{metric.StrategyName,-32}]... ");
                var retMetric = await RetrievalEvaluator.EvaluateAsync(
                    metric.StrategyName,
                    metric.Chunks,
                    bgeGenerator,
                    benchmarkQueries);

                retrievalMetrics.Add(retMetric);
                Console.WriteLine($"Recall@5: {retMetric.RecallAt5:P0} | MRR: {retMetric.Mrr:F3} | nDCG@10: {retMetric.NdcgAt10:F3} | Zero-Hit: {retMetric.ZeroHitRatePercent:F0}%");
            }

            PrintRetrievalBenchmarkTable(retrievalMetrics);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[3/5] Пропуск Retrieval Benchmark: модель BGE-M3 недоступна.");
            Console.ResetColor();
        }

        // 8. Отчёт 3: Answer Quality & Cost Report (Правильность, Обоснованность, Задержки, Токены, Рублевая стоимость)
        var answerMetrics = new List<AnswerQualityCostMetric>();

        if (bgeGenerator != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[4/5] Запуск Answer Quality & Cost (Отчёт 3) на репрезентативных стратегиях...");
            Console.ResetColor();

            // Выбираем ключевые стратегии для генерации ответов и подсчета стоимости
            string[] selectedStrategies =
            [
                "Sentence(5) + Token(200)",
                "Recursive(800) + Token(256)",
                "FixedSize (500 / ov 100)",
                "Semantic (thresh 0.70)"
            ];

            var targetChunks = chunkMetrics
                .Where(m => selectedStrategies.Contains(m.StrategyName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (targetChunks.Count == 0)
            {
                targetChunks = chunkMetrics.Take(3).ToList();
            }

            foreach (var cm in targetChunks)
            {
                Console.Write($"  * Генерация RAG-ответов и расчет стоимости для [{cm.StrategyName}]... ");
                var ansMetric = await AnswerEvaluator.EvaluateAsync(
                    cm.StrategyName,
                    cm.Chunks,
                    cm.ElapsedMs,
                    bgeGenerator,
                    chatClient,
                    tokenCounter,
                    pricingModel,
                    benchmarkQueries.Take(4).ToList()); // 4 контрольных вопроса для оценки генерации

                answerMetrics.Add(ansMetric);
                Console.WriteLine($"Правильность: {ansMetric.AvgCorrectnessScore:P0} | Задержка: {ansMetric.AvgTotalLatencyMs:F0} мс | Стоимость/запрос: {ansMetric.AvgCostPerQueryRubles:F4} ₽");
            }

            PrintAnswerQualityCostTable(answerMetrics);
        }

        // 9. Сохранение всех трёх отчётов на диск
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n[5/5] Формирование и сохранение всех отчётов в папку 'reports/'...");
        Console.ResetColor();

        string reportsDir = ResolveReportsDirectory();

        string file1 = ReportWriters.WriteChunkingQualityReport(chunkMetrics, reportsDir);
        string file2 = ReportWriters.WriteRetrievalBenchmarkReport(retrievalMetrics, reportsDir);
        string file3 = ReportWriters.WriteAnswerQualityCostReport(answerMetrics, reportsDir);
        string file4 = ReportWriters.WriteExecutiveSummaryReport(chunkMetrics, retrievalMetrics, answerMetrics, reportsDir);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[УСПЕХ] Все 3 отчёта и сводный Executive Summary успешно сформированы:");
        Console.ResetColor();
        Console.WriteLine($"  1. Chunking Quality Report:       {file1}");
        Console.WriteLine($"  2. Retrieval Benchmark Report:     {file2}");
        Console.WriteLine($"  3. Answer Quality & Cost Report:   {file3}");
        Console.WriteLine($"  4. Executive Summary Report:       {file4}\n");

        return 0;
    }

    private static void PrintHeader()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("       SmartDocs: Лаборатория комплексной оценки стратегий чанкинга             ");
        Console.WriteLine("       1. Chunking Quality | 2. Retrieval Benchmark | 3. Answer Quality & Cost  ");
        Console.WriteLine("================================================================================\n");
    }

    private static void PrintChunkingQualityTable(IReadOnlyList<ChunkingQualityMetric> metrics)
    {
        Console.WriteLine("\n=============================================================================================================");
        Console.WriteLine("                         1. CHUNKING QUALITY REPORT (КАЧЕСТВО ЧАНКИНГА)                              ");
        Console.WriteLine("=============================================================================================================");
        Console.WriteLine(" Стратегия                      │ Чанков │ Символы (min/avg/max) │ Токены (min/avg/max) │ Покрытие │ Таблицы │ >256t │ Мета │ Время ");
        Console.WriteLine("────────────────────────────────┼────────┼───────────────────────┼──────────────────────┼──────────┼─────────┼───────┼──────┼───────");

        foreach (var m in metrics)
        {
            int tableIssues = m.BrokenTablesCount + m.OrphanedTableRowsCount;
            Console.WriteLine(
                $" {m.StrategyName,-30} │ {m.TotalChunks,6} │ " +
                $"{m.CharMin,4}/{m.CharAvg,3:F0}/{m.CharMax,4} │ " +
                $"{m.TokenMin,4}/{m.TokenAvg,3:F0}/{m.TokenMax,4} │ " +
                $"{m.DocumentCoveragePercent,7:F1}% │ {tableIssues,7} │ " +
                $"{m.OverBudget256Count,5} │ {m.MetadataErrorsCount,4} │ {m.ElapsedMs,5} мс");
        }
        Console.WriteLine("=============================================================================================================");
    }

    private static void PrintRetrievalBenchmarkTable(IReadOnlyList<RetrievalBenchmarkMetric> benchmarks)
    {
        Console.WriteLine("\n=============================================================================================================");
        Console.WriteLine("                         2. RETRIEVAL BENCHMARK REPORT (ТОЧНОСТЬ ПОИСКА)                             ");
        Console.WriteLine("=============================================================================================================");
        Console.WriteLine(" Стратегия                      │ Recall@5 │ Recall@10 │   MRR   │ nDCG@10 │ Zero-Hit Rate │ Средн. поиск (мс) ");
        Console.WriteLine("────────────────────────────────┼──────────┼───────────┼─────────┼─────────┼───────────────┼───────────────────");

        foreach (var b in benchmarks.OrderByDescending(b => b.NdcgAt10))
        {
            Console.WriteLine(
                $" {b.StrategyName,-30} │ {b.RecallAt5,7:P0}  │ {b.RecallAt10,8:P0}  │ {b.Mrr,7:F3} │ {b.NdcgAt10,7:F3} │ {b.ZeroHitRatePercent,12:F1}% │ {b.AvgRetrievalMs,14:F1} мс");
        }
        Console.WriteLine("=============================================================================================================");
    }

    private static void PrintAnswerQualityCostTable(IReadOnlyList<AnswerQualityCostMetric> answerMetrics)
    {
        Console.WriteLine("\n=============================================================================================================");
        Console.WriteLine("                     3. ANSWER QUALITY & COST REPORT (ОТВЕТЫ И СТОИМОСТЬ)                                    ");
        Console.WriteLine("=============================================================================================================");
        Console.WriteLine(" Стратегия                      │ Правильн. │ Обоснован. │ Поиск │ LLM ген. │ Всего мс │ Токены  │ Стоимость/запрос  ");
        Console.WriteLine("────────────────────────────────┼───────────┼────────────┼───────┼──────────┼──────────┼─────────┼───────────────────");

        foreach (var a in answerMetrics.OrderByDescending(a => a.AvgCorrectnessScore))
        {
            Console.WriteLine(
                $" {a.StrategyName,-30} │ {a.AvgCorrectnessScore,8:P0}  │ {a.AvgFaithfulnessScore,9:P0}  │ {a.AvgRetrievalMs,4:F0}мс │ {a.AvgGenerationMs,6:F0}мс │ {a.AvgTotalLatencyMs,6:F0}мс │ {a.TotalAllTokens,7:N0} │ {a.AvgCostPerQueryRubles,14:F4} ₽");
        }
        Console.WriteLine("=============================================================================================================");
    }

    private static List<(string Name, string Description, IChunker Chunker)> BuildStrategySuite(
        TokenCounter tokenCounter,
        EmbeddingGenerator? bgeGenerator)
    {
        var suite = new List<(string Name, string Description, IChunker Chunker)>
        {
            (
                "Recursive (800 chars)",
                "Базовый рекурсивный чанкер с приоритетом заголовков и абзацев",
                new RecursiveCharacterChunker(maxChunkSize: 800)
            ),
            (
                "Recursive (400 chars)",
                "Компактный рекурсивный чанкер под небольшие фрагменты",
                new RecursiveCharacterChunker(maxChunkSize: 400)
            ),
            (
                "Sentence (3 sent)",
                "Разбиение по границам предложений (до 3 предложений)",
                new SentenceChunker(maxSentencesPerChunk: 3)
            ),
            (
                "Sentence (5 sent)",
                "Разбиение по границам предложений (до 5 предложений)",
                new SentenceChunker(maxSentencesPerChunk: 5)
            ),
            (
                "FixedSize (500 / ov 100)",
                "Контрольный чанкинг фиксированного размера с перекрытием",
                new FixedSizeChunker(chunkSize: 500, overlap: 100)
            ),
            (
                "FixedSize (250 / ov 50)",
                "Мелкий фиксированный чанкинг с перекрытием",
                new FixedSizeChunker(chunkSize: 250, overlap: 50)
            ),
            (
                "TokenAware (Fixed + 256 tok)",
                "Декоратор: фиксированные блоки (1000) + жесткий потолок 256 токенов",
                new TokenAwareChunker(new FixedSizeChunker(chunkSize: 1000, overlap: 100), tokenCounter, maxTokens: 256)
            ),
            (
                "Recursive(800) + Token(256)",
                "Декоратор: рекурсивные границы + жесткий потолок токенов",
                new TokenAwareChunker(new RecursiveCharacterChunker(maxChunkSize: 800), tokenCounter, maxTokens: 256)
            ),
            (
                "Sentence(5) + Token(200)",
                "Декоратор: предложения + ограничение в 200 токенов",
                new TokenAwareChunker(new SentenceChunker(maxSentencesPerChunk: 5), tokenCounter, maxTokens: 200)
            )
        };

        if (bgeGenerator != null)
        {
            suite.Add((
                "Semantic (thresh 0.70)",
                "Семантический поиск смысловых сдвигов через косинусное сходство BGE-M3",
                new SemanticChunker(bgeGenerator, breakpointThreshold: 0.70)
            ));
        }

        return suite;
    }

    private static string? ResolvePdfPath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) && File.Exists(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        string[] candidateDirs =
        [
            Path.Combine(AppContext.BaseDirectory, "Documents"),
            Path.Combine(Directory.GetCurrentDirectory(), "Documents"),
            Path.Combine(Directory.GetCurrentDirectory(), "samples", "SmartDocs.StrategyTest.Cli", "Documents")
        ];

        foreach (var dir in candidateDirs)
        {
            if (Directory.Exists(dir))
            {
                var pdf = Directory.GetFiles(dir, "*.pdf").FirstOrDefault();
                if (!string.IsNullOrEmpty(pdf))
                {
                    return Path.GetFullPath(pdf);
                }
            }
        }

        return null;
    }

    private static string ResolveReportsDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        string? projectRoot = FindProjectRoot(baseDir);

        if (projectRoot != null)
        {
            var projectReports = Path.Combine(projectRoot, "samples", "SmartDocs.StrategyTest.Cli", "reports");
            Directory.CreateDirectory(projectReports);
            return projectReports;
        }

        var localReports = Path.Combine(baseDir, "reports");
        Directory.CreateDirectory(localReports);
        return localReports;
    }

    private static EmbeddingGenerator? TryInitBgeM3()
    {
        var baseDir = AppContext.BaseDirectory;
        string? projectRoot = FindProjectRoot(baseDir);
        if (projectRoot == null)
        {
            return null;
        }

        var modelPath = Path.Combine(projectRoot, "models", "bge-m3", "model.onnx");
        var spmPath = Path.Combine(projectRoot, "models", "bge-m3", "sentencepiece.bpe.model");

        if (File.Exists(modelPath) && File.Exists(spmPath))
        {
            try
            {
                var gen = new EmbeddingGenerator(modelPath, spmPath);
                Console.WriteLine("      [OK] Локальная ONNX-модель BGE-M3 (1024-dim) подключена.");
                return gen;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [WARN] Не удалось инициализировать BGE-M3: {ex.Message}");
            }
        }

        return null;
    }

    private static IChatClient? TryInitChatClient(IConfiguration configuration)
    {
        var authKey = configuration["SmartDocs:Llm:AuthorizationKey"]
            ?? configuration["GigaChat:AuthorizationKey"]
            ?? configuration["GIGACHAT_AUTH_KEY"]
            ?? Environment.GetEnvironmentVariable("GIGACHAT_AUTH_KEY");

        if (string.IsNullOrWhiteSpace(authKey))
        {
            return null;
        }

        try
        {
            var options = new GigaChatClientOptions
            {
                AuthorizationKey = authKey,
                DefaultModel = GigaChatModels.GigaChat2
            };
            return options.AsChatClient();
        }
        catch
        {
            return null;
        }
    }

    private static string? FindProjectRoot(string startDir)
    {
        var cur = new DirectoryInfo(startDir);
        while (cur != null)
        {
            if (File.Exists(Path.Combine(cur.FullName, "SmartDocs.sln")) ||
                File.Exists(Path.Combine(cur.FullName, "SmartDocs.slnx")))
            {
                return cur.FullName;
            }
            cur = cur.Parent;
        }
        return null;
    }
}
