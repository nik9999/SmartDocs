using System.Linq;
using Rag.Application.Evaluation;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Application.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="RetrievalEvaluator"/>.
/// </summary>
public sealed class RetrievalEvaluatorTests
{
    private static RetrievalResult CreateResult(Guid documentId, Guid? chunkId = null)
    {
        return new RetrievalResult(
            documentId,
            chunkId ?? Guid.NewGuid(),
            "test text",
            1.0,
            1,
            RetrievalSource.Hybrid);
    }

    private static GoldenQuery CreateGolden(IReadOnlySet<Guid> expectedDocIds, string? referenceAnswer = null)
    {
        return GoldenQuery.Create("test query", expectedDocIds, referenceAnswer);
    }

    /// <summary>
    /// Helper to create evaluation samples from arrays.
    /// </summary>
    private static IEnumerable<(GoldenQuery Gold, IReadOnlyList<RetrievalResult> Hits)> CreateSamples(
        params (GoldenQuery Gold, IReadOnlyList<RetrievalResult> Hits)[] items)
    {
        return items;
    }

    // =========================================================================
    // Test 1 — all relevant
    // =========================================================================

    [Fact]
    public void Evaluate_AllRelevant_ReturnsExpectedMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1, d2 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d1),
            CreateResult(d2),
            CreateResult(d3),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 3);

        // Assert
        Assert.Equal(1.0, metrics.RecallAtK);
        Assert.Equal(2.0 / 3.0, metrics.PrecisionAtK, 5);
        Assert.Equal(1.0, metrics.Mrr);
        Assert.True(metrics.NdcgAtK > 0);
        Assert.Equal(3, metrics.K);
        Assert.Equal(1, metrics.QueryCount);
    }

    // =========================================================================
    // Test 2 — first relevant is rank 2
    // =========================================================================

    [Fact]
    public void Evaluate_FirstRelevantIsRank2_ReturnsExpectedMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d3 = Guid.NewGuid();
        var d4 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d3),
            CreateResult(d1),
            CreateResult(d4),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 3);

        // Assert
        Assert.Equal(1.0, metrics.RecallAtK);
        Assert.Equal(0.5, metrics.Mrr, 5);
    }

    // =========================================================================
    // Test 3 — no relevant results
    // =========================================================================

    [Fact]
    public void Evaluate_NoRelevantResults_ReturnsZeroMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();
        var d4 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d2),
            CreateResult(d3),
            CreateResult(d4),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 3);

        // Assert
        Assert.Equal(0.0, metrics.RecallAtK);
        Assert.Equal(0.0, metrics.PrecisionAtK);
        Assert.Equal(0.0, metrics.Mrr);
        Assert.Equal(0.0, metrics.NdcgAtK);
    }

    // =========================================================================
    // Test 4 — duplicate chunks of same document
    // =========================================================================

    [Fact]
    public void Evaluate_DuplicateChunks_DoesNotCountDocumentTwice()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1, d2 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d1, new Guid("11111111-1111-1111-1111-111111111111")),
            CreateResult(d1, new Guid("22222222-2222-2222-2222-222222222222")),
            CreateResult(d2, new Guid("33333333-3333-3333-3333-333333333333")),
            CreateResult(d3, new Guid("44444444-4444-4444-4444-444444444444")),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 3);

        // Assert
        // D1 appears once (at position 1), D2 appears once (at position 3)
        // Recall: 2 relevant out of 2 expected = 1.0
        Assert.Equal(1.0, metrics.RecallAtK);

        // Precision: 2 relevant out of K=3 = 2/3
        Assert.Equal(2.0 / 3.0, metrics.PrecisionAtK, 5);

        // MRR: first relevant is D1 at position 1 => 1/1 = 1.0
        Assert.Equal(1.0, metrics.Mrr);

        // nDCG > 0 because we have relevant docs
        Assert.True(metrics.NdcgAtK > 0);
    }

    // =========================================================================
    // Test 5 — empty samples
    // =========================================================================

    [Fact]
    public void Evaluate_EmptySamples_ReturnsZeroMetrics()
    {
        // Act
        var metrics = RetrievalEvaluator.Evaluate(Enumerable.Empty<(GoldenQuery, IReadOnlyList<RetrievalResult>)>(), k: 5);

        // Assert
        Assert.Equal(0.0, metrics.RecallAtK);
        Assert.Equal(0.0, metrics.PrecisionAtK);
        Assert.Equal(0.0, metrics.Mrr);
        Assert.Equal(0.0, metrics.NdcgAtK);
        Assert.Equal(5, metrics.K);
        Assert.Equal(0, metrics.QueryCount);
    }

    // =========================================================================
    // Test 6 — invalid K
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Evaluate_InvalidK_ThrowsArgumentOutOfRangeException(int k)
    {
        // Arrange
        var gold = CreateGolden(new HashSet<Guid> { Guid.NewGuid() });
        var hits = new List<RetrievalResult>();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetrievalEvaluator.Evaluate(CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)), k));
    }

    // =========================================================================
    // Test 7 — K smaller than hits
    // =========================================================================

    [Fact]
    public void Evaluate_KSmallerThanHits_ExcludesResultsAfterK()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d2),
            CreateResult(d3),
            CreateResult(d1), // D1 is at position 3, but K=2
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 2);

        // Assert
        // D1 is not in top-2, so recall = 0
        Assert.Equal(0.0, metrics.RecallAtK);
        Assert.Equal(0.0, metrics.PrecisionAtK);
        Assert.Equal(0.0, metrics.Mrr);
        Assert.Equal(0.0, metrics.NdcgAtK);
    }

    // =========================================================================
    // Additional test — multiple queries averaging
    // =========================================================================

    [Fact]
    public void Evaluate_MultipleQueries_AveragesMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();
        var d4 = Guid.NewGuid();

        var gold1 = CreateGolden(new HashSet<Guid> { d1 });
        var gold2 = CreateGolden(new HashSet<Guid> { d2 });

        var hits1 = new List<RetrievalResult>
        {
            CreateResult(d1),
            CreateResult(d3),
        };

        var hits2 = new List<RetrievalResult>
        {
            CreateResult(d4),
            CreateResult(d2),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples(
                (gold1, (IReadOnlyList<RetrievalResult>)hits1),
                (gold2, (IReadOnlyList<RetrievalResult>)hits2)),
            k: 2);

        // Assert
        // Query 1: recall=1, precision=0.5, mrr=1, ndcg>0
        // Query 2: recall=1, precision=0.5, mrr=0.5, ndcg>0
        // Average recall = 1.0
        Assert.Equal(1.0, metrics.RecallAtK);
        Assert.Equal(0.5, metrics.PrecisionAtK);
        // Average MRR = (1 + 0.5) / 2 = 0.75
        Assert.Equal(0.75, metrics.Mrr, 5);
        Assert.True(metrics.NdcgAtK > 0);
        Assert.Equal(2, metrics.QueryCount);
    }

    // =========================================================================
    // Additional test — nDCG with partial hit
    // =========================================================================

    [Fact]
    public void Evaluate_nDCG_PartialHit_ReturnsCorrectValue()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1, d2 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d1), // relevant, rank 1
            CreateResult(d3), // not relevant, rank 2
            // d2 is at position 3, but K=2 so it's excluded
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 2);

        // Assert
        // DCG = 1/log2(2) + 0/log2(3) = 1.0
        // IDCG = 1/log2(2) + 1/log2(3) = 1.0 + 0.6309... = 1.6309...
        // nDCG = 1.0 / 1.6309... ≈ 0.6131
        var expectedNdcg = 1.0 / (1.0 + 1.0 / Math.Log2(3));
        Assert.Equal(expectedNdcg, metrics.NdcgAtK, 5);
    }

    // =========================================================================
    // Additional test — nDCG with no hit
    // =========================================================================

    [Fact]
    public void Evaluate_nDCG_NoRelevantInTopK_ReturnsZero()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var d3 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1, d2 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d3),
            CreateResult(Guid.NewGuid()),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 2);

        // Assert
        Assert.Equal(0.0, metrics.NdcgAtK);
    }

    // =========================================================================
    // Additional test — GoldenQuery validation
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GoldenQuery_Create_EmptyQuery_ThrowsArgumentException(string query)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => GoldenQuery.Create(query, new HashSet<Guid>()));
    }

    [Fact]
    public void GoldenQuery_Create_NullQuery_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => GoldenQuery.Create(null!, new HashSet<Guid>()));
    }

    [Fact]
    public void GoldenQuery_Create_NullExpectedDocIds_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => GoldenQuery.Create("query", (IReadOnlySet<Guid>)null!));
    }

    // =========================================================================
    // Additional test — empty ExpectedDocumentIds
    // =========================================================================

    [Fact]
    public void Evaluate_EmptyExpectedDocIds_ReturnsZeroMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid>());
        var hits = new List<RetrievalResult>
        {
            CreateResult(d1),
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 3);

        // Assert
        Assert.Equal(0.0, metrics.RecallAtK);
        Assert.Equal(0.0, metrics.PrecisionAtK);
        Assert.Equal(0.0, metrics.Mrr);
        Assert.Equal(0.0, metrics.NdcgAtK);
    }

    // =========================================================================
    // Additional test — hits shorter than K
    // =========================================================================

    [Fact]
    public void Evaluate_HitsShorterThanK_ComputesCorrectMetrics()
    {
        // Arrange
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();

        var gold = CreateGolden(new HashSet<Guid> { d1, d2 });
        var hits = new List<RetrievalResult>
        {
            CreateResult(d1),
            CreateResult(d2),
            // Only 2 hits, but K=5
        };

        // Act
        var metrics = RetrievalEvaluator.Evaluate(
            CreateSamples((gold, (IReadOnlyList<RetrievalResult>)hits)),
            k: 5);

        // Assert
        // Recall: 2/2 = 1.0
        Assert.Equal(1.0, metrics.RecallAtK);
        // Precision: 2/5 = 0.4 (penalized for fewer than K results)
        Assert.Equal(0.4, metrics.PrecisionAtK);
        // MRR: first relevant at position 1 => 1.0
        Assert.Equal(1.0, metrics.Mrr);
    }

    // =========================================================================
    // Additional test — RetrievalMetrics.ToString
    // =========================================================================

    [Fact]
    public void RetrievalMetrics_ToString_ReturnsFormattedString()
    {
        // Arrange
        var metrics = new RetrievalMetrics(0.5, 0.6, 0.7, 0.8, 10, 5);

        // Act
        var str = metrics.ToString();

        // Assert
        Assert.Contains("K=10", str);
        Assert.Contains("Queries=5", str);
        Assert.Contains("Recall@K=", str);
        Assert.Contains("Precision@K=", str);
        Assert.Contains("MRR=", str);
        Assert.Contains("nDCG@K=", str);
    }

    // =========================================================================
    // Test — FormatBaseline
    // =========================================================================

    [Fact]
    public void RetrievalMetrics_FormatBaseline_ReturnsHumanReadableReport()
    {
        // Arrange
        var metrics = new RetrievalMetrics(0.8, 0.6, 0.75, 0.7, 5, 15);

        // Act
        var report = metrics.FormatBaseline();

        // Assert
        Assert.Contains("Retrieval Evaluation", report);
        Assert.Contains("Queries: 15", report);
        Assert.Contains("K: 5", report);
        Assert.Contains("Recall@5:", report);
        Assert.Contains("Precision@5:", report);
        Assert.Contains("MRR:", report);
        Assert.Contains("nDCG@5:", report);
    }

    // =========================================================================
    // Test — BaselineGoldenQueries
    // =========================================================================

    [Fact]
    public void BaselineGoldenQueries_GetQueries_ReturnsCorrectCount()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Assert — expanded dataset should have 48 queries
        Assert.Equal(48, queries.Count);
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_HasNoHitQuery()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Assert — there should be at least one query with empty expected document set
        var noHitQuery = queries.FirstOrDefault(q => q.ExpectedDocumentIds.Count == 0);
        Assert.NotNull(noHitQuery);
        Assert.Equal("несуществующий термин абракадабра", noHitQuery!.Query);
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_HasMultiDocumentQuery()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Assert — there should be at least one query referencing multiple documents
        var multiDocQuery = queries.FirstOrDefault(q => q.ExpectedDocumentIds.Count > 1);
        Assert.NotNull(multiDocQuery);
        Assert.Equal("Канал 1 Канал 2 Канал 3", multiDocQuery!.Query);
        Assert.True(multiDocQuery.ExpectedDocumentIds.Count >= 2);
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_AllHaveNonEmptyQuery()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Assert
        foreach (var query in queries)
        {
            Assert.False(string.IsNullOrWhiteSpace(query.Query));
        }
    }

    // =========================================================================
    // Dataset Quality Tests — expanded dataset
    // =========================================================================

    [Fact]
    public void BaselineGoldenQueries_GetQueries_NoDuplicateQueries()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();
        var queryTexts = queries.Select(q => q.Query).ToList();

        // Assert
        var duplicates = queryTexts.GroupBy(q => q)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_HasNegativeQueries()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();
        var negativeQueries = queries.Where(q => q.ExpectedDocumentIds.Count == 0).ToList();

        // Assert — should have multiple negative queries
        Assert.True(negativeQueries.Count >= 3,
            $"Expected at least 3 negative queries, got {negativeQueries.Count}");
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_HasHardNegativeQueries()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Hard negatives: queries that could match multiple similar documents
        var hardNegatives = queries.Where(q =>
            q.ExpectedDocumentIds.Count > 1 &&
            q.ExpectedDocumentIds.Count < 5).ToList();

        // Assert — should have some multi-document queries for hard negative testing
        Assert.True(hardNegatives.Count >= 3,
            $"Expected at least 3 hard negative queries, got {hardNegatives.Count}");
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_AllGuidsAreValid()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();
        var allGuids = queries.SelectMany(q => q.ExpectedDocumentIds).Distinct().ToList();

        // Assert — all GUIDs should be valid (no format exceptions)
        foreach (var guid in allGuids)
        {
            Assert.False(guid == Guid.Empty,
                $"Found empty Guid in ExpectedDocumentIds for query: {queries.First(q => q.ExpectedDocumentIds.Contains(guid)).Query}");
        }
    }

    [Fact]
    public void BaselineGoldenQueries_GetQueries_AllHaveReferenceAnswerOrNull()
    {
        // Arrange & Act
        var queries = BaselineGoldenQueries.GetQueries();

        // Assert — all queries should have valid ReferenceAnswer (null is acceptable)
        foreach (var query in queries)
        {
            // ReferenceAnswer is nullable, so this just ensures no exceptions
            var _ = query.ReferenceAnswer;
        }
    }

    [Fact]
    public void GoldenDatasetLoader_LoadAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Act & Assert
        var ex = Assert.Throws<AggregateException>(() =>
            GoldenDatasetLoader.LoadAsync("nonexistent-path.json", CancellationToken.None).Result);
        Assert.IsType<FileNotFoundException>(ex.GetBaseException());
    }
}
