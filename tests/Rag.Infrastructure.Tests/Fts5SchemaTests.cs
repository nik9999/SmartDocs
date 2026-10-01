using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rag.Core.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class Fts5SchemaTests
{
    [Fact]
    public async Task InitializeAsync_CreatesChunksFtsTable()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ChunksFts'";
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InitializeAsync_CreatesFtsTriggers()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert - check triggers exist
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE 'ChunksFts%'";
        var triggers = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            triggers.Add(reader.GetString(0));
        }

        Assert.Contains("ChunksFtsAfterInsert", triggers);
        Assert.Contains("ChunksFtsAfterDelete", triggers);
        Assert.Contains("ChunksFtsAfterUpdate", triggers);
    }

    [Fact]
    public async Task InitializeAsync_SchemaVersionIs3()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM SystemMetadata WHERE Key = 'schema_version'";
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        Assert.Equal("3", result.ToString());
    }

    [Fact]
    public async Task InitializeAsync_PopulatesFtsIndexFromExistingChunks()
    {
        // Arrange - manually insert a chunk before FTS is ready
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";

        await using var setupConnection = new SqliteConnection(connectionString);
        await setupConnection.OpenAsync();
        using var setupCmd = setupConnection.CreateCommand();

        // Create tables
        setupCmd.CommandText = DatabaseSchema.CreateSystemMetadataTable;
        await setupCmd.ExecuteNonQueryAsync();
        setupCmd.CommandText = DatabaseSchema.CreateDocumentsTable;
        await setupCmd.ExecuteNonQueryAsync();
        setupCmd.CommandText = DatabaseSchema.CreateChunksTable;
        await setupCmd.ExecuteNonQueryAsync();
        setupCmd.CommandText = DatabaseSchema.CreateChunksFtsTable;
        await setupCmd.ExecuteNonQueryAsync();
        setupCmd.CommandText = DatabaseSchema.CreateChunksFtsTriggers;
        await setupCmd.ExecuteNonQueryAsync();
        setupCmd.CommandText = "INSERT INTO SystemMetadata (Key, Value) VALUES ('schema_version', '3')";
        await setupCmd.ExecuteNonQueryAsync();

        // Insert a document and chunk
        var docId = Guid.NewGuid();
        setupCmd.CommandText = "INSERT INTO Documents (DocumentId, Title, Content, Source) VALUES (@id, @title, @content, @source)";
        setupCmd.Parameters.AddWithValue("@id", docId.ToString("D"));
        setupCmd.Parameters.AddWithValue("@title", "Test");
        setupCmd.Parameters.AddWithValue("@content", "Test");
        setupCmd.Parameters.AddWithValue("@source", "test");
        await setupCmd.ExecuteNonQueryAsync();

        var chunkId = Guid.NewGuid();
        setupCmd.CommandText = "INSERT INTO Chunks (ChunkId, DocumentId, Text, Position) VALUES (@chunkId, @docId, @text, @pos)";
        setupCmd.Parameters.AddWithValue("@chunkId", chunkId.ToString("D"));
        setupCmd.Parameters.AddWithValue("@docId", docId.ToString("D"));
        setupCmd.Parameters.AddWithValue("@text", "Измеренное значение Канал 2");
        setupCmd.Parameters.AddWithValue("@pos", 0);
        await setupCmd.ExecuteNonQueryAsync();

        // Now run initializer — it should NOT crash and should rebuild FTS
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert — chunk should be searchable
        await using var searchConnection = new SqliteConnection(connectionString);
        await searchConnection.OpenAsync();
        using var searchCmd = searchConnection.CreateCommand();
        searchCmd.CommandText = "SELECT COUNT(*) FROM ChunksFts";
        var count = Convert.ToInt32(await searchCmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_fts_test_{Guid.NewGuid()}.db");
    }
}
