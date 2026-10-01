using Rag.Application.Retrieval;
using Rag.Core.Contracts;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Search;
using Xunit;

namespace Rag.Infrastructure.Tests;

/// <summary>
/// Integration-style unit test for the full retrieval pipeline.
/// Proves that RRF receives more candidates than FinalTopK.
/// </summary>
public class RetrievalPipelineTests
{
    private sealed class FakeSparseRetriever : ISparseRetriever
    {
        public IReadOnlyList<RetrievalResult>? LastResults { get; set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetrievalResult>>(LastResults ?? Array.Empty<RetrievalResult>());
    }

    private sealed class FakeVectorRetriever : IVectorRetriever
    {
        public IReadOnlyList<RetrievalResult>? LastResults { get; set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetrievalResult>>(LastResults ?? Array.Empty<RetrievalResult>());
    }

    private sealed class CountingFusion : IResultFusion
    {
        public int FuseCallCount { get; private set; }
        public int? LastInputCount { get; private set; }
        public int? LastTopK { get; private set; }

        public IReadOnlyList<RetrievalResult> Fuse(
            IReadOnlyList<IReadOnlyList<RetrievalResult>> resultSets, int topK)
        {
            FuseCallCount++;
            LastInputCount = resultSets.Sum(rs => rs?.Count ?? 0);
            LastTopK = topK;
            // Use real RRF for deduplication
            return new ReciprocalRankFusion().Fuse(resultSets, topK);
        }
    }

    private sealed class CountingReranker : IReranker
    {
        public int RerankCallCount { get; private set; }
        public int? LastInputCount { get; private set; }
        public int? LastTopK { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
            string query, IReadOnlyList<RetrievalResult> candidates, int topK, CancellationToken cancellationToken)
        {
            RerankCallCount++;
            LastInputCount = candidates.Count;
            LastTopK = topK;
            // Use real reranker for deterministic ordering
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(
                new BaselineReranker().RerankAsync(query, candidates, topK, cancellationToken).Result);
        }
    }

    [Fact]
    public async Task Pipeline_CandidateTopKExceedsFinalTopK()
    {
        // Arrange
        const int finalTopK = 3;
        const int candidateTopK = 10;

        var sparse = new FakeSparseRetriever
        {
            LastResults = Enumerable.Range(1, candidateTopK)
                .Select(i => new RetrievalResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"Result {i}",
                    1.0 - i * 0.05,
                    i,
                    RetrievalSource.Sparse))
                .ToList()
        };

        var vector = new FakeVectorRetriever
        {
            LastResults = Enumerable.Range(1, candidateTopK)
                .Select(i => new RetrievalResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"Vector {i}",
                    0.9 - i * 0.05,
                    i,
                    RetrievalSource.Dense))
                .ToList()
        };

        var fusion = new CountingFusion();
        var reranker = new CountingReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", finalTopK, candidateTopK);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert
        // 1. Fusion is called once (not per result set)
        Assert.Equal(1, fusion.FuseCallCount);

        // 2. Fusion receives both result sets (20 total candidates)
        Assert.Equal(candidateTopK * 2, fusion.LastInputCount);

        // 3. Reranker receives more candidates than FinalTopK
        Assert.True(reranker.LastInputCount > finalTopK,
            $"Reranker should receive more candidates ({reranker.LastInputCount}) than FinalTopK ({finalTopK})");

        // 4. Reranker narrows to FinalTopK
        Assert.Equal(finalTopK, reranker.LastTopK);

        // 5. Final result count equals FinalTopK
        Assert.Equal(finalTopK, results.Count);
    }

    [Fact]
    public async Task Pipeline_DeterministicWithSameRrfScore_UsesChunkId()
    {
        // Arrange: two chunks with identical RRF scores (same rank in same number of result sets)
        // Both appear rank 1 in sparse and rank 2 in dense
        var chunkA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var chunkB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var sparse = new FakeSparseRetriever
        {
            LastResults = new[]
            {
                new RetrievalResult(Guid.NewGuid(), chunkA, "Text A", 0.9, 1, RetrievalSource.Sparse),
                new RetrievalResult(Guid.NewGuid(), chunkB, "Text B", 0.8, 2, RetrievalSource.Sparse),
            }
        };

        var vector = new FakeVectorRetriever
        {
            LastResults = new[]
            {
                new RetrievalResult(Guid.NewGuid(), chunkB, "Text B", 0.75, 1, RetrievalSource.Dense),
                new RetrievalResult(Guid.NewGuid(), chunkA, "Text A", 0.85, 2, RetrievalSource.Dense),
            }
        };

        var fusion = new CountingFusion();
        var reranker = new CountingReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5, candidateTopK: 10);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert: chunkA < chunkB lexicographically, so chunkA should come first
        Assert.Equal(2, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId);
        Assert.Equal(chunkB, results[1].ChunkId);
    }

    [Fact]
    public async Task Pipeline_EmptyResults_ReturnsEmpty()
    {
        // Arrange
        var sparse = new FakeSparseRetriever { LastResults = Array.Empty<RetrievalResult>() };
        var vector = new FakeVectorRetriever { LastResults = Array.Empty<RetrievalResult>() };
        var fusion = new CountingFusion();
        var reranker = new CountingReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5, candidateTopK: 10);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task Pipeline_OnlySparseResults_ReturnsSparse()
    {
        // Arrange
        var sparse = new FakeSparseRetriever
        {
            LastResults = new[]
            {
                new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), "Sparse only", 0.9, 1, RetrievalSource.Sparse),
            }
        };
        var vector = new FakeVectorRetriever { LastResults = Array.Empty<RetrievalResult>() };
        var fusion = new CountingFusion();
        var reranker = new CountingReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5, candidateTopK: 10);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public async Task Pipeline_OnlyVectorResults_ReturnsVector()
    {
        // Arrange
        var sparse = new FakeSparseRetriever { LastResults = Array.Empty<RetrievalResult>() };
        var vector = new FakeVectorRetriever
        {
            LastResults = new[]
            {
                new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), "Vector only", 0.85, 1, RetrievalSource.Dense),
            }
        };
        var fusion = new CountingFusion();
        var reranker = new CountingReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5, candidateTopK: 10);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Single(results);
    }
}
