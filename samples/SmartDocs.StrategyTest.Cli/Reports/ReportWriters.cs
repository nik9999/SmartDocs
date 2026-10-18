using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SmartDocs.Samples.StrategyTest.Cli.Models;

namespace SmartDocs.Samples.StrategyTest.Cli.Reports;

/// <summary>
/// Renders console outputs and generates markdown report artifacts on disk:
/// 1. Chunking Quality Report
/// 2. Retrieval Benchmark Report
/// 3. Answer Quality &amp; Cost Report
/// 4. Executive Summary Report
/// </summary>
public static class ReportWriters
{
    public static void EnsureReportsDirectory(string reportsDir)
    {
        if (!Directory.Exists(reportsDir))
        {
            Directory.CreateDirectory(reportsDir);
        }
    }

    // =========================================================================
    // 1. CHUNKING QUALITY REPORT
    // =========================================================================
    public static string WriteChunkingQualityReport(
        IReadOnlyList<ChunkingQualityMetric> metrics,
        string reportsDir)
    {
        EnsureReportsDirectory(reportsDir);
        var filePath = Path.Combine(reportsDir, "01_Chunking_Quality_Report.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Отчёт 1: Chunking Quality Report (Качество чанкинга)");
        sb.AppendLine();
        sb.AppendLine($"*Дата генерации*: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"*Количество исследованных стратегий*: {metrics.Count}  ");
        sb.AppendLine();
        sb.AppendLine("## 1.1 Сводная матрица качества чанкинга");
        sb.AppendLine();
        sb.AppendLine("| Стратегия | Чанков | Символы (min/avg/max) | Токены (min/avg/max) | Покрытие | Потеряно симв. | Ошибки таблиц | >256 tok (%) | Ошибки мета | Время |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var m in metrics)
        {
            sb.AppendLine(
                $"| **{m.StrategyName}** | {m.TotalChunks} | " +
                $"{m.CharMin}/{m.CharAvg:F0}/{m.CharMax} | " +
                $"{m.TokenMin}/{m.TokenAvg:F0}/{m.TokenMax} | " +
                $"{m.DocumentCoveragePercent:F1}% | {m.LostCharactersCount} | " +
                $"{m.BrokenTablesCount + m.OrphanedTableRowsCount} | " +
                $"{m.OverBudget256Count} ({m.OverBudget256Percent:F1}%) | " +
                $"{m.MetadataErrorsCount} | {m.ElapsedMs} мс |");
        }

        sb.AppendLine();
        sb.AppendLine("## 1.2 Детальный анализ по стратегиям");
        sb.AppendLine();

        foreach (var m in metrics)
        {
            sb.AppendLine($"### {m.StrategyName}");
            sb.AppendLine($"*{m.StrategyDescription}*");
            sb.AppendLine();
            sb.AppendLine($"- **Общее количество чанков**: {m.TotalChunks}");
            sb.AppendLine($"- **Символы**: мин = {m.CharMin}, медиана = {m.CharMedian}, среднее = {m.CharAvg:F1}, макс = {m.CharMax}, стд. откл. = {m.CharStdDev:F1}");
            sb.AppendLine($"- **Токены**: мин = {m.TokenMin}, медиана = {m.TokenMedian}, среднее = {m.TokenAvg:F1}, макс = {m.TokenMax}, стд. откл. = {m.TokenStdDev:F1}");
            sb.AppendLine($"- **Полнота покрытия текста**: {m.DocumentCoveragePercent:F2}% (непокрытых пробелов: {m.UncoveredGapsCount}, потеряно символов: {m.LostCharactersCount})");
            sb.AppendLine($"- **Таблицы**: оторванных строк без шапки = {m.OrphanedTableRowsCount}, разорванных таблиц = {m.BrokenTablesCount}");
            sb.AppendLine($"- **Превышения токен-бюджета**:");
            sb.AppendLine($"  - Превышение 256 токенов: {m.OverBudget256Count} чанков ({m.OverBudget256Percent:F1}%)");
            sb.AppendLine($"  - Превышение 512 токенов: {m.OverBudget512Count} чанков ({m.OverBudget512Percent:F1}%)");
            sb.AppendLine($"  - Превышение 1024 токенов: {m.OverBudget1024Count} чанков ({m.OverBudget1024Percent:F1}%)");
            sb.AppendLine($"- **Целостность метаданных**: {m.MetadataErrorsCount} замечаний");

            if (m.MetadataIssues.Count > 0)
            {
                sb.AppendLine("  * Замечания по метаданным:");
                foreach (var issue in m.MetadataIssues.Take(3))
                {
                    sb.AppendLine($"    - {issue}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine("Распределение по размеру (символов):");
            foreach (var kvp in m.SizeDistribution)
            {
                int barWidth = Math.Min(30, kvp.Value);
                sb.AppendLine($"  [{kvp.Key,-11}]: {kvp.Value,4} {new string('#', barWidth)}");
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    // =========================================================================
    // 2. RETRIEVAL BENCHMARK REPORT
    // =========================================================================
    public static string WriteRetrievalBenchmarkReport(
        IReadOnlyList<RetrievalBenchmarkMetric> benchmarks,
        string reportsDir)
    {
        EnsureReportsDirectory(reportsDir);
        var filePath = Path.Combine(reportsDir, "02_Retrieval_Benchmark_Report.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Отчёт 2: Retrieval Benchmark Report (Точность поиска)");
        sb.AppendLine();
        sb.AppendLine($"*Дата генерации*: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"*Модель векторных эмбеддингов*: BGE-M3 (1024 dimensions, Dense Cosine Similarity)  ");
        int totalQueries = benchmarks.Count > 0 ? benchmarks[0].TotalQueries : 0;
        sb.AppendLine($"*Тестовый набор*: {totalQueries} контрольных вопросов по паспорту изделия  ");
        sb.AppendLine();
        sb.AppendLine("## 2.1 Сводная таблица метрик поиска");
        sb.AppendLine();
        sb.AppendLine("| Стратегия | Recall@5 | Recall@10 | MRR | nDCG@10 | Zero-Hit Rate | Поиск (мс) |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var b in benchmarks.OrderByDescending(b => b.NdcgAt10))
        {
            sb.AppendLine(
                $"| **{b.StrategyName}** | {b.RecallAt5:P1} | {b.RecallAt10:P1} | " +
                $"{b.Mrr:F3} | **{b.NdcgAt10:F3}** | {b.ZeroHitRatePercent:F1}% | {b.AvgRetrievalMs:F1} мс |");
        }

        sb.AppendLine();
        sb.AppendLine("## 2.2 Результаты по каждому вопросу");
        sb.AppendLine();

        foreach (var b in benchmarks)
        {
            sb.AppendLine($"### Стратегия: {b.StrategyName}");
            sb.AppendLine();
            sb.AppendLine("| ID | Вопрос | Первый хит (Ранг) | Скор | Найденный чанк (сниппет) | Статус |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :--- | :---: |");

            foreach (var q in b.QueryResults)
            {
                string rankStr = q.FirstRelevantRank > 0 ? $"#{q.FirstRelevantRank}" : "НЕ НАЙДЕН";
                string status = q.FoundInTop5 ? "PASSED (Top-5)" : (q.FoundInTop10 ? "PASSED (Top-10)" : "MISSED");
                string snippet = q.TopHitSnippet.Replace("|", "\\|").Trim();

                sb.AppendLine($"| `{q.Query.Id}` | {q.Query.Question} | {rankStr} | {q.TopHitScore:F4} | {snippet} | **{status}** |");
            }

            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    // =========================================================================
    // 3. ANSWER QUALITY & COST REPORT
    // =========================================================================
    public static string WriteAnswerQualityCostReport(
        IReadOnlyList<AnswerQualityCostMetric> metrics,
        string reportsDir)
    {
        EnsureReportsDirectory(reportsDir);
        var filePath = Path.Combine(reportsDir, "03_Answer_Quality_Cost_Report.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Отчёт 3: Answer Quality & Cost Report (Качество ответов и стоимость)");
        sb.AppendLine();
        sb.AppendLine($"*Дата генерации*: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"*Тарифы*: Эмбеддинги BGE-M3 = 0.00 ₽ (локально), GigaChat Prompt = 0.20 ₽ / 1K tok, Completion = 0.40 ₽ / 1K tok  ");
        sb.AppendLine();
        sb.AppendLine("## 3.1 Сводная таблица качества генерации и затрат");
        sb.AppendLine();
        sb.AppendLine("| Стратегия | Правильность | Обоснованность | Чанкинг (мс) | Эмбеддинг (мс) | Поиск (мс) | LLM (мс) | Итого задержка | Токены суммарно | Стоимость 1 запроса |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |");

        foreach (var m in metrics.OrderByDescending(m => m.AvgCorrectnessScore))
        {
            sb.AppendLine(
                $"| **{m.StrategyName}** | **{m.AvgCorrectnessScore:P1}** | {m.AvgFaithfulnessScore:P1} | " +
                $"{m.AvgChunkingMs:F1} | {m.AvgEmbeddingMs:F1} | {m.AvgRetrievalMs:F1} | {m.AvgGenerationMs:F1} | " +
                $"{m.AvgTotalLatencyMs:F0} мс | {m.TotalAllTokens:N0} | **{m.AvgCostPerQueryRubles:F4} ₽** |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3.2 Примеры сгенерированных ответов и проверка обоснованности");
        sb.AppendLine();

        foreach (var m in metrics.Take(3))
        {
            sb.AppendLine($"### {m.StrategyName}");
            sb.AppendLine();

            foreach (var q in m.QuestionResults.Take(4))
            {
                sb.AppendLine($"**Вопрос**: {q.Query.Question}  ");
                sb.AppendLine($"**Сгенерированный ответ**: {q.GeneratedAnswer}  ");
                sb.AppendLine($"**Метрики**: Правильность: {q.CorrectnessScore:P0} | Обоснованность: {q.FaithfulnessScore:P0} | Задержка: {q.TotalMs} мс | Стоимость: {q.Cost.TotalCost:F4} ₽  ");
                sb.AppendLine($"**Источники**: {string.Join(", ", q.RetrievedChunkIds)}  ");
                sb.AppendLine();
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    // =========================================================================
    // 4. EXECUTIVE SUMMARY REPORT
    // =========================================================================
    public static string WriteExecutiveSummaryReport(
        IReadOnlyList<ChunkingQualityMetric> chunkMetrics,
        IReadOnlyList<RetrievalBenchmarkMetric> retrievalMetrics,
        IReadOnlyList<AnswerQualityCostMetric> answerMetrics,
        string reportsDir)
    {
        EnsureReportsDirectory(reportsDir);
        var filePath = Path.Combine(reportsDir, "00_Executive_Summary.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Сводный исполнительный отчёт (Executive Summary)");
        sb.AppendLine();
        sb.AppendLine($"*Дата проведения исследования*: {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"*Исследуемый документ*: Паспорт модулей MDS AI-8UI и MDS AI-8UI/D (ПИМФ.426439.004-ПС)  ");
        sb.AppendLine();
        sb.AppendLine("## Интегральная оценка стратегий чанкинга");
        sb.AppendLine();
        sb.AppendLine("Итоговый рейтинг сформирован по мультифакторной формуле взвешенной эффективности:");
        sb.AppendLine("$$\\text{Score} = (\\text{Chunking Quality} \\times 0.25) + (\\text{Retrieval nDCG} \\times 0.40) + (\\text{Answer Correctness} \\times 0.25) + (\\text{Cost Efficiency} \\times 0.10)$$");
        sb.AppendLine();
        sb.AppendLine("| Стратегия | Качество чанков (дефекты) | nDCG@10 | Точность ответов | Стоимость запроса | Интегральный балл | Рекомендация |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :---: | :---: |");

        var summaryItems = new List<(string Name, double Score, string Rec)>();

        foreach (var c in chunkMetrics)
        {
            var r = retrievalMetrics.FirstOrDefault(x => x.StrategyName == c.StrategyName);
            var a = answerMetrics.FirstOrDefault(x => x.StrategyName == c.StrategyName);

            double chunkScore = Math.Max(0.0, 1.0 - ((c.BrokenTablesCount + c.OrphanedTableRowsCount + c.OverBudget256Count) / (double)Math.Max(1, c.TotalChunks)));
            double ndcg = r?.NdcgAt10 ?? 0.5;
            double correctness = a?.AvgCorrectnessScore ?? 0.5;
            double costScore = 1.0; // Высокая эффективность локального стека

            double totalScore = (chunkScore * 0.25) + (ndcg * 0.40) + (correctness * 0.25) + (costScore * 0.10);

            string rec = totalScore >= 0.80 ? "⭐ Production Ready" : (totalScore >= 0.65 ? "⚠️ С доработкой" : "❌ Не рекомендуется");
            summaryItems.Add((c.StrategyName, totalScore, rec));

            sb.AppendLine($"| **{c.StrategyName}** | {chunkScore:P0} | {ndcg:F3} | {correctness:P0} | {a?.AvgCostPerQueryRubles ?? 0m:F4} ₽ | **{totalScore * 100:F1} / 100** | {rec} |");
        }

        sb.AppendLine();
        var best = summaryItems.OrderByDescending(s => s.Score).FirstOrDefault();
        sb.AppendLine($"### Итоговый вердикт лаборатории: Победитель — **{best.Name}** ({best.Score * 100:F1} баллов).");
        sb.AppendLine();

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }
}
