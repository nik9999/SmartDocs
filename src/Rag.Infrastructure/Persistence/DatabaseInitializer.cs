using Microsoft.Data.Sqlite;

namespace Rag.Infrastructure.Persistence;

/// <summary>
/// Initializes and maintains the database schema.
/// Idempotent and safe to call multiple times.
/// </summary>
public sealed class DatabaseInitializer
{
    private readonly SqliteDatabase _database;

    public DatabaseInitializer(SqliteDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// Initializes the database schema if not already present.
    /// Safe to call multiple times — does not destroy existing data.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _database.CreateConnection();

        // Create all tables
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateSystemMetadataTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateDocumentsTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateDocumentMetadataTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksIndex, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateEmbeddingsTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateDocumentMetadataIndex, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksFtsTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksFtsTriggers, cancellationToken).ConfigureAwait(false);

        // Check and set schema version
        var existingVersion = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);

        if (existingVersion == null)
        {
            // No version recorded — set it
            await SetSchemaVersionAsync(connection, DatabaseSchema.CurrentVersion, cancellationToken).ConfigureAwait(false);
        }
        else if (existingVersion != DatabaseSchema.CurrentVersion)
        {
            // Handle known migrations
            if (existingVersion == 1)
            {
                await MigrateFromV1ToV2Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else if (existingVersion == 2)
            {
                await MigrateFromV2ToV3Async(connection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported schema version {existingVersion}. Expected version {DatabaseSchema.CurrentVersion}. " +
                    "Database migration is not implemented.");
            }
        }
    }

    /// <summary>
    /// Migrates schema from version 1 to version 2.
    /// Removes CreatedAt/UpdatedAt columns from Documents table.
    /// </summary>
    private async Task MigrateFromV1ToV2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        // SQLite does not support DROP COLUMN before 3.35.0,
        // so we recreate the table.
        using var cmd = connection.CreateCommand();

        // Copy data to temp table
        cmd.CommandText = """
            CREATE TEMPORARY TABLE Documents_backup(
                DocumentId TEXT NOT NULL PRIMARY KEY,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Source TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        cmd.CommandText = "INSERT INTO Documents_backup SELECT * FROM Documents;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Drop old table
        cmd.CommandText = "DROP TABLE Documents;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Create new table without timestamps
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateDocumentsTable, cancellationToken).ConfigureAwait(false);

        // Copy data back (CreatedAt/UpdatedAt are dropped)
        cmd.CommandText = """
            INSERT INTO Documents (DocumentId, Title, Content, Source)
            SELECT DocumentId, Title, Content, Source FROM Documents_backup;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Drop temp table
        cmd.CommandText = "DROP TABLE Documents_backup;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Update version
        await SetSchemaVersionAsync(connection, DatabaseSchema.CurrentVersion, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Migrates schema from version 2 to version 3.
    /// Adds FTS5 virtual table and triggers for sparse search.
    /// </summary>
    private async Task MigrateFromV2ToV3Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        // Create FTS5 table and triggers
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksFtsTable, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, DatabaseSchema.CreateChunksFtsTriggers, cancellationToken).ConfigureAwait(false);

        // Populate FTS index from existing Chunks data
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ChunksFts (Text, DocumentId, ChunkId)
            SELECT Text, DocumentId, ChunkId FROM Chunks;
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Update version
        await SetSchemaVersionAsync(connection, DatabaseSchema.CurrentVersion, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int?> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM SystemMetadata WHERE Key = @key";
        command.Parameters.AddWithValue("@key", DatabaseSchema.VersionKey);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return int.Parse(reader.GetString(0));
        }

        return null;
    }

    private async Task SetSchemaVersionAsync(SqliteConnection connection, int version, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO SystemMetadata (Key, Value) VALUES (@key, @value)";
        command.Parameters.AddWithValue("@key", DatabaseSchema.VersionKey);
        command.Parameters.AddWithValue("@value", version.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
