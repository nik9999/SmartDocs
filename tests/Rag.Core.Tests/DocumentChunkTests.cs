using Rag.Core.Documents;
using Xunit;

namespace Rag.Core.Tests;

public class DocumentChunkTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesChunk()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var metadata = new ChunkMetadata(pageNumber: 5);

        // Act
        var chunk = new DocumentChunk(docId, "Some text", 0, metadata);

        // Assert
        Assert.Equal(docId, chunk.DocumentId);
        Assert.Equal("Some text", chunk.Text);
        Assert.Equal(0, chunk.Position);
        Assert.Equal(5, chunk.Metadata.PageNumber);
        Assert.NotEqual(Guid.Empty, chunk.ChunkId);
    }

    [Fact]
    public void Constructor_EmptyText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new DocumentChunk(Guid.NewGuid(), "", 0));
    }

    [Fact]
    public void Constructor_NullText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new DocumentChunk(Guid.NewGuid(), null!, 0));
    }

    [Fact]
    public void Constructor_NegativePosition_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new DocumentChunk(Guid.NewGuid(), "text", -1));
    }

    [Fact]
    public void Constructor_WithNullMetadata_UsesEmptyMetadata()
    {
        // Act
        var chunk = new DocumentChunk(Guid.NewGuid(), "text", 0, null);

        // Assert
        Assert.NotNull(chunk.Metadata);
        Assert.Null(chunk.Metadata.PageNumber);
    }

    [Fact]
    public void Constructor_WithExplicitChunkId_UsesProvidedId()
    {
        // Arrange
        var expectedChunkId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        // Act
        var chunk = new DocumentChunk(expectedChunkId, docId, "text", 0, ChunkMetadata.Empty);

        // Assert
        Assert.Equal(expectedChunkId, chunk.ChunkId);
    }
}
