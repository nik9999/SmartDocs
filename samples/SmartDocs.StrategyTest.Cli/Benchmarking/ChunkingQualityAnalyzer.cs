using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Samples.StrategyTest.Cli.Models;

namespace SmartDocs.Samples.StrategyTest.Cli.Benchmarking;

/// <summary>
/// Comprehensive analyzer for Report 1: Chunking Quality Report.
/// Evaluates size distribution, lost fragments, table preservation, token budget breaches, and metadata integrity.
/// </summary>
public static class ChunkingQualityAnalyzer
{
    public static ChunkingQualityMetric Analyze(
        string strategyName,
        string strategyDescription,
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        TokenCounter tokenCounter,
        long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(tokenCounter);

        if (chunks.Count == 0)
        {
            return new ChunkingQualityMetric(
                StrategyName: strategyName,
                StrategyDescription: strategyDescription,
                TotalChunks: 0,
                ElapsedMs: elapsedMs,
                CharMin: 0, CharAvg: 0, CharMedian: 0, CharMax: 0, CharStdDev: 0,
                TokenMin: 0, TokenAvg: 0, TokenMedian: 0, TokenMax: 0, TokenStdDev: 0,
                EmptyChunksCount: 0, DuplicateChunksCount: 0,
                DocumentCoveragePercent: 0, LostCharactersCount: document.Content.Length, UncoveredGapsCount: 1,
                TotalTablesInDoc: 0, BrokenTablesCount: 0, OrphanedTableRowsCount: 0,
                OverBudget256Count: 0, OverBudget256Percent: 0,
                OverBudget512Count: 0, OverBudget512Percent: 0,
                OverBudget1024Count: 0, OverBudget1024Percent: 0,
                MetadataErrorsCount: 0, MetadataIssues: [],
                SizeDistribution: new Dictionary<string, int>(),
                DefectExamples: ["Коллекция чанков пуста"],
                Chunks: chunks);
        }

        // 1. Статистика по символам и токенам
        var charLengths = chunks.Select(c => c.Text.Length).OrderBy(l => l).ToList();
        var tokenLengths = chunks.Select(c => tokenCounter.CountTokens(c.Text)).OrderBy(t => t).ToList();

        int charMin = charLengths[0];
        int charMax = charLengths[^1];
        double charAvg = charLengths.Average();
        int charMedian = charLengths[charLengths.Count / 2];
        double charStdDev = Math.Sqrt(charLengths.Select(val => Math.Pow(val - charAvg, 2)).Average());

        int tokenMin = tokenLengths[0];
        int tokenMax = tokenLengths[^1];
        double tokenAvg = tokenLengths.Average();
        int tokenMedian = tokenLengths[tokenLengths.Count / 2];
        double tokenStdDev = Math.Sqrt(tokenLengths.Select(val => Math.Pow(val - tokenAvg, 2)).Average());

        // 2. Пустые чанки и дубликаты
        int emptyCount = chunks.Count(c => string.IsNullOrWhiteSpace(c.Text));
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        int duplicateCount = 0;
        foreach (var c in chunks)
        {
            var hash = ComputeHash(c.Text);
            if (!hashes.Add(hash))
            {
                duplicateCount++;
            }
        }

        // 3. Анализ покрытия текста и потерянных фрагментов
        var (coveragePct, lostChars, gapsCount) = AnalyzeTextCoverage(document.Content, chunks);

        // 4. Анализ структуры таблиц
        var (totalTables, brokenTables, orphanedRows) = AnalyzeTableIntegrity(document.Content, chunks);

        // 5. Превышения токен-бюджета (256, 512, 1024)
        int over256 = tokenLengths.Count(t => t > 256);
        int over512 = tokenLengths.Count(t => t > 512);
        int over1024 = tokenLengths.Count(t => t > 1024);

        double over256Pct = (over256 / (double)chunks.Count) * 100.0;
        double over512Pct = (over512 / (double)chunks.Count) * 100.0;
        double over1024Pct = (over1024 / (double)chunks.Count) * 100.0;

        // 6. Проверка целостности метаданных
        var (metadataErrors, metadataIssues) = ValidateMetadata(document, chunks);

        // 7. Распределение размеров по корзинам
        var sizeDistribution = new Dictionary<string, int>
        {
            ["< 100"] = charLengths.Count(l => l < 100),
            ["100 - 300"] = charLengths.Count(l => l >= 100 && l < 300),
            ["300 - 600"] = charLengths.Count(l => l >= 300 && l < 600),
            ["600 - 1000"] = charLengths.Count(l => l >= 600 && l <= 1000),
            ["> 1000"] = charLengths.Count(l => l > 1000)
        };

        // 8. Детекция дефектов на стыках текста
        var defects = DetectTextBoundaryDefects(chunks);

        return new ChunkingQualityMetric(
            StrategyName: strategyName,
            StrategyDescription: strategyDescription,
            TotalChunks: chunks.Count,
            ElapsedMs: elapsedMs,
            CharMin: charMin,
            CharAvg: Math.Round(charAvg, 1),
            CharMedian: charMedian,
            CharMax: charMax,
            CharStdDev: Math.Round(charStdDev, 1),
            TokenMin: tokenMin,
            TokenAvg: Math.Round(tokenAvg, 1),
            TokenMedian: tokenMedian,
            TokenMax: tokenMax,
            TokenStdDev: Math.Round(tokenStdDev, 1),
            EmptyChunksCount: emptyCount,
            DuplicateChunksCount: duplicateCount,
            DocumentCoveragePercent: Math.Round(coveragePct, 2),
            LostCharactersCount: lostChars,
            UncoveredGapsCount: gapsCount,
            TotalTablesInDoc: totalTables,
            BrokenTablesCount: brokenTables,
            OrphanedTableRowsCount: orphanedRows,
            OverBudget256Count: over256,
            OverBudget256Percent: Math.Round(over256Pct, 1),
            OverBudget512Count: over512,
            OverBudget512Percent: Math.Round(over512Pct, 1),
            OverBudget1024Count: over1024,
            OverBudget1024Percent: Math.Round(over1024Pct, 1),
            MetadataErrorsCount: metadataErrors,
            MetadataIssues: metadataIssues,
            SizeDistribution: sizeDistribution,
            DefectExamples: defects,
            Chunks: chunks);
    }

