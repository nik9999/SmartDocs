namespace Rag.Infrastructure.Tests;

using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Application.Retrieval;
using Rag.Core.Contracts;
using Rag.Core.Retrieval;
using Rag.Infrastructure.DependencyInjection;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Search;
using Xunit;

public class RetrievalPipelineDiTests
{
    [Fact]
    public void IResultFusion_ResolvesFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();

        // Act
        var sp = services.BuildServiceProvider();
        var fusion = sp.GetRequiredService<IResultFusion>();

        // Assert
        Assert.NotNull(fusion);
        Assert.IsType<ReciprocalRankFusion>(fusion);
    }

    [Fact]
    public void IReranker_ResolvesFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();

        // Act
        var sp = services.BuildServiceProvider();
        var reranker = sp.GetRequiredService<IReranker>();

        // Assert
        Assert.NotNull(reranker);
        Assert.IsType<BaselineReranker>(reranker);
    }

    [Fact]
    public void RetrievalService_CanBeCreatedViaDI()
    {
        // Arrange: build a minimal DI container with all required dependencies
        var services = new ServiceCollection();

        // Infrastructure components
        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();

        // Fake retrievers for DI resolution test
        services.AddScoped<ISparseRetriever, FakeSparseRetriever>();
        services.AddScoped<IVectorRetriever, FakeVectorRetriever>();

        // Application services
        services.AddScoped<IRetrievalService, RetrievalService>();

        // Act
        var sp = services.BuildServiceProvider();

        // Assert: RetrievalService should resolve without exception
        var retrievalService = sp.GetRequiredService<IRetrievalService>();
        Assert.NotNull(retrievalService);
        Assert.IsType<RetrievalService>(retrievalService);
    }

    [Fact]
    public void RetrievalService_FullPipeline_ThroughDI()
    {
        // Arrange: build DI with all components
        var services = new ServiceCollection();

        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();
        services.AddScoped<ISparseRetriever, FakeSparseRetriever>();
        services.AddScoped<IVectorRetriever, FakeVectorRetriever>();

        // Register Application services
        services.AddScoped<IRetrievalService, RetrievalService>();

        // Act
        var sp = services.BuildServiceProvider();

        // Assert: full pipeline resolves
        var retrievalService = sp.GetRequiredService<IRetrievalService>();
        Assert.NotNull(retrievalService);

        var fusion = sp.GetRequiredService<IResultFusion>();
        Assert.NotNull(fusion);

        var reranker = sp.GetRequiredService<IReranker>();
        Assert.NotNull(reranker);
    }

    [Fact]
    public async Task RetrievalService_SearchAsync_ReturnsResults()
    {
        // Arrange
        var services = new ServiceCollection();

        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();
        services.AddScoped<ISparseRetriever, FakeSparseRetrieverWithResults>();
        services.AddScoped<IVectorRetriever, FakeVectorRetrieverWithResults>();

        services.AddScoped<IRetrievalService, RetrievalService>();

        var sp = services.BuildServiceProvider();
        var retrievalService = sp.GetRequiredService<IRetrievalService>();
        var query = new RetrievalQuery("test query", 5);

        // Act
        var results = await retrievalService.SearchAsync(query, CancellationToken.None);

        // Assert
        Assert.NotNull(results);
    }

    private sealed class FakeSparseRetriever : ISparseRetriever
    {
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
    }

    private sealed class FakeVectorRetriever : IVectorRetriever
    {
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
    }

    private sealed class FakeSparseRetrieverWithResults : ISparseRetriever
    {
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            var chunkId = Guid.NewGuid();
            var result = new RetrievalResult(
                Guid.NewGuid(), chunkId, "Sparse result", 0.9, 1, RetrievalSource.Sparse);
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(new[] { result });
        }
    }

    private sealed class FakeVectorRetrieverWithResults : IVectorRetriever
    {
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            var chunkId = Guid.NewGuid();
            var result = new RetrievalResult(
                Guid.NewGuid(), chunkId, "Vector result", 0.85, 1, RetrievalSource.Dense);
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(new[] { result });
        }
    }
}
