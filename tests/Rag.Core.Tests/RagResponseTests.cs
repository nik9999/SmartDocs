using Rag.Core.Generation;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class RagResponseTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesResponse()
    {
        // Arrange
        var citations = new List<Citation>();

        // Act
        var response = new RagResponse("answer", citations);

        // Assert
        Assert.Equal("answer", response.Answer);
        Assert.Same(citations, response.Citations);
        Assert.Null(response.Query);
    }

    [Fact]
    public void Constructor_EmptyAnswer_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RagResponse("", Array.Empty<Citation>()));
    }

    [Fact]
    public void Constructor_NullAnswer_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RagResponse(null!, Array.Empty<Citation>()));
    }

    [Fact]
    public void Constructor_NullCitations_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RagResponse("answer", null!));
    }

    [Fact]
    public void Constructor_WithQuery_StoresQuery()
    {
        // Arrange
        var citation = new Citation(1, Guid.NewGuid(), Guid.NewGuid(), "text", "source");

        // Act
        var response = new RagResponse("answer", new[] { citation }, "original query");

        // Assert
        Assert.Equal("answer", response.Answer);
        Assert.Single(response.Citations);
        Assert.Equal("original query", response.Query);
    }
}
