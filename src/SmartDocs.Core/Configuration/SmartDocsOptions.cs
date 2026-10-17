namespace SmartDocs.Core.Configuration;

/// <summary>
/// Umbrella options object bound to the top-level <c>SmartDocs</c> configuration
/// section. Groups the cross-cutting SmartDocs settings so a single
/// <c>IOptions&lt;SmartDocsOptions&gt;</c> resolve exposes every sub-section.
/// </summary>
public sealed class SmartDocsOptions
{
    /// <summary>Configuration section path bound to this class.</summary>
    public const string SectionName = "SmartDocs";

    /// <summary>Ingestion-time chunking settings (Chapter 4).</summary>
    public IngestionOptions Ingestion { get; set; } = new();

    /// <summary>Reranking settings — mode, candidate pool, and relevance floor (Chapter 9).</summary>
    public RerankingOptions Reranking { get; set; } = new();

    /// <summary>Embedding settings — model identifier, dimensions, and model path.</summary>
    public EmbeddingOptions Embedding { get; set; } = new();
}
