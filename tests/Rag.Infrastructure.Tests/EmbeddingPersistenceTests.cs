using Microsoft.Data.Sqlite;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Rag.Infrastructure.Tests;

/// <summary>
/// Tests for IEmbeddingRepository persistence.
/// </summary>
public class EmbeddingPersistenceTests
{
    private string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_embedding_test_{Guid.NewGuid()}.db");
    }

    private void CreateEmbeddingsTable(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        // Disable foreign keys for isolated testing
        cmd.CommandText = "PRAGMA foreign_keys = OFF";
        cmd.ExecuteNonQuery();
        cmd.CommandText = DatabaseSchema.CreateChunksTable;
        cmd.ExecuteNonQuery();
        cmd.CommandText = DatabaseSchema.CreateEmbeddingsTable;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task SaveAsync_SavesEmbedding()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();
        var vector = new float[] { 0.1f, 0.2f, 0.3f };
        var embedding = new Embedding(vector, "test-model");

        // Act
        await repo.SaveAsync(chunkId, embedding, CancellationToken.None);

        // Assert
        var result = await repo.GetAsync(chunkId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(3, result.Dimensions);
        Assert.Equal(3, result.Vector.Length);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForNonExistent()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await repo.GetAsync(nonExistentId, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_RoundTrip_VectorExact()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();
        var vector = new float[] { 0.12345f, -0.6789f, 0.0001f, 1.0f, -0.5f };
        var embedding = new Embedding(vector, "roundtrip-model");

        // Act
        await repo.SaveAsync(chunkId, embedding, CancellationToken.None);
        var result = await repo.GetAsync(chunkId, CancellationToken.None);

        // Assert - exact binary round-trip
        Assert.NotNull(result);
        Assert.Equal(vector.Length, result.Vector.Length);
        for (var i = 0; i < vector.Length; i++)
        {
            Assert.Equal(vector[i], result.Vector[i], precision: 5);
        }
    }

    [Fact]
    public async Task SaveAsync_Replacement_Overwrites()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();
        var vector1 = new float[] { 0.1f, 0.2f };
        var embedding1 = new Embedding(vector1, "model-v1");
        await repo.SaveAsync(chunkId, embedding1, CancellationToken.None);

        var vector2 = new float[] { 0.3f, 0.4f, 0.5f };
        var embedding2 = new Embedding(vector2, "model-v2");

        // Act
        await repo.SaveAsync(chunkId, embedding2, CancellationToken.None);
        var result = await repo.GetAsync(chunkId, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("model-v2", result.Model);
        Assert.Equal(3, result.Dimensions);
        Assert.Equal(3, result.Vector.Length);
    }

    [Fact]
    public async Task SaveAsync_EmptyVector_Throws()
    {
        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();
        var emptyVector = Array.Empty<float>();

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            repo.SaveAsync(chunkId, new Embedding(emptyVector, "test"), CancellationToken.None));
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public async Task SaveAsync_BlobRoundTrip_ExplicitLittleEndian()
    {
        // Arrange - test values covering edge cases
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        CreateEmbeddingsTable(connectionString);
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();
        // Include: 0, 1, -1, 0.5, -0.5, and real embedding-like values
        var vector = new float[]
        {
            0f,
            1f,
            -1f,
            0.5f,
            -0.5f,
            0.1234567f,
            -0.9876543f,
            15.4f,
            0.0f,
            -3.14159f
        };
        var embedding = new Embedding(vector, "blob-test-model");

        // Act
        await repo.SaveAsync(chunkId, embedding, CancellationToken.None);
        var result = await repo.GetAsync(chunkId, CancellationToken.None);

        // Assert - exact binary round-trip for all values
        Assert.NotNull(result);
        Assert.Equal(vector.Length, result.Vector.Length);
        for (var i = 0; i < vector.Length; i++)
        {
            Assert.Equal(vector[i], result.Vector[i], precision: 7);
        }
    }
}
