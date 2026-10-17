using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// Cohere Rerank API (<c>rerank-v3.5</c>, the single multilingual model in the
/// v3.5 line and the 2026 default in the book). Sends the query + candidate
/// texts to Cohere's v2 Rerank endpoint and replaces the score with the API's
/// relevance_score. Requires <c>COHERE_API_KEY</c> in the environment OR an
/// explicit constructor argument.
/// </summary>
public sealed class CohereReranker : IReranker
{
    private static readonly Uri RerankEndpoint = new("https://api.cohere.com/v2/rerank");
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public CohereReranker(HttpClient http, string apiKey, string model = "rerank-v3.5")
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _http = http;
        _apiKey = apiKey;
        _model = model;
    }

    public string Implementation => $"cohere-{_model}";

    public async Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        if (candidates.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var request = new RerankRequest(
            Model: _model,
            Query: query,
            Documents: [.. candidates.Select(c => c.Chunk.Text)],
            TopN: Math.Min(topK, candidates.Count));

        using var msg = new HttpRequestMessage(HttpMethod.Post, RerankEndpoint)
        {
            Content = JsonContent.Create(request),
        };
        msg.Headers.Add("Authorization", $"Bearer {_apiKey}");

        using var response = await _http.SendAsync(msg, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cohere returned empty rerank response");

        return [.. body.Results
            .OrderByDescending(r => r.RelevanceScore)
            .Select(r => new RetrievalResult(candidates[r.Index].Chunk, r.RelevanceScore))];
    }

    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents,
        [property: JsonPropertyName("top_n")] int TopN,
        // Cohere's per-document token budget (API default 4096). Left null by
        // default and omitted from the wire payload so the API applies its own
        // default; only serialized when explicitly set.
        [property: JsonPropertyName("max_tokens_per_doc")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int? MaxTokensPerDoc = null);

    private sealed record RerankResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<RerankResult> Results);

    private sealed record RerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}