    private static (double coveragePct, int lostChars, int gapsCount) AnalyzeTextCoverage(
        string docText,
        IReadOnlyList<DocumentChunk> chunks)
    {
        if (string.IsNullOrEmpty(docText))
        {
            return (100.0, 0, 0);
        }

        // Если у всех чанков корректно заполнены смещения, строим объединение интервалов
        bool allHaveOffsets = chunks.All(c => c.StartCharOffset >= 0 && c.EndCharOffset > c.StartCharOffset);
        if (allHaveOffsets)
        {
            var intervals = chunks
                .Select(c => (Start: Math.Max(0, c.StartCharOffset), End: Math.Min(docText.Length, c.EndCharOffset)))
                .Where(iv => iv.Start < iv.End)
                .OrderBy(iv => iv.Start)
                .ToList();

            if (intervals.Count > 0)
            {
                var merged = new List<(int Start, int End)>();
                var current = intervals[0];

                for (int i = 1; i < intervals.Count; i++)
                {
                    if (intervals[i].Start <= current.End)
                    {
                        current = (current.Start, Math.Max(current.End, intervals[i].End));
                    }
                    else
                    {
                        merged.Add(current);
                        current = intervals[i];
                    }
                }
                merged.Add(current);

                int covered = merged.Sum(iv => iv.End - iv.Start);
                int lost = Math.Max(0, docText.Length - covered);
                double pct = Math.Min(100.0, (covered / (double)docText.Length) * 100.0);
                int gaps = merged.Count - 1 + (merged[0].Start > 10 ? 1 : 0) + (docText.Length - merged[^1].End > 10 ? 1 : 0);

                return (pct, lost, gaps);
            }
        }

        // Альтернативный поиск по тексту (для чанкеров без заполненного Span):
        // Проверяем, сколько строк и параграфов исходного текста присутствуют в чанках
        var docLines = docText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 5)
            .ToList();

