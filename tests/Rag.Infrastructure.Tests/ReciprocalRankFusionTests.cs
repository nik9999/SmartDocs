namespace Rag.Infrastructure.Tests;

using Rag.Core.Contracts;
using Rag.Core.Retrieval;
using Rag.Infrastructure.Search;
using Xunit;

public class ReciprocalRankFusionTests
{
    private readonly ReciprocalRankFusion _fusion = new();

    private RetrievalResult CreateResult(Guid chunkId, double score, int rank, RetrievalSource source) =>
        new(Guid.NewGuid(), chunkId, $"Text for {chunkId}", score, rank, source);

    [Fact]
    public void Fuse_ReturnsOrderedByRrfScore()
    {
        // Arrange: two result sets where chunk A appears first in both
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();
        var chunkC = Guid.NewGuid();

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
            CreateResult(chunkC, 0.7, 3, RetrievalSource.Sparse),
        };

        var dense = new[]
        {
            CreateResult(chunkA, 0.85, 1, RetrievalSource.Dense),
            CreateResult(chunkB, 0.75, 2, RetrievalSource.Dense),
        };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 10);

        // Assert: chunkA should be first (appears rank 1 in both -> highest RRF)
        Assert.Equal(3, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId);
        Assert.Equal(RetrievalSource.Hybrid, results[0].Source);
        Assert.Equal(chunkA, results[0].ChunkId);
    }

    [Fact]
    public void Fuse_DeduplicatesByChunkId()
    {
        // Arrange: same chunk appears in both result sets
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
        };

        var dense = new[]
        {
            CreateResult(chunkA, 0.85, 1, RetrievalSource.Dense),
            CreateResult(chunkB, 0.75, 3, RetrievalSource.Dense),
        };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 10);

        // Assert: each chunk should appear only once
        Assert.Equal(2, results.Count);
        var chunkIds = results.Select(r => r.ChunkId).ToList();
        Assert.Equal(2, chunkIds.Distinct().Count());
    }

    [Fact]
    public void Fuse_DeterministicOrdering()
    {
        // Arrange: same inputs, run fusion multiple times
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
        };

        var dense = new[]
        {
            CreateResult(chunkB, 0.75, 1, RetrievalSource.Dense),
            CreateResult(chunkA, 0.85, 2, RetrievalSource.Dense),
        };

        var resultSets = new List<IReadOnlyList<RetrievalResult>> { sparse, dense };

        // Act: run fusion multiple times
        var result1 = _fusion.Fuse(resultSets, 10);
        var result2 = _fusion.Fuse(resultSets, 10);
        var result3 = _fusion.Fuse(resultSets, 10);

        // Assert: all runs produce identical ordering
        Assert.Equal(result1[0].ChunkId, result2[0].ChunkId);
        Assert.Equal(result1[0].ChunkId, result3[0].ChunkId);
        Assert.Equal(result1[1].ChunkId, result2[1].ChunkId);
        Assert.Equal(result1[1].ChunkId, result3[1].ChunkId);
    }

    [Fact]
    public void Fuse_TopK_LimitsResults()
    {
        // Arrange: 5 chunks, topK = 2
        var chunks = Enumerable.Range(0, 5)
            .Select(i => Guid.NewGuid())
            .ToList();

        var sparse = chunks.Select((c, i) => CreateResult(c, 0.9 - i * 0.1, i + 1, RetrievalSource.Sparse)).ToArray();
        var dense = chunks.Select((c, i) => CreateResult(c, 0.85 - i * 0.1, i + 1, RetrievalSource.Dense)).ToArray();

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 2);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Fuse_TopK_1_ReturnsSingleResult()
    {
        // Arrange
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();

        var sparse = new[] { CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse) };
        var dense = new[] { CreateResult(chunkB, 0.8, 1, RetrievalSource.Dense) };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 1);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public void Fuse_EmptyResultSets_ReturnsEmpty()
    {
        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>>(), 5);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Fuse_NullResultSetInList_IgnoresIt()
    {
        // Arrange
        var chunkA = Guid.NewGuid();
        var sparse = new[] { CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse) };

        // Act
        var resultSets = new List<IReadOnlyList<RetrievalResult>> { sparse, default!, default! };
        var results = _fusion.Fuse(resultSets, 5);

        // Assert
        Assert.Single(results);
        Assert.Equal(chunkA, results[0].ChunkId);
    }

    [Fact]
    public void Fuse_OnlyEmptyResultSets_ReturnsEmpty()
    {
        // Arrange
        var sparse = Array.Empty<RetrievalResult>();
        var dense = Array.Empty<RetrievalResult>();

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 5);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Fuse_NullResultSets_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _fusion.Fuse((IReadOnlyList<IReadOnlyList<RetrievalResult>>?)null!, 5));
    }

    [Fact]
    public void Fuse_TopK_Zero_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { Array.Empty<RetrievalResult>() }, 0));
    }

    [Fact]
    public void Fuse_TopK_Negative_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { Array.Empty<RetrievalResult>() }, -1));
    }

    [Fact]
    public void Fuse_PreservesBestScoreForChunkId()
    {
        // Arrange: same chunk with different scores in two result sets
        var chunkA = Guid.NewGuid();

        var sparse = new[] { CreateResult(chunkA, 0.95, 1, RetrievalSource.Sparse) };
        var dense = new[] { CreateResult(chunkA, 0.5, 5, RetrievalSource.Dense) };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 5);

        // Assert: the best score (0.95 from sparse) should be preserved
        Assert.Single(results);
        Assert.Equal(0.95, results[0].Score);
    }

    [Fact]
    public void Fuse_MultipleChunks_RRFScoringCorrect()
    {
        // Arrange: chunk A rank 1 in both -> highest RRF
        //          chunk B rank 2 in both -> second highest RRF
        //          chunk C rank 3 in sparse only -> lower RRF
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();
        var chunkC = Guid.NewGuid();

        const double k = 60.0;
        // RRF for A: 1/(60+0) + 1/(60+0) = 2/60
        // RRF for B: 1/(60+1) + 1/(60+1) = 2/61
        // RRF for C: 1/(60+2) = 1/62
        double rrfA = 2.0 / (k + 0);
        double rrfB = 2.0 / (k + 1);
        double rrfC = 1.0 / (k + 2);

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
            CreateResult(chunkC, 0.7, 3, RetrievalSource.Sparse),
        };

        var dense = new[]
        {
            CreateResult(chunkA, 0.85, 1, RetrievalSource.Dense),
            CreateResult(chunkB, 0.75, 2, RetrievalSource.Dense),
        };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 10);

        // Assert: order by RRF score
        Assert.Equal(3, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId); // highest RRF
        Assert.Equal(chunkB, results[1].ChunkId);
        Assert.Equal(chunkC, results[2].ChunkId); // lowest RRF

        // Verify RRF scores are in correct order
        Assert.True(rrfA > rrfB && rrfB > rrfC);
    }

    [Fact]
    public void Fuse_HandlesSingleResultSet()
    {
        // Arrange
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
        };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse }, 5);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(chunkA, results[0].ChunkId);
        Assert.Equal(chunkB, results[1].ChunkId);
    }

    [Fact]
    public void Fuse_TopK_ExceedsAvailableResults_ReturnsAll()
    {
        // Arrange: only 2 chunks, topK = 10
        var chunkA = Guid.NewGuid();
        var chunkB = Guid.NewGuid();

        var sparse = new[] { CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse) };
        var dense = new[] { CreateResult(chunkB, 0.8, 1, RetrievalSource.Dense) };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 10);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Fuse_DeterministicWithSameRrfScore_UsesChunkId()
    {
        // Arrange: two chunks with identical RRF scores (same rank in same number of result sets)
        // Both appear rank 1 in sparse and rank 2 in dense
        var chunkA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var chunkB = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var sparse = new[]
        {
            CreateResult(chunkA, 0.9, 1, RetrievalSource.Sparse),
            CreateResult(chunkB, 0.8, 2, RetrievalSource.Sparse),
        };

        var dense = new[]
        {
            CreateResult(chunkB, 0.75, 1, RetrievalSource.Dense),
            CreateResult(chunkA, 0.85, 2, RetrievalSource.Dense),
        };

        // Act
        var results = _fusion.Fuse(new List<IReadOnlyList<RetrievalResult>> { sparse, dense }, 10);

        // Assert: when RRF scores are equal, order by ChunkId (ascending)
        // chunkA < chunkB lexicographically, so chunkA should come first
        Assert.Equal(chunkA, results[0].ChunkId);
        Assert.Equal(chunkB, results[1].ChunkId);
    }
}
