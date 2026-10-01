using Rag.Core.Documents;
using Xunit;

namespace Rag.Core.Tests;

public class ChunkMetadataTests
{
    [Fact]
    public void Empty_HasDefaultValues()
    {
        // Act
        var empty = ChunkMetadata.Empty;

        // Assert
        Assert.Null(empty.PageNumber);
        Assert.Null(empty.Section);
        Assert.Null(empty.TokenCount);
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Act
        var metadata = new ChunkMetadata(
            pageNumber: 10,
            section: "Introduction",
            tokenCount: 200);

        // Assert
        Assert.Equal(10, metadata.PageNumber);
        Assert.Equal("Introduction", metadata.Section);
        Assert.Equal(200, metadata.TokenCount);
    }

    [Fact]
    public void Constructor_AllOptionalParams_DefaultToNull()
    {
        // Act
        var metadata = new ChunkMetadata();

        // Assert
        Assert.Null(metadata.PageNumber);
        Assert.Null(metadata.Section);
        Assert.Null(metadata.TokenCount);
    }
}
