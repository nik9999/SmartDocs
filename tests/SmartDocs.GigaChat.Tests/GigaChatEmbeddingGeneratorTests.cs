using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using SmartDocs.GigaChat.Extensions;
using SmartDocs.GigaChat.Tests.TestHelpers;
using Xunit;

namespace SmartDocs.GigaChat.Tests;

public class GigaChatEmbeddingGeneratorTests
{
    private const string MockJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.fake-token";

    [Fact]
    public async Task GenerateAsync_ReturnsEmbeddingsVectors()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://gigachat.devices.sberbank.ru/api/v1/embeddings", req.RequestUri?.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal(MockJwt, req.Headers.Authorization?.Parameter);

            var json = """
            {
              "data": [
                {
                  "index": 0,
                  "embedding": [0.123, -0.456, 0.789],
                  "object": "embedding"
                },
                {
                  "index": 1,
                  "embedding": [0.321, -0.654, 0.987],
                  "object": "embedding"
                }
              ],
              "model": "Embeddings",
              "usage": {
                "prompt_tokens": 12,
                "total_tokens": 12
              }
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var options = new GigaChatClientOptions
        {
            AuthorizationKey = MockJwt,
            HttpClient = httpClient
        };

        using var client = new GigaChatClient(options);
        var embeddingGenerator = client.AsEmbeddingGenerator();

        var result = await embeddingGenerator.GenerateAsync(["Привет", "Мир"]);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Vector.Length);
        Assert.Equal(0.123f, result[0].Vector.Span[0], precision: 3);
        Assert.Equal(-0.456f, result[0].Vector.Span[1], precision: 3);
        Assert.Equal(0.789f, result[0].Vector.Span[2], precision: 3);
        Assert.Equal(12, result.Usage?.TotalTokenCount);
    }
}
