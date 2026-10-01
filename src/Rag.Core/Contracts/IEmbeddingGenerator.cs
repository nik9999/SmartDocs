namespace Rag.Core.Contracts;

using Rag.Core.Embeddings;

/// <summary>
/// Generates embedding vectors from text using a local ONNX model.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">Input text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vector with model metadata.</returns>
    Task<Embedding> GenerateAsync(
        string text,
        CancellationToken cancellationToken);
}
