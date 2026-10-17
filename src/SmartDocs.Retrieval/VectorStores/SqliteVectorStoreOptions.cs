namespace SmartDocs.Retrieval.VectorStores;

/// <summary>
/// Configuration options for <see cref="SqliteVectorStore"/>.
/// </summary>
public sealed class SqliteVectorStoreOptions
{
    /// <summary>
    /// Connection string for the SQLite database.
    /// Defaults to <c>Data Source=smartdocs_vectors.db</c>.
    /// Use <c>Data Source=:memory:;Mode=Memory;Cache=Shared</c> for in-memory testing.
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=smartdocs_vectors.db";

    /// <summary>
    /// The collection / partition name inside the SQLite table.
    /// Defaults to <c>"default"</c>.
    /// </summary>
    public string CollectionName { get; set; } = "default";

    /// <summary>
    /// The table name where vectors and chunks are stored.
    /// Defaults to <c>"smartdocs_vectors"</c>.
    /// </summary>
    public string TableName { get; set; } = "smartdocs_vectors";

    /// <summary>
    /// When <see langword="true"/>, automatically creates the table and index if they do not exist.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;
}
