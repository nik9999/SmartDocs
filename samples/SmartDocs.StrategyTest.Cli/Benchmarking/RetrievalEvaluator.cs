using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Embeddings;
using SmartDocs.Retrieval.VectorStores;
using SmartDocs.Samples.StrategyTest.Cli.Models;

namespace SmartDocs.Samples.StrategyTest.Cli.Benchmarking;

/// <summary>
/// Evaluates retrieval performance for a set of chunks against a benchmark dataset.
/// Computes Recall@5, Recall@10, MRR, nDCG@10, and Zero-Hit Rate.
/// </summary>
public static class RetrievalEvaluator
{
    public static async Task<RetrievalBenchmarkMetric> EvaluateAsync(
        string strategyName,
        IReadOnlyList<DocumentChunk> chunks,
        EmbeddingGenerator embeddingGenerator,
        IReadOnlyList<BenchmarkQuery> queries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(queries);

        if (chunks.Count == 0 || queries.Count == 0)
        {
            return new RetrievalBenchmarkMetric(
                StrategyName: strategyName,
                TotalQueries: queries.Count,
                RecallAt5: 0,
                RecallAt10: 0,
                Mrr: 0,
                NdcgAt10: 0,
                ZeroHitRatePercent: 100,
                AvgRetrievalMs: 0,
                QueryResults: []);
        }

        // 1. Создаем in-memory векторное хранилище и генерируем эмбеддинги чанков
        var vectorStore = new InMemoryVectorStore($"bench-{Guid.NewGuid():N}");
        var embeddedChunks = new List<EmbeddedChunk>(chunks.Count);

        // Генерация пакетами по 16 чанков для эффективности
        const int batchSize = 16;
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            var texts = batch.Select(c => c.Text).ToList();
            var generated = await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);

            for (int j = 0; j < batch.Count; j++)
            {
                embeddedChunks.Add(new EmbeddedChunk(batch[j], generated[j].Vector, "bge-m3"));
            }
        }

        await vectorStore.UpsertAsync(embeddedChunks, cancellationToken);

        // 2. Выполняем оценку по каждому тестовому запросу
        var queryResults = new List<QuestionRetrievalResult>(queries.Count);
        var retrievalTimes = new List<double>(queries.Count);

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            var qEmbedding = await embeddingGenerator.GenerateAsync([query.Question], cancellationToken: cancellationToken);
            var queryVector = qEmbedding[0].Vector;

            var topHits = await vectorStore.SearchAsync(queryVector, topK: 10, cancellationToken: cancellationToken);
            sw.Stop();
            retrievalTimes.Add(sw.Elapsed.TotalMilliseconds);

            int firstRank = -1;
            float topHitScore = topHits.Count > 0 ? (float)topHits[0].Score : 0f;
            string topHitChunkId = topHits.Count > 0 ? topHits[0].Chunk.ChunkId : "NONE";
            string topHitSnippet = topHits.Count > 0 ? Truncate(topHits[0].Chunk.Text, 120) : string.Empty;

            double dcg = 0.0;

            for (int r = 0; r < topHits.Count; r++)
            {
                int rank = r + 1;
                var hitText = topHits[r].Chunk.Text;
                bool isRelevant = IsChunkRelevant(hitText, query);

                if (isRelevant)
                {
                    if (firstRank == -1)
                    {
                        firstRank = rank;
                    }
                    dcg += 1.0 / Math.Log2(rank + 1);
                }
            }

            bool foundInTop5 = firstRank is >= 1 and <= 5;
            bool foundInTop10 = firstRank is >= 1 and <= 10;
            double reciprocalRank = firstRank > 0 ? 1.0 / firstRank : 0.0;

            // Идеальный IDCG@10 для одного релевантного документа
            double idcg = 1.0 / Math.Log2(1 + 1); // 1.0
            double ndcg = Math.Min(1.0, dcg / idcg);

            queryResults.Add(new QuestionRetrievalResult(
                Query: query,
                FirstRelevantRank: firstRank,
                TopHitScore: topHitScore,
                TopHitChunkId: topHitChunkId,
                TopHitSnippet: topHitSnippet,
                FoundInTop5: foundInTop5,
                FoundInTop10: foundInTop10,
                ReciprocalRank: reciprocalRank,
                NdcgAt10: ndcg));
        }

        // 3. Вычисление агрегированных метрик
        double recallAt5 = queryResults.Count(q => q.FoundInTop5) / (double)queries.Count;
        double recallAt10 = queryResults.Count(q => q.FoundInTop10) / (double)queries.Count;
        double meanMrr = queryResults.Average(q => q.ReciprocalRank);
        double meanNdcg = queryResults.Average(q => q.NdcgAt10);
        double zeroHitRate = (queryResults.Count(q => !q.FoundInTop10) / (double)queries.Count) * 100.0;
        double avgRetrievalMs = retrievalTimes.Count > 0 ? retrievalTimes.Average() : 0.0;

        return new RetrievalBenchmarkMetric(
            StrategyName: strategyName,
            TotalQueries: queries.Count,
            RecallAt5: Math.Round(recallAt5, 3),
            RecallAt10: Math.Round(recallAt10, 3),
            Mrr: Math.Round(meanMrr, 3),
            NdcgAt10: Math.Round(meanNdcg, 3),
            ZeroHitRatePercent: Math.Round(zeroHitRate, 1),
            AvgRetrievalMs: Math.Round(avgRetrievalMs, 2),
            QueryResults: queryResults);
    }

    private static bool IsChunkRelevant(string chunkText, BenchmarkQuery query)
    {
        // 1. Проверяем наличие ключевых фактов
        int factsMatched = query.ExpectedFacts.Count(fact =>
            chunkText.Contains(fact, StringComparison.OrdinalIgnoreCase));

        if (factsMatched >= Math.Min(2, query.ExpectedFacts.Count))
        {
            return true;
        }

        // 2. Проверяем совпадение ключевых слов доменного вопроса
        int keywordsMatched = query.RelevantKeywords.Count(kw =>
            chunkText.Contains(kw, StringComparison.OrdinalIgnoreCase));

        return keywordsMatched >= Math.Min(3, query.RelevantKeywords.Count);
    }

    private static string Truncate(string text, int maxChars)
    {
        var clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length <= maxChars ? clean : string.Concat(clean.AsSpan(0, maxChars), "...");
    }
}

