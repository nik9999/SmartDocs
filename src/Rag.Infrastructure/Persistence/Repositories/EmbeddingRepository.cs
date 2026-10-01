using Microsoft.Data.Sqlite;
using Rag.Core.Embeddings;

namespace Rag.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists embedding vectors to SQLite using binary BLOB storage.
/// </summary>
public sealed class EmbeddingRepository : IEmbeddingRepository
{
    private readonly string _connectionString;

    public EmbeddingRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Guid chunkId,
        Embedding embedding,
        CancellationToken cancellationToken)
    {
        if (embedding == null)
            throw new ArgumentNullException(nameof(embedding));

        if (embedding.Vector.Length != embedding.Dimensions)
            throw new ArgumentException(
                $"Vector length ({embedding.Vector.Length}) does not match Dimensions ({embedding.Dimensions}).",
                nameof(embedding));

        if (string.IsNullOrWhiteSpace(embedding.Model))
            throw new ArgumentException("Model must not be empty.", nameof(embedding));

        if (embedding.Dimensions <= 0)
            throw new ArgumentException("Dimensions must be greater than zero.", nameof(embedding));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Upsert embedding
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO Embeddings (ChunkId, Model, Dimensions, Vector)
                VALUES (@chunkId, @model, @dimensions, @vector)
                """;
            command.Parameters.AddWithValue("@chunkId", chunkId.ToString("D"));
            command.Parameters.AddWithValue("@model", embedding.Model);
            command.Parameters.AddWithValue("@dimensions", embedding.Dimensions);
            command.Parameters.AddWithValue("@vector", SerializeVector(embedding.Vector));
            command.Transaction = transaction;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Embedding?> GetAsync(
        Guid chunkId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Model, Dimensions, Vector FROM Embeddings WHERE ChunkId = @chunkId";
        command.Parameters.AddWithValue("@chunkId", chunkId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var model = reader.GetString(0);
        var dimensions = reader.GetInt32(1);
        var vector = DeserializeVector((byte[])reader.GetValue(2), dimensions);

        return new Embedding(vector, model);
    }

    /// <summary>
    /// Serializes a float array to a byte array (explicit IEEE-754 float32 little-endian).
    /// </summary>
    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            var fBytes = BitConverter.GetBytes(vector[i]);
            // Ensure little-endian: if machine is big-endian, reverse
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(fBytes);
            }
            Buffer.BlockCopy(fBytes, 0, bytes, i * sizeof(float), sizeof(float));
        }
        return bytes;
    }

    /// <summary>
    /// Deserializes a byte array to a float array (explicit IEEE-754 float32 little-endian).
    /// </summary>
    private static float[] DeserializeVector(byte[] bytes, int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float))
            throw new ArgumentException(
                $"Byte array length ({bytes.Length}) does not match expected dimensions ({dimensions * sizeof(float)}).",
                nameof(bytes));

        var vector = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            var fBytes = new byte[sizeof(float)];
            Buffer.BlockCopy(bytes, i * sizeof(float), fBytes, 0, sizeof(float));
            // Ensure little-endian: if machine is big-endian, reverse
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(fBytes);
            }
            vector[i] = BitConverter.ToSingle(fBytes, 0);
        }
        return vector;
    }
}
