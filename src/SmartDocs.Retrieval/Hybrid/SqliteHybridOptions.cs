namespace SmartDocs.Retrieval.Hybrid;

/// <summary>
/// Knobs controlling <see cref="SqliteHybridRetriever"/>.
/// </summary>
public sealed class SqliteHybridOptions
{
    /// <summary>
    /// SQLite connection string. Default is <c>Data Source=smartdocs_vectors.db</c>.
    /// Use <c>Data Source=:memory:;Mode=Memory;Cache=Shared</c> for in-memory testing.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=smartdocs_vectors.db";

    /// <summary>
    /// Collection partition name. Default is <c>"default"</c>.
    /// </summary>
    public string CollectionName { get; set; } = "default";

    /// <summary>
    /// Table name for vector chunks. Default is <c>"smartdocs_vectors"</c>.
    /// </summary>
    public string VectorsTableName { get; set; } = "smartdocs_vectors";

    /// <summary>
    /// Virtual table name for FTS5 full-text search. Default is <c>"smartdocs_fts"</c>.
    /// </summary>
    public string FtsTableName { get; set; } = "smartdocs_fts";

    /// <summary>
    /// RRF smoothing constant (default 60, matching Cormack et al. 2009 and <see cref="RrfMerger"/>).
    /// </summary>
    public int RrfConstant { get; set; } = 60;

    /// <summary>
    /// Prefetch candidate multiplier per leg before fusion. Default is 4 (i.e. fetches 4 × topK candidates).
    /// </summary>
    public int CandidateMultiplier { get; set; } = 4;

    /// <summary>
    /// When <see langword="true"/>, automatically initializes tables, triggers, and indexes. Default is <see langword="true"/>.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;
}
