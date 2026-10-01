using Microsoft.Data.Sqlite;
using Rag.Application.Documents;
using Rag.Core.Documents;

namespace Rag.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists documents, metadata, and chunks to SQLite.
/// </summary>
public sealed class DocumentRepository : IDocumentRepository
{
    private readonly string _connectionString;

    public DocumentRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Validate invariant: all chunks belong to this document
            foreach (var chunk in chunks)
            {
                if (chunk.DocumentId != document.DocumentId)
                {
                    throw new ArgumentException(
                        $"Chunk {chunk.ChunkId} belongs to document {chunk.DocumentId}, " +
                        $"but document {document.DocumentId} is being saved.",
                        nameof(chunks));
                }
            }

            // Upsert the document
            await SaveDocumentAsync(connection, transaction, document, cancellationToken).ConfigureAwait(false);

            // Delete existing metadata and chunks for this document
            await DeleteExistingMetadataAsync(connection, transaction, document.DocumentId, cancellationToken).ConfigureAwait(false);
            await DeleteExistingChunksAsync(connection, transaction, document.DocumentId, cancellationToken).ConfigureAwait(false);

            // Insert metadata
            await SaveMetadataAsync(connection, transaction, document.DocumentId, document.Metadata, cancellationToken).ConfigureAwait(false);

            // Insert chunks
            await SaveChunksAsync(connection, transaction, document.DocumentId, chunks, cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DocumentAggregate?> GetAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Load document
        var document = await LoadDocumentAsync(connection, documentId, cancellationToken).ConfigureAwait(false);
        if (document == null)
            return null;

        // Load chunks ordered by position
        var chunks = await LoadChunksAsync(connection, documentId, cancellationToken).ConfigureAwait(false);

        return new DocumentAggregate(document, chunks);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Documents WHERE DocumentId = @documentId";
            command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));
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

    private static async Task SaveDocumentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Document document,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO Documents (DocumentId, Title, Content, Source)
            VALUES (@documentId, @title, @content, @source)
            """;
        command.Parameters.AddWithValue("@documentId", document.DocumentId.ToString("D"));
        command.Parameters.AddWithValue("@title", document.Title);
        command.Parameters.AddWithValue("@content", document.Content);
        command.Parameters.AddWithValue("@source", document.Source);
        command.Transaction = transaction;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid documentId,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        foreach (var entry in metadata)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO DocumentMetadata (DocumentId, Key, Value)
                VALUES (@documentId, @key, @value)
                """;
            command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));
            command.Parameters.AddWithValue("@key", entry.Key);
            command.Parameters.AddWithValue("@value", entry.Value);
            command.Transaction = transaction;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SaveChunksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid documentId,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Chunks (ChunkId, DocumentId, Text, Position, PageNumber, Section, TokenCount)
                VALUES (@chunkId, @documentId, @text, @position, @pageNumber, @section, @tokenCount)
                """;
            command.Parameters.AddWithValue("@chunkId", chunk.ChunkId.ToString("D"));
            command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));
            command.Parameters.AddWithValue("@text", chunk.Text);
            command.Parameters.AddWithValue("@position", chunk.Position);

            if (chunk.Metadata.PageNumber.HasValue)
                command.Parameters.AddWithValue("@pageNumber", chunk.Metadata.PageNumber.Value);
            else
                command.Parameters.AddWithValue("@pageNumber", DBNull.Value);

            if (!string.IsNullOrEmpty(chunk.Metadata.Section))
                command.Parameters.AddWithValue("@section", chunk.Metadata.Section);
            else
                command.Parameters.AddWithValue("@section", DBNull.Value);

            if (chunk.Metadata.TokenCount.HasValue)
                command.Parameters.AddWithValue("@tokenCount", chunk.Metadata.TokenCount.Value);
            else
                command.Parameters.AddWithValue("@tokenCount", DBNull.Value);

            command.Transaction = transaction;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeleteExistingMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DocumentMetadata WHERE DocumentId = @documentId";
        command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));
        command.Transaction = transaction;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteExistingChunksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Chunks WHERE DocumentId = @documentId";
        command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));
        command.Transaction = transaction;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document?> LoadDocumentAsync(
        SqliteConnection connection,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title, Content, Source FROM Documents WHERE DocumentId = @documentId";
        command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var title = reader.GetString(0);
        var content = reader.GetString(1);
        var source = reader.GetString(2);

        // Load metadata
        var metadata = await LoadMetadataAsync(connection, documentId, cancellationToken).ConfigureAwait(false);

        return new Document(documentId, title, content, source, metadata);
    }

    private static async Task<IReadOnlyList<DocumentChunk>> LoadChunksAsync(
        SqliteConnection connection,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChunkId, Text, Position, PageNumber, Section, TokenCount
            FROM Chunks
            WHERE DocumentId = @documentId
            ORDER BY Position ASC
            """;
        command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunkId = Guid.Parse(reader.GetString(0));
            var pageNumber = reader.IsDBNull(3) ? (int?)null : (int?)reader.GetInt32(3);
            var section = reader.IsDBNull(4) ? null : reader.GetString(4);
            var tokenCount = reader.IsDBNull(5) ? (int?)null : (int?)reader.GetInt32(5);

            chunks.Add(new DocumentChunk(
                chunkId,
                documentId,
                reader.GetString(1),
                reader.GetInt32(2),
                new ChunkMetadata(pageNumber, section, tokenCount)));
        }

        return chunks;
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadMetadataAsync(
        SqliteConnection connection,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM DocumentMetadata WHERE DocumentId = @documentId";
        command.Parameters.AddWithValue("@documentId", documentId.ToString("D"));

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            metadata[reader.GetString(0)] = reader.GetString(1);
        }

        return metadata;
    }
}
