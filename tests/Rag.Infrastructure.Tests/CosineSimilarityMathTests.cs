using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Infrastructure.Tests;

/// <summary>
/// Unit tests for cosine similarity mathematics.
/// </summary>
public class CosineSimilarityMathTests
{
    /// <summary>
    /// Computes cosine similarity between two float arrays.
    /// Mirrors the implementation in SqliteVectorRetriever.
    /// </summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0;

        double dot = 0;
        double normASquared = 0;
        double normBSquared = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * (double)b[i];
            normASquared += (double)a[i] * (double)a[i];
            normBSquared += (double)b[i] * (double)b[i];
        }

        if (normASquared == 0 || normBSquared == 0)
            return 0;

        return dot / (Math.Sqrt(normASquared) * Math.Sqrt(normBSquared));
    }

    [Fact]
    public void IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 1, 0, 0 };
        var sim = CosineSimilarity(a, b);
        Assert.True(Math.Abs(sim - 1.0) < 0.0001, $"Expected 1.0, got {sim}");
    }

    [Fact]
    public void OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { 0, 1 };
        var sim = CosineSimilarity(a, b);
        Assert.True(Math.Abs(sim) < 0.0001, $"Expected 0, got {sim}");
    }

    [Fact]
    public void OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1, 0 };
        var b = new float[] { -1, 0 };
        var sim = CosineSimilarity(a, b);
        Assert.True(Math.Abs(sim - (-1.0)) < 0.0001, $"Expected -1, got {sim}");
    }

    [Fact]
    public void ZeroVector_ReturnsZero()
    {
        var a = new float[] { 0, 0, 0 };
        var b = new float[] { 1, 0, 0 };
        var sim = CosineSimilarity(a, b);
        Assert.Equal(0, sim);
        Assert.False(double.IsNaN(sim));
        Assert.False(double.IsInfinity(sim));
    }

    [Fact]
    public void BothZeroVectors_ReturnsZero()
    {
        var a = new float[] { 0, 0 };
        var b = new float[] { 0, 0 };
        var sim = CosineSimilarity(a, b);
        Assert.Equal(0, sim);
        Assert.False(double.IsNaN(sim));
        Assert.False(double.IsInfinity(sim));
    }

    [Fact]
    public void NormalizedVectors_DotEqualsCosine()
    {
        var a = new float[] { 0.6f, 0.8f };
        var b = new float[] { 0.8f, 0.6f };
        var sim = CosineSimilarity(a, b);
        // Dot product of normalized vectors
        var dot = (double)a[0] * b[0] + (double)a[1] * b[1];
        Assert.True(Math.Abs(sim - dot) < 0.0001, $"Cosine {sim} should equal dot {dot}");
    }

    [Fact]
    public void DifferentLengths_ReturnsZero()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 1, 0 };
        var sim = CosineSimilarity(a, b);
        Assert.Equal(0, sim);
    }
}
