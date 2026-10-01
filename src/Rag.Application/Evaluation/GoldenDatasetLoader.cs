using System.Text.Json;
using System.Text.Json.Serialization;
using Rag.Application.Evaluation;

namespace Rag.Application.Evaluation;

/// <summary>
/// Loads a golden dataset from a JSON file.
/// </summary>
/// <remarks>
/// <para>
/// The expected JSON format is an array of objects with the following properties:
/// <code>
/// [
///   {
///     "query": "search query text",
///     "expectedDocumentIds": ["guid1", "guid2"]
///   }
/// ]
/// </code>
/// </para>
/// <para>
/// The <c>referenceAnswer</c> property is optional and not used by retrieval metrics.
/// </para>
/// </remarks>
public static class GoldenDatasetLoader
{
    /// <summary>
    /// Loads a golden dataset from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON file containing golden queries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of <see cref="GoldenQuery"/> instances.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the file at <paramref name="filePath"/> does not exist.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is malformed or does not match the expected structure.
    /// </exception>
    public static async Task<IReadOnlyList<GoldenQuery>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Golden dataset file not found.", filePath);

        var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

        return ParseJson(json);
    }

    /// <summary>
    /// Parses a golden dataset from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string containing golden queries.</param>
    /// <returns>A list of <see cref="GoldenQuery"/> instances.</returns>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is malformed or does not match the expected structure.
    /// </exception>
    public static IReadOnlyList<GoldenQuery> ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Golden dataset JSON cannot be empty.");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new GuidJsonConverter() }
        };

        var documents = JsonSerializer.Deserialize<GoldenDatasetDocument[]>(json, options);

        if (documents == null)
            throw new JsonException("Golden dataset JSON is null or invalid.");

        var queries = new List<GoldenQuery>(documents.Length);

        for (var i = 0; i < documents.Length; i++)
        {
            var doc = documents[i];

            if (string.IsNullOrWhiteSpace(doc.Query))
                throw new JsonException(
                    $"Golden dataset document at index {i} has empty or null 'query' field.");

            if (doc.ExpectedDocumentIds == null || doc.ExpectedDocumentIds.Count == 0)
            {
                // Empty expectedDocumentIds is allowed (tests no-hit scenarios)
                queries.Add(GoldenQuery.Create(doc.Query, new HashSet<Guid>(), doc.ReferenceAnswer));
            }
            else
            {
                var docIds = new HashSet<Guid>(doc.ExpectedDocumentIds);
                queries.Add(GoldenQuery.Create(doc.Query, docIds, doc.ReferenceAnswer));
            }
        }

        return queries;
    }

    /// <summary>
    /// Internal DTO for JSON deserialization.
    /// </summary>
    private sealed class GoldenDatasetDocument
    {
        public string? Query { get; init; }
        public IReadOnlyList<Guid>? ExpectedDocumentIds { get; init; }
        public string? ReferenceAnswer { get; init; }
    }

    /// <summary>
    /// Custom JSON converter for Guid that handles both string and raw GUID formats.
    /// </summary>
    private sealed class GuidJsonConverter : JsonConverter<Guid>
    {
        public override Guid Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetGuid();
        }

        public override void Write(
            Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
