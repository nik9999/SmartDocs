using System.ComponentModel.DataAnnotations;

namespace SmartDocs.Core.Configuration;

/// <summary>
/// Configuration options for embeddings in SmartDocs.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>Configuration section path bound to this class.</summary>
    public const string SectionName = "SmartDocs:Embedding";

    /// <summary>
    /// Embedding model identifier. Defaults to <c>bge-m3</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string EmbeddingModel { get; set; } = "bge-m3";

    /// <summary>
    /// Dimensionality of the embedding vectors produced by the model (1024 for BGE-M3).
    /// </summary>
    public int Dimensions { get; set; } = 1024;

    /// <summary>
    /// Path to local ONNX model file.
    /// </summary>
    public string ModelPath { get; set; } = "models/bge-m3/model.onnx";
}
