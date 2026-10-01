using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class RetrievalResultTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesResult()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        // Act
        var result = new RetrievalResult(
            docId, chunkId, "text", 0.95, 1, RetrievalSource.Sparse);

        // Assert
        Assert.Equal(docId, result.DocumentId);
        Assert.Equal(chunkId, result.ChunkId);
        Assert.Equal("text", result.Text);
        Assert.Equal(0.95, result.Score);
        Assert.Equal(1, result.Rank);
        Assert.Equal(RetrievalSource.Sparse, result.Source);
    }

    [Fact]
    public void Constructor_EmptyDocumentId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.Empty, Guid.NewGuid(), "text", 0.5, 1, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_EmptyChunkId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.NewGuid(), Guid.Empty, "text", 0.5, 1, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_EmptyText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), "", 0.5, 1, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_NullText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), null!, 0.5, 1, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_ZeroRank_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), "text", 0.5, 0, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_NegativeRank_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RetrievalResult(Guid.NewGuid(), Guid.NewGuid(), "text", 0.5, -1, RetrievalSource.Sparse));
    }

    [Fact]
    public void Constructor_WithNullMetadata_UsesEmptyDictionary()
    {
        // Act
        var result = new RetrievalResult(
            Guid.NewGuid(), Guid.NewGuid(), "text", 0.5, 1, RetrievalSource.Sparse, null);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Empty(result.Metadata);
    }

    [Theory]
    [InlineData(RetrievalSource.Sparse)]
    [InlineData(RetrievalSource.Dense)]
    [InlineData(RetrievalSource.Hybrid)]
    [InlineData(RetrievalSource.Reranked)]
    public void Constructor_AllSources_Accepted(RetrievalSource source)
    {
        // Act
        var result = new RetrievalResult(
            Guid.NewGuid(), Guid.NewGuid(), "text", 0.5, 1, source);

        // Assert
        Assert.Equal(source, result.Source);
    }
}
