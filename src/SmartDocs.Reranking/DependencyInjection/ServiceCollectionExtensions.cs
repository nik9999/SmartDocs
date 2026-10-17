using System.Net.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;

namespace SmartDocs.Reranking.DependencyInjection;

/// <summary>
/// DI registration for the SmartDocs reranking layer (Chapter 9). Wires up the
/// configured <see cref="IReranker"/> and transparently decorates the already
/// registered <see cref="IRetriever"/> with <see cref="RerankingMiddleware"/>
/// so reranking is invisible to the rest of the pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reranking stack and decorates the existing
    /// <see cref="IRetriever"/> with <see cref="RerankingMiddleware"/>.
    /// <para>
    /// Call this <strong>after</strong> an <see cref="IRetriever"/> has already
    /// been registered (e.g. via the retrieval DI extensions): it uses Scrutor's
    /// <c>Decorate</c> to wrap that existing registration, and the call throws if
    /// none is present.
    /// </para>
    /// <para>
    /// The active reranker is selected at resolve time from
    /// <see cref="SmartDocsOptions"/>.<see cref="SmartDocsOptions.Reranking"/>'s
    /// <see cref="RerankingOptions.Mode"/>:
    /// <list type="bullet">
    ///   <item><c>none</c> → <see cref="NoOpReranker"/> (default, A/B control, fallback).</item>
    ///   <item><c>llm</c> → <see cref="LlmRerank"/> over the registered <see cref="IChatClient"/>.</item>
    ///   <item><c>cohere</c> → <see cref="CohereReranker"/> over the named <c>cohere</c> HttpClient + <c>COHERE_API_KEY</c>.</item>
    ///   <item><c>onnx</c> → <see cref="OnnxCrossEncoderReranker"/>, requires a registered <see cref="ICrossEncoderModel"/>
    ///         (add <c>SmartDocs.Reranking.Onnx</c> and call <c>AddSmartDocsOnnxCrossEncoder</c>).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsReranking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<NoOpReranker>();
        services.AddSingleton<LlmRerank>();

        services.AddSingleton<IReranker>(sp =>
        {
            var reranking = sp.GetRequiredService<IOptions<SmartDocsOptions>>().Value.Reranking;
            var mode = reranking.Mode;
            return mode switch
            {
                "none" => sp.GetRequiredService<NoOpReranker>(),
                "llm" => sp.GetRequiredService<LlmRerank>(),
                "cohere" => BuildCohereReranker(sp),
                "onnx" => BuildOnnxReranker(sp),
                _ => throw new InvalidOperationException($"Unknown reranking mode: {mode}"),
            };
        });

        services.Decorate<IRetriever>((inner, sp) =>
        {
            var reranking = sp.GetRequiredService<IOptions<SmartDocsOptions>>().Value.Reranking;
            return new RerankingMiddleware(
                inner,
                sp.GetRequiredService<IReranker>(),
                reranking.CandidateCount,
                reranking.MinScore);
        });

        return services;
    }

    private static CohereReranker BuildCohereReranker(IServiceProvider sp)
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("cohere");
        var apiKey = Environment.GetEnvironmentVariable("COHERE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Reranking mode 'cohere' requires the COHERE_API_KEY environment variable to be set.");
        }
        return new CohereReranker(httpClient, apiKey);
    }

    private static OnnxCrossEncoderReranker BuildOnnxReranker(IServiceProvider sp)
    {
        var model = sp.GetService<ICrossEncoderModel>()
            ?? throw new InvalidOperationException(
                "Reranking mode 'onnx' requires an ICrossEncoderModel registration. " +
                "Reference the SmartDocs.Reranking.Onnx adapter and call " +
                "AddSmartDocsOnnxCrossEncoder(onnxModelPath, sentencePieceModelPath).");
        return new OnnxCrossEncoderReranker(model);
    }
}
