namespace SmartDocs.Core.Configuration;

/// <summary>
/// Ingestion-time knobs bound from the <c>SmartDocs:Ingestion</c> section of
/// <c>appsettings.json</c>. Controls how documents are chunked before they are
/// embedded and indexed (Chapter 4).
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>
    /// When <see langword="true"/>, the default recursive chunker is wrapped in a
    /// contextual-retrieval decorator that prepends an LLM-generated situating
    /// sentence to each chunk. Needs a reachable LLM endpoint at index time and
    /// costs one chat call per chunk; leave <see langword="false"/> for offline or
    /// cost-sensitive ingestion.
    /// </summary>
    public bool UseContextualRetrieval { get; set; }

    /// <summary>
    /// Default recursive chunk-size budget. Interpreted in <em>characters</em>
    /// today; see the token-aware note in Chapter 4 for converting a token budget
    /// (for example "512-token chunks") into a character budget.
    /// </summary>
    public int MaxChunkSize { get; set; } = 800;

    /// <summary>
    /// When <see langword="true"/> (default), runs document pre-processing and sanitization
    /// (word dehyphenation, blank/boilerplate page filtering, dot-leader normalization, and header/footer cleanup)
    /// before chunking.
    /// </summary>
    public bool PreprocessDocuments { get; set; } = true;
}