        if (docLines.Count == 0)
        {
            return (100.0, 0, 0);
        }

        var allChunksText = string.Join("\n", chunks.Select(c => c.Text));
        int coveredLines = docLines.Count(line => allChunksText.Contains(line, StringComparison.Ordinal));
        double linePct = (coveredLines / (double)docLines.Count) * 100.0;
        int estimatedLost = (int)(docText.Length * (1.0 - (linePct / 100.0)));
        int missingGaps = docLines.Count - coveredLines;

        return (linePct, Math.Max(0, estimatedLost), missingGaps);
    }

    private static (int totalTables, int brokenTables, int orphanedRows) AnalyzeTableIntegrity(
        string docText,
        IReadOnlyList<DocumentChunk> chunks)
    {
        // Подсчет таблиц в исходном документе по характерной шапке (|---| или | :--- |)
        int totalTables = Regex.Matches(docText, @"\|[\s-:]+\|").Count;

        int orphanedRows = 0;
        int brokenTables = 0;

        foreach (var chunk in chunks)
        {
            var lines = chunk.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool hasTableRows = lines.Any(l => l.StartsWith('|') && l.EndsWith('|'));
            bool hasTableHeaderSeparator = lines.Any(l => l.StartsWith('|') && l.Contains("---"));

            // Чанк содержит строки таблицы, но не содержит шапки таблицы
            if (hasTableRows && !hasTableHeaderSeparator)
            {
                orphanedRows++;
            }

            // Чанк начинается прямо со строки данных таблицы
            if (lines.Length > 0 && lines[0].StartsWith('|') && !lines[0].Contains("---"))
            {
                brokenTables++;
            }
        }

        return (totalTables, brokenTables, orphanedRows);
    }

    private static (int errorCount, List<string> issues) ValidateMetadata(
        Document doc,
        IReadOnlyList<DocumentChunk> chunks)
    {
        var issues = new List<string>();
        int errors = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];

            if (string.IsNullOrWhiteSpace(chunk.ChunkId))
            {
                errors++;
                issues.Add($"Чанк #{i}: пустой ChunkId");
            }

            if (chunk.ChunkIndex != i)
            {
                errors++;
                issues.Add($"Чанк #{i}: нарушение нумерации ChunkIndex (равен {chunk.ChunkIndex})");
            }

            if (chunk.Metadata == null)
            {
                errors++;
                issues.Add($"Чанк #{i}: отсутствуют метаданные Metadata");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(chunk.Metadata.Id))
                {
                    errors++;
                    issues.Add($"Чанк #{i}: пустой Id документа в метаданных");
                }

                if (string.IsNullOrWhiteSpace(chunk.Metadata.Title))
                {
                    errors++;
                    issues.Add($"Чанк #{i}: пустой Title документа");
                }
            }
        }

        return (errors, issues.Take(10).ToList());
    }

    private static List<string> DetectTextBoundaryDefects(IReadOnlyList<DocumentChunk> chunks)
    {
        var defects = new List<string>();

        foreach (var chunk in chunks)
        {
            var trimmed = chunk.Text.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // Начинается со строчной буквы (разрыв фразы)
            if (char.IsLower(trimmed[0]))
            {
                var preview = trimmed.Length > 30 ? string.Concat(trimmed.AsSpan(0, 30), "...") : trimmed;
                defects.Add($"[{chunk.ChunkId}] Начинается со строчной буквы: \"{preview.Replace("\n", " ")}\"");
            }
            // Начинается со знака препинания
            else if (trimmed[0] is '.' or ',' or ';' or ':' or ')' or ']' or '!' or '?')
            {
                defects.Add($"[{chunk.ChunkId}] Начинается со знака препинания '{trimmed[0]}'");
            }

            // Заканчивается дефисом переноса
            if (trimmed.EndsWith('-'))
            {
                defects.Add($"[{chunk.ChunkId}] Заканчивается висячим дефисом/переносом");
            }

            // Ограничиваем список примеров дефектов
            if (defects.Count >= 20)
            {
                break;
            }
        }

        return defects;
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim()));
        return Convert.ToHexString(bytes);
    }
}
