using System;
using System.Collections.Generic;
using SmartDocs.Core.Documents;
using SmartDocs.Samples.StrategyTest.Cli.Cost;

namespace SmartDocs.Samples.StrategyTest.Cli.Models;

/// <summary>
/// Metric results for Report 1: Chunking Quality Report.
/// </summary>
public sealed record ChunkingQualityMetric(
    string StrategyName,
    string StrategyDescription,
    int TotalChunks,
    long ElapsedMs,
    // Символы
    int CharMin,
    double CharAvg,
    int CharMedian,
    int CharMax,
    double CharStdDev,
    // Токены
    int TokenMin,
    double TokenAvg,
    int TokenMedian,
    int TokenMax,
    double TokenStdDev,
    // Пустые и дубликаты
    int EmptyChunksCount,
    int DuplicateChunksCount,
    // Покрытие и потерянные фрагменты
    double DocumentCoveragePercent,
    int LostCharactersCount,
    int UncoveredGapsCount,
    // Структура таблиц
    int TotalTablesInDoc,
    int BrokenTablesCount,
    int OrphanedTableRowsCount,
    // Превышения токен-бюджета
    int OverBudget256Count,
    double OverBudget256Percent,
    int OverBudget512Count,
    double OverBudget512Percent,
    int OverBudget1024Count,
    double OverBudget1024Percent,
    // Ошибки метаданных
    int MetadataErrorsCount,
    IReadOnlyList<string> MetadataIssues,
    // Распределение размеров по корзинам
    IReadOnlyDictionary<string, int> SizeDistribution,
    // Список обнаруженных дефектов
    IReadOnlyList<string> DefectExamples,
    // Сохраненные чанки для последующих бенчмарков
    IReadOnlyList<DocumentChunk> Chunks);

/// <summary>
/// A benchmark query with expected ground truth facts and keywords.
/// </summary>
public sealed record BenchmarkQuery(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedFacts,
    IReadOnlyList<string> RelevantKeywords,
    string TargetSection);

/// <summary>
/// Retrieval evaluation result for a single question.
/// </summary>
public sealed record QuestionRetrievalResult(
    BenchmarkQuery Query,
    int FirstRelevantRank, // 1-based, or -1 if not found
    float TopHitScore,
    string TopHitChunkId,
    string TopHitSnippet,
    bool FoundInTop5,
    bool FoundInTop10,
    double ReciprocalRank,
    double NdcgAt10);

/// <summary>
/// Metric results for Report 2: Retrieval Benchmark Report.
/// </summary>
public sealed record RetrievalBenchmarkMetric(
    string StrategyName,
    int TotalQueries,
    double RecallAt5,
    double RecallAt10,
    double Mrr,
    double NdcgAt10,
    double ZeroHitRatePercent,
    double AvgRetrievalMs,
    IReadOnlyList<QuestionRetrievalResult> QueryResults);

/// <summary>
/// Evaluation result for a single RAG question answer.
/// </summary>
public sealed record QuestionAnswerResult(
    BenchmarkQuery Query,
    string GeneratedAnswer,
    double CorrectnessScore, // 0.0 - 1.0 (наличие ключевых фактов)
    double FaithfulnessScore, // 0.0 - 1.0 (обоснованность текстом контекста)
    long ChunkingMs,
    long EmbeddingMs,
    long RetrievalMs,
    long GenerationMs,
    long TotalMs,
    int EmbeddingTokens,
    int PromptTokens,
    int CompletionTokens,
    CostBreakdown Cost,
    IReadOnlyList<string> RetrievedChunkIds);

/// <summary>
/// Metric results for Report 3: Answer Quality &amp; Cost Report.
/// </summary>
public sealed record AnswerQualityCostMetric(
    string StrategyName,
    int TotalEvaluatedQuestions,
    double AvgCorrectnessScore,
    double AvgFaithfulnessScore,
    // Средние задержки
    double AvgChunkingMs,
    double AvgEmbeddingMs,
    double AvgRetrievalMs,
    double AvgGenerationMs,
    double AvgTotalLatencyMs,
    // Токены суммарно
    int TotalEmbeddingTokens,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalAllTokens,
    // Стоимость
    decimal TotalCostRubles,
    decimal AvgCostPerQueryRubles,
    IReadOnlyList<QuestionAnswerResult> QuestionResults);
