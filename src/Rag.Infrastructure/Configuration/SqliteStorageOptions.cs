namespace Rag.Infrastructure.Configuration;

/// <summary>
/// Configuration for SQLite storage.
/// </summary>
public sealed class SqliteStorageOptions
{
    /// <summary>
    /// SQLite connection string. Default: Data Source=data/rag.db
    /// </summary>
    public string ConnectionString { get; init; } = "Data Source=data/rag.db";
}
