using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.Embeddings;
using SmartDocs.Core.Tokens;
using SmartDocs.GigaChat;
using SmartDocs.GigaChat.Extensions;

namespace SmartDocs.Core.DependencyInjection;

/// <summary>
/// DI registration extensions for SmartDocs.Core. The seed of the
/// <c>services.AddSmartDocsRagPipeline()</c> NuGet pattern that ships in
/// Chapter 25.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wire up the SmartDocs core services: <see cref="LlmClientOptions"/>,
    /// <see cref="IChatClient"/>, <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>,
    /// and <see cref="ITokenCounter"/>.
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="configuration">
    /// Application configuration. The <c>SmartDocs:Llm</c> section is bound
    /// to <see cref="LlmClientOptions"/>; the active provider determines
    /// which client implementation is registered.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmClientOptions>()
            .Bind(configuration.GetSection(LlmClientOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static opts => opts.Provider != LlmProvider.GigaChat ||
                               !string.IsNullOrWhiteSpace(opts.AuthorizationKey) ||
                               (!string.IsNullOrWhiteSpace(opts.ClientId) && !string.IsNullOrWhiteSpace(opts.ClientSecret)),
                "SmartDocs:Llm:AuthorizationKey (or ClientId + ClientSecret) is required when Provider is GigaChat.")
            .ValidateOnStart();

        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IChatClient>(BuildChatClient);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(BuildEmbeddingGenerator);
        services.AddSingleton<ITokenCounter>(_ => new TokenCounter());

        return services;
    }

    private static IChatClient BuildChatClient(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
        var model = options.ResolveChatModel();

        return options.Provider switch
        {
            LlmProvider.Ollama => new OllamaApiClient(new Uri(options.Endpoint), model),

            LlmProvider.GigaChat => new GigaChatClient(new GigaChatClientOptions
            {
                AuthorizationKey = options.AuthorizationKey,
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
                Scope = options.GigaChatScope,
                BaseUrl = string.IsNullOrWhiteSpace(options.Endpoint) || options.Endpoint == "http://localhost:11434"
                    ? new Uri("https://gigachat.devices.sberbank.ru/api/v1")
                    : new Uri(options.Endpoint),
                DefaultModel = model,
            }).AsChatClient(model),

            _ => throw new InvalidOperationException($"Unsupported provider: {options.Provider}"),
        };
    }

    private static IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator(IServiceProvider sp)
    {
        var embeddingOptions = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        return new EmbeddingGenerator(
            embeddingOptions.ModelPath,
            dimensions: embeddingOptions.Dimensions);
    }
}
