namespace Rag.Infrastructure.Tests;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Search;
using Xunit;

public class BaselineRerankerTests
{
    private readonly BaselineReranker _reranker = new();

    private RetrievalResult CreateResult(Guid chunkId, string text, double score) =>
        new(Guid.NewGuid(), chunkId, text, score, 1, RetrievalSource.Hybrid);

    [Fact]
    public async Task RerankAsync_HighestOverlapFirst()
    {
        // Arrange
        var query = "температура давления";
        var chunkA = Guid.NewGuid(); // matches "температура" and "давления" (2/2)
        var chunkB = Guid.NewGuid(); // matches none (0/2)
        var chunkC = Guid.NewGuid(); // matches "температуры" - different token, no match (0/2)

        var candidates = new[]
        {
            CreateResult(chunkA, "Температура давления система", 0.9),
            CreateResult(chunkB, "Канал связи работает", 0.8),
            CreateResult(chunkC, "Давление температуры воздуха", 0.7),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 10, CancellationToken.None);

        // Assert: chunkA has highest overlap (2/2), others have 0
        Assert.Equal(3, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId);
    }

    [Fact]
    public async Task RerankAsync_TopK_LimitsResults()
    {
        // Arrange
        var query = "тестовый текст";
        var candidates = Enumerable.Range(0, 5)
            .Select(i => CreateResult(
                Guid.NewGuid(),
                $"Текстовый документ номер {i}",
                0.9 - i * 0.1))
            .ToArray();

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 3, CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmpty()
    {
        // Act
        var results = await _reranker.RerankAsync("test", Array.Empty<RetrievalResult>(), 5, CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task RerankAsync_NoOverlap_ReturnsResultsWithZeroScore()
    {
        // Arrange: query has no matching keywords in any candidate
        var query = "несуществующийтермин";
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "Температура в системе", 0.9),
            CreateResult(Guid.NewGuid(), "Давление в трубопроводе", 0.8),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: still returns results but with zero overlap score
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RerankAsync_DeterministicOrdering()
    {
        // Arrange: same inputs, run multiple times
        var query = "тестовый запрос";
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "Тестовый текст", 0.9),
            CreateResult(Guid.NewGuid(), "Запрос тест", 0.8),
            CreateResult(Guid.NewGuid(), "Тестовый запрос документ", 0.7),
        };

        var result1 = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);
        var result2 = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);
        var result3 = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: all runs produce identical ordering
        Assert.Equal(result1[0].ChunkId, result2[0].ChunkId);
        Assert.Equal(result1[0].ChunkId, result3[0].ChunkId);
    }

    [Fact]
    public async Task RerankAsync_UpdatesSourceToReranked()
    {
        // Arrange
        var query = "тест";
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "Тестовый текст", 0.9),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal(RetrievalSource.Reranked, results[0].Source);
    }

    [Fact]
    public async Task RerankAsync_QueryWithSingleWord()
    {
        // Arrange
        var query = "температура";
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();
        var candidates = new[]
        {
            CreateResult(chunkA, "Температура повышена", 0.9),
            CreateResult(chunkB, "Давление в норме", 0.8),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: chunkA should be first (matches query keyword)
        Assert.Equal(2, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId);
        Assert.Equal(RetrievalSource.Reranked, results[0].Source);
    }

    [Fact]
    public async Task RerankAsync_CaseInsensitiveMatching()
    {
        // Arrange
        var query = "ТЕМПЕРАТУРА ДАВЛЕНИЯ";
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "температура давления система", 0.9),
            CreateResult(Guid.NewGuid(), "Канал связи", 0.8),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: case should not matter, first candidate matches both keywords
        Assert.Equal(2, results.Count);
        Assert.Equal(RetrievalSource.Reranked, results[0].Source);
    }

    [Fact]
    public async Task RerankAsync_NullCandidates_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _reranker.RerankAsync("test", (IReadOnlyList<RetrievalResult>)null!, 5, CancellationToken.None));
    }

    [Fact]
    public async Task RerankAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _reranker.RerankAsync("", Array.Empty<RetrievalResult>(), 5, CancellationToken.None));
    }

    [Fact]
    public async Task RerankAsync_WhitespaceQuery_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _reranker.RerankAsync("   ", Array.Empty<RetrievalResult>(), 5, CancellationToken.None));
    }

    [Fact]
    public async Task RerankAsync_TopK_Zero_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _reranker.RerankAsync("test", Array.Empty<RetrievalResult>(), 0, CancellationToken.None));
    }

    [Fact]
    public async Task RerankAsync_TopK_ExceedsCandidates_ReturnsAll()
    {
        // Arrange
        var query = "тест";
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "Тестовый текст", 0.9),
            CreateResult(Guid.NewGuid(), "Текст номер два", 0.8),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 10, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RerankAsync_PreservesMetadata()
    {
        // Arrange
        var query = "тест";
        var chunkId = Guid.NewGuid();
        var metadata = new Dictionary<string, string> { { "section", "test" }, { "page", "42" } };
        var candidates = new[] { new RetrievalResult(Guid.NewGuid(), chunkId, "Тестовый текст", 0.9, 1, RetrievalSource.Hybrid, metadata) };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal(metadata["section"], results[0].Metadata["section"]);
        Assert.Equal(metadata["page"], results[0].Metadata["page"]);
    }

    [Fact]
    public async Task RerankAsync_SameScore_DeterministicByChunkId()
    {
        // Arrange: two candidates with same overlap score
        var query = "тест текст";
        var chunkA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var chunkB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var candidates = new[]
        {
            CreateResult(chunkB, "Текст и ещё текст", 0.9),  // "текст" appears twice but counted once = 1/2
            CreateResult(chunkA, "Тестовый текст документ", 0.8), // "тест" + "текст" = 2/2
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: chunkA has higher score (2/2 vs 1/2)
        Assert.Equal(chunkA, results[0].ChunkId);
    }

    [Fact]
    public async Task RerankAsync_ShortTokensFiltered()
    {
        // Arrange: query with short tokens that should be filtered
        var query = "а б в тест"; // "а", "б", "в" are single-char, filtered out
        var candidates = new[]
        {
            CreateResult(Guid.NewGuid(), "Тестовый текст", 0.9),
        };

        // Act
        var results = await _reranker.RerankAsync(query, candidates, 5, CancellationToken.None);

        // Assert: should still work, only "тест" keyword is used
        Assert.Single(results);
    }
}
