using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Multimodal;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class TokenAwareChunkerTests
{
    private readonly TokenCounter _tokenCounter = new();

    private static DocumentMetadata CreateMetadata(string id = "doc1") =>
        new(id, "technical-docs", "Engineering", "Moscow", "Internal", "Reference", 2026, "Tester", new DateOnly(2026, 1, 1), "Test Document");

    private static Document CreateDocument(string content, string id = "doc1") =>
        new(CreateMetadata(id), content, $"docs/{id}.md");

    private static DocumentChunk CreateChunk(string chunkId, string docId, int chunkIndex, string text, int start = 0, int end = 0) =>
        new(chunkId, docId, chunkIndex, text, start, end == 0 ? text.Length : end, CreateMetadata(docId));

    [Fact]
    public void ChunkingTokenBudgets_CalculatesBaseBudgetAndValidatesLimits()
    {
        // Default budgets
        var defaultBudgets = new ChunkingTokenBudgets();
        Assert.Equal(450, defaultBudgets.FinalTargetCeiling);
        Assert.Equal(80, defaultBudgets.ContextBudget);
        Assert.Equal(70, defaultBudgets.MultimodalBudget);
        Assert.Equal(300, defaultBudgets.BaseTextBudget);

        // Custom budgets
        var custom = new ChunkingTokenBudgets(finalTargetCeiling: 512, contextBudget: 100, multimodalBudget: 112);
        Assert.Equal(300, custom.BaseTextBudget);

        // Invalid: reserves exceed ceiling
        Assert.Throws<ArgumentException>(() => new ChunkingTokenBudgets(finalTargetCeiling: 200, contextBudget: 120, multimodalBudget: 100));

        // Invalid: negative values
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingTokenBudgets(finalTargetCeiling: -10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingTokenBudgets(finalTargetCeiling: 500, contextBudget: -5));
    }

    [Fact]
    public void TokenAwareChunker_SplitIfNeeded_WithinBudget_ReturnsOriginalChunk()
    {
        var chunker = new TokenAwareChunker(_tokenCounter, maxTokens: 100);
        var chunk = CreateChunk("doc1#0", "doc1", 0, "Короткий текст документа для проверки бюджета токенов.");

        var result = chunker.SplitIfNeeded(chunk).ToList();

        Assert.Single(result);
        Assert.Equal(chunk.Text, result[0].Text);
        Assert.Equal(chunk.ChunkId, result[0].ChunkId);
    }

    [Fact]
    public void TokenAwareChunker_SplitIfNeeded_ExceedingBudget_SplitsCleanly()
    {
        var chunker = new TokenAwareChunker(_tokenCounter, maxTokens: 30);
        var longText = string.Join(" ", Enumerable.Repeat("Модуль ввода аналоговый МВА8 предназначен для сбора сигналов с датчиков.", 10));

        var chunk = CreateChunk("doc1#0", "doc1", 0, longText);

        var pieces = chunker.SplitIfNeeded(chunk).ToList();

        Assert.True(pieces.Count > 1, "Expected oversized chunk to be split into multiple pieces");
        foreach (var piece in pieces)
        {
            var tokenCount = _tokenCounter.CountTokens(piece.Text);
            Assert.True(tokenCount <= 30, $"Piece token count {tokenCount} exceeded maxTokens 30");
        }
    }

    [Fact]
    public void TokenAwareChunker_EnforceBudget_ReindexesConsecutively()
    {
        var chunker = new TokenAwareChunker(_tokenCounter, maxTokens: 25);

        var chunk1 = CreateChunk("doc1#0", "doc1", 0, "Короткий фрагмент.");
        var chunk2 = CreateChunk("doc1#1", "doc1", 1, string.Join(" ", Enumerable.Repeat("Длинный фрагмент с повторяющимся текстом для проверки разбиения.", 6)));
        var chunk3 = CreateChunk("doc1#2", "doc1", 2, "Завершающий фрагмент.");

        var enforced = chunker.EnforceBudget(new[] { chunk1, chunk2, chunk3 }).ToList();

        Assert.True(enforced.Count >= 3);
        for (int i = 0; i < enforced.Count; i++)
        {
            Assert.Equal(i, enforced[i].ChunkIndex);
            Assert.Equal($"doc1#{i}", enforced[i].ChunkId);
            Assert.True(_tokenCounter.CountTokens(enforced[i].Text) <= 25);
        }
    }

    [Fact]
    public async Task TokenAwareChunker_EnforceBudgetAsync_MatchesSynchronous()
    {
        var chunker = new TokenAwareChunker(_tokenCounter, maxTokens: 25);

        var chunk1 = CreateChunk("doc1#0", "doc1", 0, "Короткий фрагмент.");
        var chunk2 = CreateChunk("doc1#1", "doc1", 1, string.Join(" ", Enumerable.Repeat("Длинный фрагмент с повторяющимся текстом для проверки разбиения.", 6)));

        async IAsyncEnumerable<DocumentChunk> GetChunksAsync()
        {
            yield return chunk1;
            yield return chunk2;
            await Task.Yield();
        }

        var results = new List<DocumentChunk>();
        await foreach (var piece in chunker.EnforceBudgetAsync(GetChunksAsync()))
        {
            results.Add(piece);
        }

        Assert.True(results.Count > 2);
        for (int i = 0; i < results.Count; i++)
        {
            Assert.Equal(i, results[i].ChunkIndex);
            Assert.True(_tokenCounter.CountTokens(results[i].Text) <= 25);
        }
    }

    [Fact]
    public async Task TokenAwareChunker_Standalone_ThrowsOnChunkAsyncDocument()
    {
        var chunker = new TokenAwareChunker(_tokenCounter, maxTokens: 100);
        var doc = CreateDocument("Test content", "doc-1");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in chunker.ChunkAsync(doc)) { }
        });
    }

    [Fact]
    public void DualBudgetPipeline_GuaranteesCeilingAfterContextualAndMultimodalEnrichment()
    {
        // 1. Separate budgets
        var budgets = new ChunkingTokenBudgets(finalTargetCeiling: 100, contextBudget: 35, multimodalBudget: 35);
        Assert.Equal(30, budgets.BaseTextBudget);

        // 2. Base chunk fits BaseTextBudget (<= 30 tokens)
        var baseText = "Модуль МВА8 измеряет сигналы.";
        var baseChunk = CreateChunk("doc1#0", "doc1", 0, baseText);
        Assert.True(_tokenCounter.CountTokens(baseChunk.Text) <= budgets.BaseTextBudget);

        // 3. Contextual LLM enrichment adds context
        var contextualPrefix = string.Join(" ", Enumerable.Repeat("Данный раздел технического руководства описывает модуль МВА8.", 3));
        var enrichedText = $"{contextualPrefix}\n\n{baseChunk.Text}";

        // 4. Multimodal enrichment adds vision caption and table
        var multimodalSuffix = string.Join(" ", Enumerable.Repeat("\n\n[Figure 1] Схема внешних подключений датчиков к клеммной колодке прибора МВА8.", 3));
        var fullyAugmentedText = $"{enrichedText}{multimodalSuffix}";

        var fullyAugmentedChunk = baseChunk with { Text = fullyAugmentedText };
        int totalTokens = _tokenCounter.CountTokens(fullyAugmentedChunk.Text);

        // The chunk now exceeds finalTargetCeiling (100 tokens)!
        Assert.True(totalTokens > budgets.FinalTargetCeiling, $"Expected enriched chunk to exceed ceiling {budgets.FinalTargetCeiling}, was {totalTokens}");

        // 5. Post-enrichment safety enforcement: re-split any chunk that exceeds FinalTargetCeiling
        var postEnrichmentSafety = new TokenAwareChunker(_tokenCounter, maxTokens: budgets.FinalTargetCeiling);
        var finalChunks = postEnrichmentSafety.EnforceBudget(new[] { fullyAugmentedChunk }).ToList();

        // All emitted pieces must strictly satisfy the final ceiling!
        Assert.True(finalChunks.Count > 1, "Expected oversized enriched chunk to be re-split");
        for (int i = 0; i < finalChunks.Count; i++)
        {
            var pieceTokens = _tokenCounter.CountTokens(finalChunks[i].Text);
            Assert.True(pieceTokens <= budgets.FinalTargetCeiling,
                $"Final chunk #{i} has {pieceTokens} tokens, which exceeds hard ceiling {budgets.FinalTargetCeiling}");
            Assert.Equal(i, finalChunks[i].ChunkIndex);
            Assert.Equal($"doc1#{i}", finalChunks[i].ChunkId);
        }
    }

    [Fact]
    public async Task MultimodalChunker_WithTokenBudget_SplitsOversizedChunks()
    {
        var innerChunker = new FixedSizeChunker(chunkSize: 100, overlap: 0);
        var image = new ExtractedImage(
            ImageId: "doc1#img-1",
            PageNumber: 1,
            Caption: string.Join(" ", Enumerable.Repeat("Подробная схема подключения прибора с описанием каждого контакта колодки.", 8)),
            ImagePath: null);

        // MultimodalChunker configured with token budget ceiling of 40 tokens
        var mmChunker = new MultimodalChunker(
            innerChunker,
            images: new[] { image },
            tables: Array.Empty<ExtractedTable>(),
            tokenCounter: _tokenCounter,
            maxTokens: 40);

        var doc = CreateDocument("Исходный текст документа.", "doc1");

        var results = new List<MultimodalChunk>();
        await foreach (var chunk in mmChunker.ChunkAsync(doc))
        {
            results.Add(chunk);
        }

        Assert.True(results.Count > 1, "Expected oversized multimodal chunk to be split");
        foreach (var chunk in results)
        {
            var tokens = _tokenCounter.CountTokens(chunk.Chunk.Text);
            Assert.True(tokens <= 40, $"Multimodal chunk has {tokens} tokens, exceeding ceiling 40");
        }
    }

    [Fact]
    public async Task MultimodalChunker_ImplementsIChunker_YieldsDocumentChunks()
    {
        var inner = new FixedSizeChunker(chunkSize: 50, overlap: 0);
        var mm = new MultimodalChunker(inner, Array.Empty<ExtractedImage>(), Array.Empty<ExtractedTable>());

        IChunker chunker = mm;
        Assert.Equal("multimodal(fixed-size)", chunker.Strategy);

        var doc = CreateDocument("Текст для проверки интерфейса IChunker.", "doc1");

        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in chunker.ChunkAsync(doc))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
    }
}
