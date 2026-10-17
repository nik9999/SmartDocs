using System;
using System.Collections.Generic;
using System.IO;
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

public class GigaChatChatClientTests
{
    private const string MockJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.fake-token";

    [Fact]
    public async Task GetResponseAsync_ReturnsSuccessfulChatResponse()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://gigachat.devices.sberbank.ru/api/v1/chat/completions", req.RequestUri?.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal(MockJwt, req.Headers.Authorization?.Parameter);

            var json = """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Привет! Я модель GigaChat от Сбера."
                  },
                  "index": 0,
                  "finish_reason": "stop"
                }
              ],
              "created": 1710000000,
              "model": "GigaChat",
              "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 8,
                "total_tokens": 18
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
        var chatClient = client.AsChatClient();

        var response = await chatClient.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "Привет!")
        ]);

        Assert.NotNull(response);
        Assert.Equal("Привет! Я модель GigaChat от Сбера.", response.Text);
        Assert.Equal("GigaChat", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(18, response.Usage?.TotalTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ParsesServerSentEvents()
    {
        var ssePayload = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"Привет\"},\"index\":0,\"finish_reason\":null}],\"created\":1710000000,\"model\":\"GigaChat\"}\n\n" +
                         "data: {\"choices\":[{\"delta\":{\"content\":\", мир!\"},\"index\":0,\"finish_reason\":\"stop\"}],\"created\":1710000001,\"model\":\"GigaChat\"}\n\n" +
                         "data: [DONE]\n\n";

        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
        });

        using var httpClient = new HttpClient(handler);
        var options = new GigaChatClientOptions
        {
            AuthorizationKey = MockJwt,
            HttpClient = httpClient
        };

        using var client = new GigaChatClient(options);
        var chatClient = client.AsChatClient();

        var chunks = new List<ChatResponseUpdate>();
        await foreach (var chunk in chatClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Привет!")]))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(2, chunks.Count);
        var fullText = string.Join("", chunks.Select(c => c.Text));
        Assert.Equal("Привет, мир!", fullText);
        Assert.Equal(ChatFinishReason.Stop, chunks[1].FinishReason);
    }
}
