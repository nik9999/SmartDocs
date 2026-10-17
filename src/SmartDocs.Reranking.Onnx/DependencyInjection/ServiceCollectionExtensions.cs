using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Reranking;

namespace SmartDocs.Reranking.Onnx.DependencyInjection;

/// <summary>
/// DI registration for the opt-in ONNX cross-encoder adapter (Chapter 9). This
/// is the only entry point that pulls the native ONNX runtime into the
/// container, so it is referenced only by apps that want self-hosted reranking.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the self-hosted <c>bge-reranker-v2-m3</c> ONNX cross-encoder as
    /// the <see cref="ICrossEncoderModel"/> and wires up
    /// <see cref="OnnxCrossEncoderReranker"/> as the <see cref="IReranker"/>.
    /// <para>
    /// This adapter pulls in the native ONNX runtime; reference it only when you
    /// intend to run reranking locally. After calling it, set the reranking mode
    /// to <c>onnx</c> so the reranking DI extension resolves this model.
    /// </para>
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="onnxModelPath">Path to the exported bge-reranker-v2-m3 <c>model.onnx</c>.</param>
    /// <param name="sentencePieceModelPath">Path to the model's SentencePiece model file.</param>
    /// <param name="maxLength">Maximum combined sequence length (truncation budget).</param>
    /// <param name="batchSize">How many (query, document) pairs to run per inference batch.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsOnnxCrossEncoder(
        this IServiceCollection services,
        string onnxModelPath,
        string sentencePieceModelPath,
        int maxLength = 512,
        int batchSize = 16)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(onnxModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentencePieceModelPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        services.AddSingleton<ICrossEncoderModel>(
            _ => BgeOnnxCrossEncoderModel.Create(onnxModelPath, sentencePieceModelPath, maxLength, batchSize));
        services.AddSingleton<IReranker, OnnxCrossEncoderReranker>();

        return services;
    }
}
