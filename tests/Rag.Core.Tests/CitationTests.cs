using Rag.Core.Generation;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class CitationTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesCitation()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        // Act
        var citation = new Citation(1, docId, chunkId, "text", "source");

        // Assert
        Assert.Equal(1, citation.CitationId);
        Assert.Equal(docId, citation.DocumentId);
        Assert.Equal(chunkId, citation.ChunkId);
        Assert.Equal("text", citation.Text);
        Assert.Equal("source", citation.Source);
    }

    [Fact]
    public void Constructor_ZeroCitationId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(0, Guid.NewGuid(), Guid.NewGuid(), "text", "source"));
    }

    [Fact]
    public void Constructor_NegativeCitationId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(-1, Guid.NewGuid(), Guid.NewGuid(), "text", "source"));
    }

    [Fact]
    public void Constructor_EmptyDocumentId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(1, Guid.Empty, Guid.NewGuid(), "text", "source"));
    }

    [Fact]
    public void Constructor_EmptyChunkId_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(1, Guid.NewGuid(), Guid.Empty, "text", "source"));
    }

    [Fact]
    public void Constructor_EmptyText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(1, Guid.NewGuid(), Guid.NewGuid(), "", "source"));
    }

    [Fact]
    public void Constructor_EmptySource_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Citation(1, Guid.NewGuid(), Guid.NewGuid(), "text", ""));
    }
}
