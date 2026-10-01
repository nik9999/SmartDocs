using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class RetrievalQueryTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesQuery()
    {
        // Act
        var query = new RetrievalQuery("test query", 10);

        // Assert
        Assert.Equal("test query", query.Text);
        Assert.Equal(10, query.TopK);
        Assert.NotNull(query.Filters);
    }

    [Fact]
    public void Constructor_TopKZero_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RetrievalQuery("query", 0));
    }

    [Fact]
    public void Constructor_TopKNegative_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RetrievalQuery("query", -5));
    }

    [Fact]
    public void Constructor_EmptyText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RetrievalQuery("", 10));
    }

    [Fact]
    public void Constructor_NullText_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new RetrievalQuery(null!, 10));
    }

    [Fact]
    public void Constructor_WithFilters_StoresFilters()
    {
        // Arrange
        var filters = new RetrievalFilters(new Dictionary<string, string> { { "key", "value" } });

        // Act
        var query = new RetrievalQuery("query", 5, filters);

        // Assert
        Assert.Single(query.Filters.Items);
        Assert.Equal("value", query.Filters.Items["key"]);
    }

    [Fact]
    public void Constructor_WithNullFilters_UsesDefault()
    {
        // Act
        var query = new RetrievalQuery("query", 5, null);

        // Assert
        Assert.NotNull(query.Filters);
        Assert.Empty(query.Filters.Items);
    }
}
