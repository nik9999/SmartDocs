namespace Rag.Infrastructure.Configuration;

/// <summary>
/// Configuration options for ONNX embedding generation.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Path to the ONNX model directory.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Name of the ONNX model file (without extension).
    /// </summary>
    public string ModelName { get; set; } = "model.onnx";

    /// <summary>
    /// Maximum sequence length for tokenization.
    /// </summary>
    public int MaxLength { get; set; } = 128;
}
