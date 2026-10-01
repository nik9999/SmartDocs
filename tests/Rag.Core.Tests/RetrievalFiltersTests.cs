using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Core.Tests;

public class RetrievalFiltersTests
{
    [Fact]
    public void Constructor_WithItems_StoresItems()
    {
        // Arrange
        var items = new Dictionary<string, string> { { "key", "value" } };

        // Act
        var filters = new RetrievalFilters(items);

        // Assert
        Assert.Single(filters.Items);
        Assert.Equal("value", filters.Items["key"]);
    }

    [Fact]
    public void Constructor_WithNullItems_UsesEmptyDictionary()
    {
        // Act
        var filters = new RetrievalFilters(null);

        // Assert
        Assert.NotNull(filters.Items);
        Assert.Empty(filters.Items);
    }
}
