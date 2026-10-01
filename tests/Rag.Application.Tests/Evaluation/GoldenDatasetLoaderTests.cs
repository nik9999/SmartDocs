using System.Text.Json;
using Rag.Application.Evaluation;
using Xunit;

namespace Rag.Application.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="GoldenDatasetLoader"/>.
/// </summary>
public sealed class GoldenDatasetLoaderTests
{
    [Fact]
    public void ParseJson_ValidJson_ReturnsGoldenQueries()
    {
        // Arrange
        var json = @"[{""query"":""test query"",""expectedDocumentIds"":[""11111111-1111-1111-1111-111111111111""]}]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Single(queries);
        Assert.Equal("test query", queries[0].Query);
        Assert.Single(queries[0].ExpectedDocumentIds);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), queries[0].ExpectedDocumentIds.First());
    }

    [Fact]
    public void ParseJson_MultipleQueries_ReturnsAllQueries()
    {
        // Arrange
        var json = @"[
            {""query"":""query 1"",""expectedDocumentIds"":[""11111111-1111-1111-1111-111111111111""]},
            {""query"":""query 2"",""expectedDocumentIds"":[""22222222-2222-2222-2222-222222222222""]},
            {""query"":""query 3"",""expectedDocumentIds"":[""33333333-3333-3333-3333-333333333333""]}
        ]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Equal(3, queries.Count);
        Assert.Equal("query 1", queries[0].Query);
        Assert.Equal("query 2", queries[1].Query);
        Assert.Equal("query 3", queries[2].Query);
    }

    [Fact]
    public void ParseJson_EmptyExpectedDocumentIds_ReturnsEmptySet()
    {
        // Arrange
        var json = @"[{""query"":""no expected"",""expectedDocumentIds"":[]}]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Single(queries);
        Assert.Empty(queries[0].ExpectedDocumentIds);
    }

    [Fact]
    public void ParseJson_MultipleExpectedDocumentIds_ReturnsAll()
    {
        // Arrange
        var json = @"[{
            ""query"":""multi expected"",
            ""expectedDocumentIds"":[""11111111-1111-1111-1111-111111111111"",""22222222-2222-2222-2222-222222222222""]
        }]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Single(queries);
        Assert.Equal(2, queries[0].ExpectedDocumentIds.Count);
    }

    [Fact]
    public void ParseJson_EmptyJsonString_ThrowsJsonException()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => GoldenDatasetLoader.ParseJson(""));
        Assert.Throws<JsonException>(() => GoldenDatasetLoader.ParseJson("   "));
    }

    [Fact]
    public void ParseJson_NullJson_ThrowsJsonException()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => GoldenDatasetLoader.ParseJson(null!));
    }

    [Fact]
    public void ParseJson_MalformedJson_ThrowsJsonException()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => GoldenDatasetLoader.ParseJson("{invalid json"));
    }

    [Fact]
    public void ParseJson_MissingQueryField_ThrowsJsonException()
    {
        // Arrange
        var json = @"[{""expectedDocumentIds"":[""11111111-1111-1111-1111-111111111111""]}]";

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() => GoldenDatasetLoader.ParseJson(json));
        Assert.Contains("query", exception.Message.ToLower());
    }

    [Fact]
    public void ParseJson_EmptyArray_ReturnsEmptyList()
    {
        // Arrange
        var json = @"[]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Empty(queries);
    }

    [Fact]
    public void ParseJson_WithReferenceAnswer_PreservesAnswer()
    {
        // Arrange
        var json = @"[{
            ""query"":""test"",
            ""expectedDocumentIds"":[""11111111-1111-1111-1111-111111111111""],
            ""referenceAnswer"":""This is a reference answer""
        }]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Single(queries);
        Assert.Equal("This is a reference answer", queries[0].ReferenceAnswer);
    }

    [Fact]
    public void ParseJson_WithoutReferenceAnswer_SetsNull()
    {
        // Arrange
        var json = @"[{""query"":""test"",""expectedDocumentIds"":[]}]";

        // Act
        var queries = GoldenDatasetLoader.ParseJson(json);

        // Assert
        Assert.Single(queries);
        Assert.Null(queries[0].ReferenceAnswer);
    }
}
