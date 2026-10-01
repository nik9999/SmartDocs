using Rag.Core.Embeddings;
using Xunit;

namespace Rag.Core.Tests;

public class EmbeddingTests
{
    [Fact]
    public void Constructor_WithValidVector_CreatesEmbedding()
    {
        // Arrange
        var vector = new[] { 0.1f, 0.2f, 0.3f };

        // Act
        var embedding = new Embedding(vector, "test-model");

        // Assert
        Assert.NotSame(vector, embedding.Vector);
        Assert.Equal(3, embedding.Vector.Length);
        Assert.Equal("test-model", embedding.Model);
        Assert.Equal(3, embedding.Dimensions);
    }

    [Fact]
    public void Constructor_DimensionsMatchVectorLength()
    {
        // Arrange
        var vector = new float[42];

        // Act
        var embedding = new Embedding(vector, "model");

        // Assert
        Assert.Equal(42, embedding.Dimensions);
        Assert.Equal(42, embedding.Vector.Length);
    }

    [Fact]
    public void Constructor_ClonesInputVector_PreventsExternalMutation()
    {
        // Arrange
        var original = new[] { 1.0f, 2.0f, 3.0f };

        // Act
        var embedding = new Embedding(original, "model");

        // Mutate the original array
        original[0] = 999.0f;

        // Assert — embedding should be unaffected
        Assert.Equal(1.0f, embedding.Vector[0]);
        Assert.Equal(2.0f, embedding.Vector[1]);
        Assert.Equal(3.0f, embedding.Vector[2]);
    }

    [Fact]
    public void Constructor_EmptyVector_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Embedding(Array.Empty<float>(), "model"));
    }

    [Fact]
    public void Constructor_NullVector_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Embedding(null!, "model"));
    }

    [Fact]
    public void Constructor_EmptyModel_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Embedding(new[] { 0.1f }, ""));
    }

    [Fact]
    public void Constructor_NullModel_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Embedding(new[] { 0.1f }, null!));
    }
}
