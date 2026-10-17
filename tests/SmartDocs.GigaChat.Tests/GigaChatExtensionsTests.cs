using System;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.GigaChat.Extensions;
using Xunit;

namespace SmartDocs.GigaChat.Tests;

public class GigaChatExtensionsTests
{
    private const string MockAuthKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.fake-token";

    [Fact]
    public void AddGigaChatClient_RegistersClientInServices()
    {
        var services = new ServiceCollection();
        services.AddGigaChatClient(options =>
        {
            options.AuthorizationKey = MockAuthKey;
            options.DefaultModel = GigaChatModels.GigaChatPro;
        });

        using var sp = services.BuildServiceProvider();
        var client = sp.GetService<GigaChatClient>();

        Assert.NotNull(client);
        Assert.Equal(GigaChatModels.GigaChatPro, client.Options.DefaultModel);
    }

    [Fact]
    public void AddGigaChatChatClient_RegistersIChatClientInServices()
    {
        var services = new ServiceCollection();
        services.AddGigaChatChatClient(GigaChatModels.GigaChatMax, options =>
        {
            options.AuthorizationKey = MockAuthKey;
        });

        using var sp = services.BuildServiceProvider();
        var chatClient = sp.GetService<IChatClient>();

        Assert.NotNull(chatClient);
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("gigachat", metadata.ProviderName);
        Assert.Equal(GigaChatModels.GigaChatMax, metadata.DefaultModelId);
    }

    [Fact]
    public void AddGigaChatEmbeddingGenerator_RegistersIEmbeddingGeneratorInServices()
    {
        var services = new ServiceCollection();
        services.AddGigaChatEmbeddingGenerator(GigaChatModels.EmbeddingsGigaR, options =>
        {
            options.AuthorizationKey = MockAuthKey;
        });

        using var sp = services.BuildServiceProvider();
        var embeddingGen = sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.NotNull(embeddingGen);
        var metadata = embeddingGen.GetService<EmbeddingGeneratorMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("gigachat", metadata.ProviderName);
        Assert.Equal(GigaChatModels.EmbeddingsGigaR, metadata.DefaultModelId);
    }

    [Fact]
    public void UseGigaChat_ConfiguresChatClientBuilder()
    {
        using var client = new GigaChatClient(MockAuthKey);

        var builder = new ChatClientBuilder(client.AsChatClient());
        builder.UseGigaChat(client, GigaChatModels.GigaChat2Pro);

        using var chatClient = builder.Build();
        Assert.NotNull(chatClient);
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal(GigaChatModels.GigaChat2Pro, metadata.DefaultModelId);
    }

    [Fact]
    public void DefaultModel_IsGigaChat2Lite()
    {
        var services = new ServiceCollection();
        services.AddGigaChatChatClient(configure: options =>
        {
            options.AuthorizationKey = MockAuthKey;
        });

        using var sp = services.BuildServiceProvider();
        var chatClient = sp.GetService<IChatClient>();

        Assert.NotNull(chatClient);
        var metadata = chatClient.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal(GigaChatModels.GigaChat2, metadata.DefaultModelId);
        Assert.Equal(GigaChatModels.GigaChat2Lite, metadata.DefaultModelId);
        Assert.Equal("GigaChat-2", metadata.DefaultModelId);
    }
}
