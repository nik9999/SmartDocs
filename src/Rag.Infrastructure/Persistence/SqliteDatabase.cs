using Microsoft.Data.Sqlite;

namespace Rag.Infrastructure.Persistence;

/// <summary>
/// Manages SQLite connection lifecycle and configuration.
/// Each call to CreateConnection returns a fresh connection.
/// </summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates and opens a new connection for the given operation.
    /// Each call returns a fresh connection — not a shared singleton.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();

        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
    }
}
