using Rag.Core.Generation;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class RagContextTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesContext()
    {
        // Arrange
        var results = new List<RetrievalResult>();
        var citations = new List<Citation>();

        // Act
        var context = new RagContext("query", results, citations);

        // Assert
        Assert.Equal("query", context.Query);
        Assert.Same(results, context.Results);
        Assert.Same(citations, context.Citations);
    }

    [Fact]
    public void Constructor_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RagContext("", Array.Empty<RetrievalResult>(), Array.Empty<Citation>()));
    }

    [Fact]
    public void Constructor_NullQuery_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RagContext(null!, Array.Empty<RetrievalResult>(), Array.Empty<Citation>()));
    }

    [Fact]
    public void Constructor_NullResults_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RagContext("query", null!, Array.Empty<Citation>()));
    }

    [Fact]
    public void Constructor_NullCitations_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RagContext("query", Array.Empty<RetrievalResult>(), null!));
    }

    [Fact]
    public void Constructor_WithResultsAndCitations_StoresThem()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var result = new RetrievalResult(docId, chunkId, "text", 0.9, 1, RetrievalSource.Sparse);
        var citation = new Citation(1, docId, chunkId, "text", "source");

        // Act
        var context = new RagContext("query", new[] { result }, new[] { citation });

        // Assert
        Assert.Single(context.Results);
        Assert.Single(context.Citations);
    }
}
