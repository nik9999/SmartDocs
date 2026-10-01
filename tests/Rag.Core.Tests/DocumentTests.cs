using Rag.Core.Documents;
using Xunit;

namespace Rag.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void Constructor_WithValidArguments_CreatesDocument()
    {
        // Arrange
        var metadata = new Dictionary<string, string> { { "key", "value" } };

        // Act
        var document = new Document("Test", "Content", "source", metadata);

        // Assert
        Assert.Equal("Test", document.Title);
        Assert.Equal("Content", document.Content);
        Assert.Equal("source", document.Source);
        Assert.Single(document.Metadata);
        Assert.NotEqual(Guid.Empty, document.DocumentId);
    }

    [Fact]
    public void Constructor_EmptyTitle_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Document("", "Content", "source"));
    }

    [Fact]
    public void Constructor_NullTitle_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Document(null!, "Content", "source"));
    }

    [Fact]
    public void Constructor_EmptyContent_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Document("Title", "", "source"));
    }

    [Fact]
    public void Constructor_EmptySource_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Document("Title", "Content", ""));
    }

    [Fact]
    public void Constructor_NullSource_ThrowsArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new Document("Title", "Content", null!));
    }

    [Fact]
    public void Constructor_WithNullMetadata_UsesEmptyDictionary()
    {
        // Act
        var document = new Document("Title", "Content", "source", null);

        // Assert
        Assert.NotNull(document.Metadata);
        Assert.Empty(document.Metadata);
    }

    [Fact]
    public void Constructor_WithExplicitDocumentId_UsesProvidedId()
    {
        // Arrange
        var expectedId = Guid.NewGuid();

        // Act
        var document = new Document(expectedId, "Title", "Content", "source", new Dictionary<string, string>());

        // Assert
        Assert.Equal(expectedId, document.DocumentId);
    }
}
