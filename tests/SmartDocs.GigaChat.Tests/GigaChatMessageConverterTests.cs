using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartDocs.GigaChat.Extensions;
using SmartDocs.GigaChat.Model;
using Xunit;

namespace SmartDocs.GigaChat.Tests;

public class GigaChatMessageConverterTests
{
    [Fact]
    public void ToGigaChatMessages_ConvertsRolesAndTextCorrectly()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Ты полезный ассистент."),
            new(ChatRole.User, "Привет, GigaChat!"),
            new(ChatRole.Assistant, "Здравствуйте! Чем могу помочь?")
        };

        var dtos = GigaChatMessageConverter.ToGigaChatMessages(messages);

        Assert.Equal(3, dtos.Count);
        Assert.Equal("system", dtos[0].Role);
        Assert.Equal("Ты полезный ассистент.", dtos[0].Content);
        Assert.Equal("user", dtos[1].Role);
        Assert.Equal("Привет, GigaChat!", dtos[1].Content);
        Assert.Equal("assistant", dtos[2].Role);
        Assert.Equal("Здравствуйте! Чем могу помочь?", dtos[2].Content);
    }

    [Fact]
    public void ToGigaChatMessages_ConvertsToolResultToFunctionRole()
    {
        var toolResult = new FunctionResultContent("call-123", "{\"temp\": 22}");
        var message = new ChatMessage(ChatRole.Tool, [toolResult]);

        var dtos = GigaChatMessageConverter.ToGigaChatMessages([message]);

        Assert.Single(dtos);
        Assert.Equal("function", dtos[0].Role);
        Assert.Equal("call-123", dtos[0].Name);
        Assert.Equal("{\"temp\": 22}", dtos[0].Content);
    }

    [Fact]
    public void ToGigaChatMessages_ConvertsFunctionCallContent()
    {
        var args = new Dictionary<string, object?> { ["city"] = "Moscow" };
        var funcCall = new FunctionCallContent("call-1", "get_weather", args);
        var message = new ChatMessage(ChatRole.Assistant, [funcCall]);

        var dtos = GigaChatMessageConverter.ToGigaChatMessages([message]);

        Assert.Single(dtos);
        Assert.Equal("assistant", dtos[0].Role);
        Assert.NotNull(dtos[0].FunctionCall);
        Assert.Equal("get_weather", dtos[0].FunctionCall!.Name);
        Assert.True(dtos[0].FunctionCall!.Arguments.TryGetProperty("city", out var cityProp));
        Assert.Equal("Moscow", cityProp.GetString());
    }

    [Fact]
    public void ToChatResponse_MapsResponseAndUsageCorrectly()
    {
        var responseDto = new GigaChatCompletionsResponse
        {
            Model = "GigaChat-Pro",
            Created = 1710000000,
            Choices =
            [
                new GigaChatChoiceDto
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new GigaChatMessageDto
                    {
                        Role = "assistant",
                        Content = "Ответ от GigaChat."
                    }
                }
            ],
            Usage = new GigaChatUsageDto
            {
                PromptTokens = 15,
                CompletionTokens = 8,
                TotalTokens = 23
            }
        };

        var chatResponse = GigaChatMessageConverter.ToChatResponse(responseDto);

        Assert.Equal("GigaChat-Pro", chatResponse.ModelId);
        Assert.Equal(ChatFinishReason.Stop, chatResponse.FinishReason);
        Assert.NotNull(chatResponse.Usage);
        Assert.Equal(15, chatResponse.Usage.InputTokenCount);
        Assert.Equal(8, chatResponse.Usage.OutputTokenCount);
        Assert.Equal(23, chatResponse.Usage.TotalTokenCount);
        Assert.Equal("Ответ от GigaChat.", chatResponse.Text);
    }

    [Theory]
    [InlineData("stop", "stop")]
    [InlineData("length", "length")]
    [InlineData("function_call", "tool_calls")]
    [InlineData("blacklist", "content_filter")]
    public void ToChatFinishReason_MapsStandardReasons(string gigaReason, string expectedReason)
    {
        var result = GigaChatMessageConverter.ToChatFinishReason(gigaReason);
        Assert.NotNull(result);
        Assert.Equal(expectedReason, result.Value.Value);
    }
}
