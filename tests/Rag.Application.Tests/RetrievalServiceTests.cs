using Rag.Application.Retrieval;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Application.Tests;

public class RetrievalServiceTests
{
    private sealed class FakeSparseRetriever : Core.Contracts.ISparseRetriever
    {
        public int CallCount { get; private set; }
        public Core.Retrieval.RetrievalQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            Core.Retrieval.RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }
    }

    private sealed class FakeVectorRetriever : Core.Contracts.IVectorRetriever
    {
        public int CallCount { get; private set; }
        public Core.Retrieval.RetrievalQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            Core.Retrieval.RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }
    }

    private sealed class FakeFusion : Core.Contracts.IResultFusion
    {
        public IReadOnlyList<IReadOnlyList<RetrievalResult>>? LastResultSets { get; private set; }
        public int? LastTopK { get; private set; }

        public IReadOnlyList<RetrievalResult> Fuse(
            IReadOnlyList<IReadOnlyList<RetrievalResult>> resultSets, int topK)
        {
            LastResultSets = resultSets;
            LastTopK = topK;
            return Array.Empty<RetrievalResult>();
        }
    }

    private sealed class FakeReranker : Core.Contracts.IReranker
    {
        public string? LastQuery { get; private set; }
        public int? LastTopK { get; private set; }
        public IReadOnlyList<RetrievalResult>? LastCandidates { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
            string query, IReadOnlyList<RetrievalResult> candidates, int topK, CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastTopK = topK;
            LastCandidates = candidates;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(candidates);
        }
    }

    [Fact]
    public async Task SearchAsync_CallsSparseAndVectorRetrievers()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(1, sparse.CallCount);
        Assert.Equal(1, vector.CallCount);
    }

    [Fact]
    public async Task SearchAsync_PassesCandidateTopKToBothRetrievers()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("my query", 10);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert — retrievers receive CandidateTopK (default 50), not FinalTopK (10)
        Assert.Equal("my query", sparse.LastQuery!.Text);
        Assert.Equal(RetrievalConstants.DefaultCandidateTopK, sparse.LastQuery.CandidateTopK);
        Assert.Equal(10, sparse.LastQuery.FinalTopK);
        Assert.Equal("my query", vector.LastQuery!.Text);
        Assert.Equal(RetrievalConstants.DefaultCandidateTopK, vector.LastQuery.CandidateTopK);
        Assert.Equal(10, vector.LastQuery.FinalTopK);
    }

    [Fact]
    public async Task SearchAsync_PassesResultsToFusion()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(fusion.LastResultSets);
        Assert.Equal(2, fusion.LastResultSets.Count);
    }

    [Fact]
    public async Task SearchAsync_PassesCandidateTopKToFusion()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 7);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert — fusion receives CandidateTopK (default 50)
        Assert.Equal(RetrievalConstants.DefaultCandidateTopK, fusion.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_PassesFinalTopKToReranker()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("rerank me", 3);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert — reranker receives FinalTopK (3)
        Assert.Equal("rerank me", reranker.LastQuery);
        Assert.Equal(3, reranker.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_ReturnsRerankedResults()
    {
        // Arrange
        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", 5);

        // Act
        var results = await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
    }

    [Fact]
    public async Task SearchAsync_Pipeline_CandidateTopKExceedsFinalTopK()
    {
        // Arrange — verify the full pipeline: retrievers get CandidateTopK,
        // fusion returns CandidateTopK, reranker narrows to FinalTopK
        const int finalTopK = 3;
        const int expectedCandidateTopK = 50;

        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", finalTopK);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert
        // 1. Retriever queries have correct CandidateTopK and FinalTopK
        Assert.Equal(expectedCandidateTopK, sparse.LastQuery!.CandidateTopK);
        Assert.Equal(expectedCandidateTopK, vector.LastQuery!.CandidateTopK);
        Assert.Equal(finalTopK, sparse.LastQuery.FinalTopK);
        Assert.Equal(finalTopK, vector.LastQuery.FinalTopK);

        // 2. Fusion receives CandidateTopK
        Assert.Equal(expectedCandidateTopK, fusion.LastTopK);

        // 3. Reranker receives FinalTopK
        Assert.Equal(finalTopK, reranker.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_CustomCandidateTopK_IsPassedCorrectly()
    {
        // Arrange
        const int finalTopK = 5;
        const int candidateTopK = 100;

        var sparse = new FakeSparseRetriever();
        var vector = new FakeVectorRetriever();
        var fusion = new FakeFusion();
        var reranker = new FakeReranker();
        var service = new RetrievalService(sparse, vector, fusion, reranker);
        var query = new RetrievalQuery("test", finalTopK, candidateTopK);

        // Act
        await service.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(candidateTopK, sparse.LastQuery!.CandidateTopK);
        Assert.Equal(candidateTopK, vector.LastQuery!.CandidateTopK);
        Assert.Equal(finalTopK, sparse.LastQuery.FinalTopK);
        Assert.Equal(candidateTopK, fusion.LastTopK);
        Assert.Equal(finalTopK, reranker.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_CandidateTopK_MustBeGreaterThanOrEqualToFinalTopK()
    {
        // Arrange
        var service = new RetrievalService(
            new FakeSparseRetriever(),
            new FakeVectorRetriever(),
            new FakeFusion(),
            new FakeReranker());

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.SearchAsync(new RetrievalQuery("test", 10, 5), CancellationToken.None));
        Assert.Contains("CandidateTopK", exception.Message);
    }
}
