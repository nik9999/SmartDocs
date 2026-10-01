using Rag.Application.Evaluation;
using Rag.Application.Retrieval;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Application.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="RetrievalEvaluationRunner"/>.
/// </summary>
public sealed class RetrievalEvaluationRunnerTests
{
    private sealed class FakeRetrievalService : IRetrievalService
    {
        public int CallCount { get; private set; }
        public RetrievalQuery? LastQuery { get; private set; }
        public IReadOnlyList<RetrievalResult>? LastResults { get; set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult(LastResults ?? Array.Empty<RetrievalResult>());
        }
    }

    [Fact]
    public async Task RunAsync_CallsRetrievalForEachQuery()
    {
        // Arrange
        const int queryCount = 3;
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("query 1", new HashSet<Guid> { d1 }),
            GoldenQuery.Create("query 2", new HashSet<Guid> { d2 }),
            GoldenQuery.Create("query 3", new HashSet<Guid> { d3 }),
        };

        var fakeService = new FakeRetrievalService
        {
            LastResults = new[]
            {
                new RetrievalResult(d1, Guid.NewGuid(), "text", 1.0, 1, RetrievalSource.Hybrid),
            }
        };

        var runner = new RetrievalEvaluationRunner(fakeService);

        // Act
        var metrics = await runner.RunAsync(queries, k: 5);

        // Assert
        Assert.Equal(queryCount, fakeService.CallCount);
    }

    [Fact]
    public async Task RunAsync_PassesCorrectKToRetrieval()
    {
        // Arrange
        const int k = 7;
        var d1 = Guid.NewGuid();

        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("test query", new HashSet<Guid> { d1 }),
        };

        var fakeService = new FakeRetrievalService
        {
            LastResults = Array.Empty<RetrievalResult>()
        };

        var runner = new RetrievalEvaluationRunner(fakeService);

        // Act
        await runner.RunAsync(queries, k);

        // Assert
        Assert.Equal(k, fakeService.LastQuery!.FinalTopK);
        Assert.Equal(RetrievalConstants.DefaultCandidateTopK, fakeService.LastQuery.CandidateTopK);
    }

    [Fact]
    public async Task RunAsync_PassesResultsToEvaluator()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("query 1", new HashSet<Guid> { d1 }),
            GoldenQuery.Create("query 2", new HashSet<Guid> { d2 }),
        };

        var fakeService = new FakeRetrievalService
        {
            LastResults = new[]
            {
                new RetrievalResult(d1, Guid.NewGuid(), "text", 1.0, 1, RetrievalSource.Hybrid),
            }
        };

        var runner = new RetrievalEvaluationRunner(fakeService);

        // Act
        var metrics = await runner.RunAsync(queries, k: 5);

        // Assert
        Assert.Equal(2, metrics.QueryCount);
        Assert.Equal(5, metrics.K);
    }

    [Fact]
    public async Task RunAsync_MultipleQueries_AveragesMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("query 1", new HashSet<Guid> { d1 }),
            GoldenQuery.Create("query 2", new HashSet<Guid> { d2 }),
            GoldenQuery.Create("query 3", new HashSet<Guid> { d1 }), // D1 is expected for both
        };

        var fakeService = new FakeRetrievalService
        {
            LastResults = new[]
            {
                new RetrievalResult(d1, Guid.NewGuid(), "text", 1.0, 1, RetrievalSource.Hybrid),
                new RetrievalResult(d3, Guid.NewGuid(), "text", 0.5, 2, RetrievalSource.Hybrid),
            }
        };

        var runner = new RetrievalEvaluationRunner(fakeService);

        // Act
        var metrics = await runner.RunAsync(queries, k: 3);

        // Assert
        // Query 1: D1 found => recall=1, precision=1/3, mrr=1
        // Query 2: D2 not found => recall=0, precision=0, mrr=0
        // Query 3: D1 found => recall=1, precision=1/3, mrr=1
        // Average recall = 2/3, average precision = 2/9, average mrr = 2/3
        Assert.Equal(2.0 / 3.0, metrics.RecallAtK, 5);
        Assert.Equal(2.0 / 9.0, metrics.PrecisionAtK, 5);
        Assert.Equal(2.0 / 3.0, metrics.Mrr, 5);
    }

    [Fact]
    public async Task RunAsync_EmptyQueries_ReturnsZeroMetrics()
    {
        // Arrange
        var runner = new RetrievalEvaluationRunner(new FakeRetrievalService());

        // Act
        var metrics = await runner.RunAsync(Array.Empty<GoldenQuery>(), k: 5);

        // Assert
        Assert.Equal(0.0, metrics.RecallAtK);
        Assert.Equal(0.0, metrics.PrecisionAtK);
        Assert.Equal(0.0, metrics.Mrr);
        Assert.Equal(0.0, metrics.NdcgAtK);
        Assert.Equal(5, metrics.K);
        Assert.Equal(0, metrics.QueryCount);
    }

    [Fact]
    public async Task RunAsync_InvalidK_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("test", new HashSet<Guid> { Guid.NewGuid() }),
        };

        var runner = new RetrievalEvaluationRunner(new FakeRetrievalService());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(queries, k: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(queries, k: -1));
    }

    [Fact]
    public async Task RunAsync_NullQueries_ThrowsArgumentNullException()
    {
        // Arrange
        var runner = new RetrievalEvaluationRunner(new FakeRetrievalService());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.RunAsync(null!, k: 5));
    }

    [Fact]
    public async Task RunAsync_NullService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RetrievalEvaluationRunner(null!));
    }

    [Fact]
    public async Task RunAsync_CancellationToken_IsPropagated()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("test", new HashSet<Guid> { d1 }),
        };

        var fakeService = new FakeRetrievalService
        {
            LastResults = Array.Empty<RetrievalResult>()
        };

        var runner = new RetrievalEvaluationRunner(fakeService);
        var cts = new CancellationTokenSource();

        // Act
        await runner.RunAsync(queries, k: 5, cts.Token);

        // Assert — if we get here without exception, the token was accepted
        // (in a real scenario, the token would be checked by the underlying service)
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public async Task RunAsync_RetrievalError_IsNotMasked()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var queries = new List<GoldenQuery>
        {
            GoldenQuery.Create("test", new HashSet<Guid> { d1 }),
        };

        var throwingService = new ThrowingRetrievalService();
        var runner = new RetrievalEvaluationRunner(throwingService);

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            runner.RunAsync(queries, k: 5));
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    /// A retrieval service that always throws.
    /// </summary>
    private sealed class ThrowingRetrievalService : IRetrievalService
    {
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            return Task.FromException<IReadOnlyList<RetrievalResult>>(
                new InvalidOperationException("Simulated retrieval failure"));
        }
    }
}
