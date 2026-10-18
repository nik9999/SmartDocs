using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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

namespace SmartDocs.Samples.StrategyTest.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        PrintHeader();

        // 1. Конфигурация и секреты (для опционального тестирования LLM / GigaChat)
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets("smartdocs-chunking-strategy")
            .AddEnvironmentVariables()
            .Build();

        var tokenCounter = new TokenCounter();

        // 2. Поиск и выбор PDF документа
        string? pdfPath = ResolvePdfPath(args);
        if (pdfPath == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ОШИБКА] Не найден ни один PDF-файл в папке 'Documents' и не передан путь в аргументах.");
            Console.ResetColor();
            Console.WriteLine("Поместите PDF-файл документации в каталог 'Documents' или запустите:");
            Console.WriteLine("  dotnet run --project samples/SmartDocs.StrategyTest.Cli -- \"C:\\path\\to\\manual.pdf\"");
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[1/4] Загрузка PDF документа: {Path.GetFileName(pdfPath)}");
        Console.ResetColor();
        Console.WriteLine($"      Полный путь: {pdfPath}");

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
        Console.WriteLine("\n[2/4] Предобработка документа через DocumentPreprocessor...");
        var preprocessor = new DocumentPreprocessor(new DocumentPreprocessorOptions
        {
            Dehyphenate = true,
            FilterEmptyPages = true,
            RemoveDotLeaders = true,
            StripRepeatingHeadersAndFooters = false // сохраняем структуру страниц
        });

        var doc = preprocessor.Preprocess(rawDoc);

        int totalTokens = tokenCounter.CountTokens(doc.Content);
        Console.WriteLine($"      Метаданные:");
        Console.WriteLine($"        * Id:           {doc.Metadata.Id}");
        Console.WriteLine($"        * Title:        {doc.Metadata.Title}");
        Console.WriteLine($"        * Author:       {doc.Metadata.Author}");
        Console.WriteLine($"        * Office:       {doc.Metadata.Office}");
        Console.WriteLine($"        * FiscalYear:   {doc.Metadata.FiscalYear}");
        Console.WriteLine($"        * DocumentType: {doc.Metadata.DocumentType}");
        Console.WriteLine($"        * Silo:         {doc.Metadata.Silo}");
        Console.WriteLine($"      Сырой текст:          {rawDoc.Content.Length:N0} символов");
        Console.WriteLine($"      После предобработки:  {doc.Content.Length:N0} символов (~{totalTokens:N0} токенов)");

        // 5. Инициализация моделей (BGE-M3 для Semantic, GigaChat для Contextual)
        var bgeGenerator = TryInitBgeM3();
        var chatClient = TryInitChatClient(configuration);

        // 6. Формирование набора стратегий
        var strategySuite = BuildStrategySuite(tokenCounter, bgeGenerator);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[3/4] Сравнение {strategySuite.Count} стратегий чанкинга на одном документе...");
        Console.ResetColor();

        var results = new List<StrategyResult>();

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

                var eval = AnalyzeChunks(name, description, chunks, tokenCounter, sw.ElapsedMilliseconds);
                results.Add(eval);
                Console.WriteLine($"  * [{name,-32}] получено {chunks.Count,3} чанков за {sw.ElapsedMilliseconds,4} мс");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  * [{name,-32}] Сбой выполнения: {ex.Message}");
                Console.ResetColor();
            }
        }

        // 7. Сводная таблица сравнения
        PrintSummaryTable(results);

        // 8. Подробный анализ распределения и проблем для каждой стратегии
        PrintDetailedDiagnostics(results);

        // 9. Визуальная оценка репрезентативных чанков
        PrintVisualChunkSamples(results);

        // 10. Опциональный этап: отдельное тестирование ContextualChunker
        Console.WriteLine();
        PrintContextualizationTest(doc, chatClient);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[ГОТОВО] Сравнение стратегий чанкинга успешно завершено.\n");
        Console.ResetColor();

        return 0;
    }

    private static void PrintHeader()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("       SmartDocs: Лаборатория сравнения стратегий и качества чанкинга           ");
        Console.WriteLine("       (Recursive, Sentence, FixedSize, TokenAware, Decorators & Semantic)      ");
        Console.WriteLine("================================================================================\n");
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
                Console.WriteLine("      [OK] Локальная ONNX-модель BGE-M3 (1024-dim) подключена для SemanticChunker.");
                return gen;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      [WARN] Не удалось инициализировать BGE-M3: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("      [INFO] Файлы модели BGE-M3 не найдены; SemanticChunker будет пропущен.");
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

    private static List<(string Name, string Description, IChunker Chunker)> BuildStrategySuite(
        ITokenCounter tokenCounter,
        EmbeddingGenerator? bgeGenerator)
    {
        var suite = new List<(string Name, string Description, IChunker Chunker)>
        {
            (
                "Recursive (800 chars)",
                "Базовый рекурсивный чанкер с учетом заголовков и абзацев",
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

    private static StrategyResult AnalyzeChunks(
        string name,
        string description,
        List<DocumentChunk> chunks,
        TokenCounter tokenCounter,
        long elapsedMs)
    {
        int count = chunks.Count;
        if (count == 0)
        {
            return new StrategyResult(
                name, description, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                new int[5], 0, 0, 0, 0, 0, 0, elapsedMs, chunks, []);
        }

        var charLengths = chunks.Select(c => c.Text.Length).OrderBy(x => x).ToList();
        var tokenLengths = chunks.Select(c => tokenCounter.CountTokens(c.Text)).OrderBy(x => x).ToList();

        int minChar = charLengths[0];
        int maxChar = charLengths[^1];
        double avgChar = charLengths.Average();
        int medianChar = charLengths[charLengths.Count / 2];

        int minTok = tokenLengths[0];
        int maxTok = tokenLengths[^1];
        double avgTok = tokenLengths.Average();
        int medianTok = tokenLengths[tokenLengths.Count / 2];
        int totalTokens = tokenLengths.Sum();

        // Распределение по корзинам символов: <100, 100-300, 300-600, 600-1000, >1000
        var buckets = new int[5];
        foreach (int len in charLengths)
        {
            if (len < 100)
            {
                buckets[0]++;
            }
            else if (len < 300)
            {
                buckets[1]++;
            }
            else if (len < 600)
            {
                buckets[2]++;
            }
            else if (len < 1000)
            {
                buckets[3]++;
            }
            else
            {
                buckets[4]++;
            }
        }

        // Проверка аномалий и дефектов границ
        int emptyCount = 0;
        int duplicateCount = 0;
        int startLowerCount = 0;
        int startPunctCount = 0;
        int brokenWordEndCount = 0;
        int brokenTableCount = 0;

        var seenTexts = new HashSet<string>(StringComparer.Ordinal);
        var issueReports = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var text = chunks[i].Text;
            var chunkId = chunks[i].ChunkId;

            if (string.IsNullOrWhiteSpace(text))
            {
                emptyCount++;
                issueReports.Add($"[{chunkId}] Пустой чанк");
                continue;
            }

            // Повторы
            if (!seenTexts.Add(text.Trim()))
            {
                duplicateCount++;
                issueReports.Add($"[{chunkId}] Дубликат текста");
            }

            var trimmed = text.Trim();

            // Начинается с пунктуации
            if (trimmed.Length > 0 && trimmed[0] is '.' or ',' or ';' or ':' or ')' or ']' or '}')
            {
                startPunctCount++;
                issueReports.Add($"[{chunkId}] Начинается со знака препинания '{trimmed[0]}'");
            }
            // Начинается со строчной буквы (разрыв фразы)
            else if (trimmed.Length > 0 && char.IsLower(trimmed[0]))
            {
                startLowerCount++;
                issueReports.Add($"[{chunkId}] Начинается со строчной буквы: \"{trimmed[..Math.Min(25, trimmed.Length)]}...\"");
            }

            // Заканчивается разорванным словом (дефис на конце)
            if (trimmed.EndsWith('-') || trimmed.EndsWith('—'))
            {
                brokenWordEndCount++;
                issueReports.Add($"[{chunkId}] Заканчивается висячим дефисом/переносом");
            }

            // Разрыв таблицы (содержит разделитель |, но не содержит шапки таблицы ---)
            if (trimmed.Contains('|') && !trimmed.Contains("---", StringComparison.Ordinal) && !trimmed.StartsWith('#'))
            {
                brokenTableCount++;
                issueReports.Add($"[{chunkId}] Обрезанная строка таблицы без шапки заголовков");
            }
        }

        return new StrategyResult(
            name,
            description,
            count,
            minChar,
            maxChar,
            avgChar,
            medianChar,
            minTok,
            maxTok,
            avgTok,
            medianTok,
            totalTokens,
            buckets,
            emptyCount,
            duplicateCount,
            startLowerCount,
            startPunctCount,
            brokenWordEndCount,
            brokenTableCount,
            elapsedMs,
            chunks,
            issueReports);
    }

    private static void PrintSummaryTable(List<StrategyResult> results)
    {
        Console.WriteLine("\n=============================================================================================================");
        Console.WriteLine("                                СВОДНАЯ СРАВНИТЕЛЬНАЯ ТАБЛИЦА СТРАТЕГИЙ                                      ");
        Console.WriteLine("=============================================================================================================");
        Console.WriteLine(" Стратегия                      │ Чанков │ Символы (min/avg/max) │ Токены (min/avg/max) │ Пуст │ Дубль │ Дефекты │ Время ");
        Console.WriteLine("────────────────────────────────┼────────┼───────────────────────┼──────────────────────┼──────┼───────┼─────────┼───────");

        foreach (var r in results)
        {
            int totalIssues = r.StartLowerCount + r.StartPunctCount + r.BrokenWordEndCount + r.BrokenTableCount;
            string charStats = string.Format(CultureInfo.InvariantCulture, "{0,3}/{1,3:F0}/{2,4}", r.MinChar, r.AvgChar, r.MaxChar);
            string tokStats = string.Format(CultureInfo.InvariantCulture, "{0,3}/{1,3:F0}/{2,4}", r.MinTokens, r.AvgTokens, r.MaxTokens);

            Console.Write($" {r.Name,-30} │");
            Console.Write($" {r.TotalChunks,6} │");
            Console.Write($" {charStats,-21} │");
            Console.Write($" {tokStats,-20} │");

            if (r.EmptyChunks > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($" {r.EmptyChunks,4} │");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"    0 │");
            }

            if (r.DuplicateChunks > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($" {r.DuplicateChunks,5} │");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"     0 │");
            }

            if (totalIssues > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($" {totalIssues,7} │");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"       0 │");
                Console.ResetColor();
            }

            Console.WriteLine($" {r.ElapsedMs,4} мс");
        }

        Console.WriteLine("=============================================================================================================");
    }

    private static void PrintDetailedDiagnostics(List<StrategyResult> results)
    {
        Console.WriteLine("\n[4/4] ДЕТАЛЬНЫЙ АНАЛИЗ ГРАНИЦ И РАСПРЕДЕЛЕНИЯ РАЗМЕРОВ:");

        foreach (var r in results)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n▶ {r.Name} ({r.Description})");
            Console.ResetColor();
            Console.WriteLine($"   * Чанков: {r.TotalChunks} | Всего токенов: {r.TotalTokens:N0} | Время: {r.ElapsedMs} мс");
            Console.WriteLine($"   * Символы: Мин={r.MinChar}, Медиана={r.MedianChar}, Среднее={r.AvgChar:F1}, Макс={r.MaxChar}");
            Console.WriteLine($"   * Токены:  Мин={r.MinTokens}, Медиана={r.MedianTokens}, Среднее={r.AvgTokens:F1}, Макс={r.MaxTokens}");

            // Гистограмма корзин
            Console.WriteLine("   * Распределение по размеру (символов):");
            Console.WriteLine($"       [ < 100]:     {r.Buckets[0],3} {new string('█', Math.Min(30, r.Buckets[0]))}");
            Console.WriteLine($"       [100 - 300]:  {r.Buckets[1],3} {new string('█', Math.Min(30, r.Buckets[1]))}");
            Console.WriteLine($"       [300 - 600]:  {r.Buckets[2],3} {new string('█', Math.Min(30, r.Buckets[2]))}");
            Console.WriteLine($"       [600 - 1000]: {r.Buckets[3],3} {new string('█', Math.Min(30, r.Buckets[3]))}");
            Console.WriteLine($"       [ > 1000]:    {r.Buckets[4],3} {new string('█', Math.Min(30, r.Buckets[4]))}");

            // Аномалии
            int totalIssues = r.StartLowerCount + r.StartPunctCount + r.BrokenWordEndCount + r.BrokenTableCount + r.DuplicateChunks + r.EmptyChunks;
            if (totalIssues == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   * Аномалии границ и целостности: не обнаружено (чистое разбиение).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   * Обнаружено потенциальных дефектов: {totalIssues}");
                Console.ResetColor();
                if (r.StartLowerCount > 0)
                {
                    Console.WriteLine($"     - Начинаются со строчной буквы (возможен разрыв фразы): {r.StartLowerCount}");
                }
                if (r.StartPunctCount > 0)
                {
                    Console.WriteLine($"     - Начинаются с пунктуации (мусор на стыке): {r.StartPunctCount}");
                }
                if (r.BrokenWordEndCount > 0)
                {
                    Console.WriteLine($"     - Заканчиваются висячим дефисом/переносом: {r.BrokenWordEndCount}");
                }
                if (r.BrokenTableCount > 0)
                {
                    Console.WriteLine($"     - Строки таблиц оторваны от заголовков: {r.BrokenTableCount}");
                }
                if (r.DuplicateChunks > 0)
                {
                    Console.WriteLine($"     - Полные дубликаты содержимого: {r.DuplicateChunks}");
                }

                // Первые 3 примера проблем
                if (r.IssueReports.Count > 0)
                {
                    Console.WriteLine("     Примеры замечаний:");
                    foreach (var issue in r.IssueReports.Take(3))
                    {
                        Console.WriteLine($"       • {issue}");
                    }
                }
            }
        }
    }

    private static void PrintVisualChunkSamples(List<StrategyResult> results)
    {
        Console.WriteLine("\n=============================================================================================================");
        Console.WriteLine("                    ВИЗУАЛЬНЫЕ ПРИМЕРЫ ЧАНКОВ (ОЦЕНКА СОХРАНЕНИЯ СМЫСЛА И ТАБЛИЦ)                            ");
        Console.WriteLine("=============================================================================================================");

        // Выбираем ключевые стратегии для наглядного сравнения
        string[] featured =
        [
            "Recursive (800 chars)",
            "Recursive(800) + Token(256)",
            "Sentence (5 sent)",
            "FixedSize (500 / ov 100)"
        ];

        foreach (var r in results.Where(res => featured.Contains(res.Name)))
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n─── [Примеры чанков]: {r.Name} ({r.TotalChunks} чанков) ───");
            Console.ResetColor();

            if (r.Chunks.Count == 0)
            {
                continue;
            }

            // 1. Первый чанк (титул/начало)
            PrintSingleChunkPreview("Первый чанк (Начало документа)", r.Chunks[0]);

            // 2. Чанк с таблицей или из середины
            var tableChunk = r.Chunks.FirstOrDefault(c => c.Text.Contains('|') && c.Text.Contains("---"));
            if (tableChunk != null && tableChunk.ChunkIndex != r.Chunks[0].ChunkIndex)
            {
                PrintSingleChunkPreview("Чанк с табличной структурой", tableChunk);
            }
            else if (r.Chunks.Count > 2)
            {
                PrintSingleChunkPreview("Чанк из середины", r.Chunks[r.Chunks.Count / 2]);
            }
        }
    }

    private static void PrintSingleChunkPreview(string label, DocumentChunk chunk)
    {
        Console.WriteLine($"\n  ▶ {label} | ID: {chunk.ChunkId} | Размер: {chunk.Text.Length} симв.");
        Console.WriteLine("  ┌" + new string('─', 78) + "┐");

        var lines = chunk.Text.Split('\n');
        int displayed = 0;
        foreach (var line in lines)
        {
            string l = line.TrimEnd('\r');
            if (l.Length > 76)
            {
                l = string.Concat(l.AsSpan(0, 73), "...");
            }
            Console.WriteLine($"  │ {l,-76} │");
            displayed++;
            if (displayed >= 8 && lines.Length > 10)
            {
                Console.WriteLine($"  │ {"[... пропущено строк: " + (lines.Length - displayed) + " ...]",-76} │");
                break;
            }
        }

        Console.WriteLine("  └" + new string('─', 78) + "┘");
    }

    private static void PrintContextualizationTest(Document doc, IChatClient? chatClient)
    {
        Console.WriteLine("=============================================================================================================");
        Console.WriteLine("                    ОТДЕЛЬНЫЙ ТЕСТ КОНТЕКСТУАЛИЗАЦИИ (ContextualChunker)                                     ");
        Console.WriteLine("=============================================================================================================");

        if (chatClient == null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [ПРОПУЩЕНО] IChatClient не настроен.");
            Console.WriteLine("  Чтобы протестировать обогащение чанков ситуационным контекстом (Anthropic Contextual Retrieval),");
            Console.WriteLine("  укажите GIGACHAT_AUTH_KEY в переменных окружения или User Secrets.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  [OK] Чат-модель обнаружена. Запуск ContextualChunker для демонстрации обогащения контекстом...");
        Console.ResetColor();

        try
        {
            var baseChunker = new RecursiveCharacterChunker(maxChunkSize: 500);
            var contextualChunker = new ContextualChunker(baseChunker, chatClient);

            var sw = Stopwatch.StartNew();
            var rawChunks = new List<DocumentChunk>();
            foreach (var c in baseChunker.ChunkAsync(doc).ToBlockingEnumerable().Take(2))
            {
                rawChunks.Add(c);
            }

            Console.WriteLine($"\n  Анализ добавления ситуационного контекста на первых 2 чанках:");

            foreach (var raw in rawChunks)
            {
                var contextSw = Stopwatch.StartNew();
                // ContextualChunker обогащает каждый чанк отдельным запросом к LLM
                var singleDoc = new Document(doc.Metadata, raw.Text, doc.SourcePath);
                var enrichedList = contextualChunker.ChunkAsync(singleDoc).ToBlockingEnumerable().ToList();
                contextSw.Stop();

                if (enrichedList.Count > 0)
                {
                    var enriched = enrichedList[0];
                    string prefix = string.Empty;
                    if (enriched.Text.Length > raw.Text.Length)
                    {
                        prefix = enriched.Text[..(enriched.Text.Length - raw.Text.Length)].Trim();
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n  ─── Чанк #{raw.ChunkIndex} ({contextSw.ElapsedMilliseconds} мс) ───");
                    Console.ResetColor();
                    Console.WriteLine($"  [Сгенерированный контекст LLM]:\n  «{prefix}»\n");
                    Console.WriteLine($"  [Исходный текст]: {raw.Text[..Math.Min(120, raw.Text.Length)].Replace("\n", " ", StringComparison.Ordinal)}...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [Ошибка при вызове LLM]: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static string? FindProjectRoot(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SmartDocs.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "SmartDocs.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    private static IEnumerable<T> ToBlockingEnumerable<T>(this IAsyncEnumerable<T> source)
    {
        var enumerator = source.GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed record StrategyResult(
        string Name,
        string Description,
        int TotalChunks,
        int MinChar,
        int MaxChar,
        double AvgChar,
        int MedianChar,
        int MinTokens,
        int MaxTokens,
        double AvgTokens,
        int MedianTokens,
        int TotalTokens,
        int[] Buckets,
        int EmptyChunks,
        int DuplicateChunks,
        int StartLowerCount,
        int StartPunctCount,
        int BrokenWordEndCount,
        int BrokenTableCount,
        long ElapsedMs,
        IReadOnlyList<DocumentChunk> Chunks,
        IReadOnlyList<string> IssueReports);
}
