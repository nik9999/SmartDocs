using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Infrastructure.Persistence;
using Xunit;

namespace Rag.Infrastructure.Tests;

public class DatabaseInitializationTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_test_{Guid.NewGuid()}.db");
    }

    private IServiceProvider CreateServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(connectionString));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDocumentRepository, Infrastructure.Persistence.Repositories.DocumentRepository>(sp =>
            new Infrastructure.Persistence.Repositories.DocumentRepository(connectionString));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InitializeAsync_CreatesAllTables()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert - verify tables exist
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";

        var tables = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("Documents", tables);
        Assert.Contains("DocumentMetadata", tables);
        Assert.Contains("Chunks", tables);
        Assert.Contains("Embeddings", tables);
        Assert.Contains("SystemMetadata", tables);
    }

    [Fact]
    public async Task InitializeAsync_RecordsSchemaVersion()
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
    public async Task InitializeAsync_Reentrant_Safe()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act - call twice
        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        // Assert - should not throw
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Documents";
        var scalar = await cmd.ExecuteScalarAsync();
        var count = Convert.ToInt32(scalar);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task InitializeAsync_DocumentsTableHasNoTimestampColumns()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act
        await initializer.InitializeAsync();

        // Assert - verify Documents table schema
        await using var connection = db.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Documents)";

        var columns = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("DocumentId", columns);
        Assert.Contains("Title", columns);
        Assert.Contains("Content", columns);
        Assert.Contains("Source", columns);
        Assert.DoesNotContain("CreatedAt", columns);
        Assert.DoesNotContain("UpdatedAt", columns);
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedVersion_Throws()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";

        // Manually create SystemMetadata with unsupported version
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE SystemMetadata (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "INSERT INTO SystemMetadata (Key, Value) VALUES ('schema_version', '99')";
        await cmd.ExecuteNonQueryAsync();

        var db = new SqliteDatabase(connectionString);
        var initializer = new DatabaseInitializer(db);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.InitializeAsync());
        Assert.Contains("Unsupported schema version", exception.Message);
        Assert.Contains("99", exception.Message);
    }
}
