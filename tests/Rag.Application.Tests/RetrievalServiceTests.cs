using Rag.Application.Retrieval;
using Rag.Core.Contracts;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Application.Tests;

public class RetrievalServiceTests
{
    private sealed class FakeSparseRetriever : ISparseRetriever
    {
        public int CallCount { get; private set; }
        public RetrievalQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }
    }

    private sealed class FakeVectorRetriever : IVectorRetriever
    {
        public int CallCount { get; private set; }
        public RetrievalQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }
    }

    private sealed class FakeFusion : IResultFusion
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

    private sealed class FakeReranker : IReranker
    {
        public string? LastQuery { get; private set; }
        public int? LastTopK { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
            string query, IReadOnlyList<RetrievalResult> candidates, int topK, CancellationToken cancellationToken)
        {
            LastQuery = query;
            LastTopK = topK;
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
    public async Task SearchAsync_PassesQueryToBothRetrievers()
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

        // Assert
        Assert.Equal("my query", sparse.LastQuery!.Text);
        Assert.Equal(10, sparse.LastQuery.TopK);
        Assert.Equal("my query", vector.LastQuery!.Text);
        Assert.Equal(10, vector.LastQuery.TopK);
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
    public async Task SearchAsync_PassesTopKToFusion()
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

        // Assert
        Assert.Equal(7, fusion.LastTopK);
    }

    [Fact]
    public async Task SearchAsync_PassesFusedResultsToReranker()
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

        // Assert
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
}
