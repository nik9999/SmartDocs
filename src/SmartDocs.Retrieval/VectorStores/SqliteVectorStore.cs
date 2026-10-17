using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;
using SmartDocs.Core.Numerics;
using SmartDocs.Retrieval.Filtering;

namespace SmartDocs.Retrieval.VectorStores;

/// <summary>
/// Embedded SQLite-backed <see cref="IVectorStore"/> implementation.
/// Stores embedded chunks in a local file-based or shared in-memory SQLite database,
/// utilizing a custom SIMD-accelerated <c>cosine_similarity</c> function for exact nearest-neighbor search.
/// Ideal for edge deployments, desktop applications, offline RAG, and standalone development.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore, IDisposable, IAsyncDisposable
{
    private readonly SqliteVectorStoreOptions _options;
    private readonly SqliteConnection _connection;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="SqliteVectorStore"/> with the specified options.
    /// </summary>
    public SqliteVectorStore(SqliteVectorStoreOptions? options = null)
    {
        _options = options ?? new SqliteVectorStoreOptions();
        CollectionName = _options.CollectionName;
        _connection = new SqliteConnection(_options.ConnectionString);
        _leaveOpen = false;
    }

    /// <summary>
    /// Creates a new <see cref="SqliteVectorStore"/> with a connection string and collection name.
    /// </summary>
    public SqliteVectorStore(string connectionString, string collectionName = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _options = new SqliteVectorStoreOptions
        {
            ConnectionString = connectionString,
            CollectionName = collectionName
        };
        CollectionName = collectionName;
        _connection = new SqliteConnection(connectionString);
        _leaveOpen = false;
    }

    /// <summary>
    /// Creates a new <see cref="SqliteVectorStore"/> using an existing open connection.
    /// </summary>
    public SqliteVectorStore(SqliteConnection connection, string collectionName = "default", bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _options = new SqliteVectorStoreOptions
        {
            ConnectionString = connection.ConnectionString,
            CollectionName = collectionName
        };
        CollectionName = collectionName;
        _connection = connection;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public string CollectionName { get; }

    /// <inheritdoc />
    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = $"""
                INSERT INTO {_options.TableName} (
                    chunk_id, collection_name, document_id, chunk_index, text,
                    start_char_offset, end_char_offset, metadata_json, model_id, vector
                ) VALUES (
                    @chunk_id, @collection_name, @document_id, @chunk_index, @text,
                    @start_char_offset, @end_char_offset, @metadata_json, @model_id, @vector
                )
                ON CONFLICT(chunk_id) DO UPDATE SET
                    collection_name = excluded.collection_name,
                    document_id = excluded.document_id,
                    chunk_index = excluded.chunk_index,
                    text = excluded.text,
                    start_char_offset = excluded.start_char_offset,
                    end_char_offset = excluded.end_char_offset,
                    metadata_json = excluded.metadata_json,
                    model_id = excluded.model_id,
                    vector = excluded.vector;
                """;

            var pChunkId = cmd.Parameters.Add("@chunk_id", SqliteType.Text);
            var pCol = cmd.Parameters.Add("@collection_name", SqliteType.Text);
            var pDocId = cmd.Parameters.Add("@document_id", SqliteType.Text);
            var pIndex = cmd.Parameters.Add("@chunk_index", SqliteType.Integer);
            var pText = cmd.Parameters.Add("@text", SqliteType.Text);
            var pStart = cmd.Parameters.Add("@start_char_offset", SqliteType.Integer);
            var pEnd = cmd.Parameters.Add("@end_char_offset", SqliteType.Integer);
            var pMeta = cmd.Parameters.Add("@metadata_json", SqliteType.Text);
            var pModel = cmd.Parameters.Add("@model_id", SqliteType.Text);
            var pVec = cmd.Parameters.Add("@vector", SqliteType.Blob);

            pCol.Value = CollectionName;

            foreach (var ec in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pChunkId.Value = ec.Chunk.ChunkId;
                pDocId.Value = ec.Chunk.DocumentId;
                pIndex.Value = ec.Chunk.ChunkIndex;
                pText.Value = ec.Chunk.Text;
                pStart.Value = ec.Chunk.StartCharOffset;
                pEnd.Value = ec.Chunk.EndCharOffset;
                pMeta.Value = JsonSerializer.Serialize(ec.Chunk.Metadata);
                pModel.Value = ec.EmbeddingModel ?? string.Empty;
                pVec.Value = MemoryMarshal.AsBytes(ec.Vector.Span).ToArray();

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var queryBytes = MemoryMarshal.AsBytes(queryVector.Span).ToArray();
            var compiledFilter = filter is null ? null : SqliteFilterCompiler.TryCompile(filter);
            var useSqlFilter = compiledFilter is not null;

            using var cmd = _connection.CreateCommand();

            if (useSqlFilter)
            {
                // True database pre-filter: evaluate metadata predicate inside SQLite BEFORE
                // SIMD cosine distance calculation and sorting, with engine-level LIMIT.
                cmd.CommandText = $"""
                    SELECT chunk_id, document_id, chunk_index, text,
                           start_char_offset, end_char_offset, metadata_json,
                           cosine_similarity(@queryVec, vector) AS score
                    FROM {_options.TableName}
                    WHERE collection_name = @collection AND ({compiledFilter!.SqlWhere})
                    ORDER BY score DESC
                    LIMIT @topK;
                    """;
                cmd.Parameters.AddWithValue("@queryVec", queryBytes);
                cmd.Parameters.AddWithValue("@collection", CollectionName);
                cmd.Parameters.AddWithValue("@topK", topK);

                foreach (var (pName, pVal) in compiledFilter.Parameters)
                {
                    cmd.Parameters.AddWithValue(pName, pVal ?? DBNull.Value);
                }
            }
            else if (filter is null)
            {
                // Unfiltered fast path: native LIMIT @topK in SQLite
                cmd.CommandText = $"""
                    SELECT chunk_id, document_id, chunk_index, text,
                           start_char_offset, end_char_offset, metadata_json,
                           cosine_similarity(@queryVec, vector) AS score
                    FROM {_options.TableName}
                    WHERE collection_name = @collection
                    ORDER BY score DESC
                    LIMIT @topK;
                    """;
                cmd.Parameters.AddWithValue("@queryVec", queryBytes);
                cmd.Parameters.AddWithValue("@collection", CollectionName);
                cmd.Parameters.AddWithValue("@topK", topK);
            }
            else
            {
                // In-process fallback: when filter uses expressions unsupported by SQL compiler
                cmd.CommandText = $"""
                    SELECT chunk_id, document_id, chunk_index, text,
                           start_char_offset, end_char_offset, metadata_json,
                           cosine_similarity(@queryVec, vector) AS score
                    FROM {_options.TableName}
                    WHERE collection_name = @collection
                    ORDER BY score DESC;
                    """;
                cmd.Parameters.AddWithValue("@queryVec", queryBytes);
                cmd.Parameters.AddWithValue("@collection", CollectionName);
            }

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<RetrievalResult>(topK);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var metaJson = reader.GetString(6);
                var metadata = JsonSerializer.Deserialize<DocumentMetadata>(metaJson)
                    ?? new DocumentMetadata(reader.GetString(1), "general", "", "", "", "general", 2026, "", DateOnly.MinValue, "");

                // In-process fallback check: only needed when SQL filter was not applied
                if (!useSqlFilter && filter is not null && !filter.Matches(metadata))
                {
                    continue;
                }

                var chunkId = reader.GetString(0);
                var docId = reader.GetString(1);
                var chunkIndex = reader.GetInt32(2);
                var text = reader.GetString(3);
                var startOffset = reader.GetInt32(4);
                var endOffset = reader.GetInt32(5);
                var score = reader.GetDouble(7);

                var chunk = new DocumentChunk(chunkId, docId, chunkIndex, text, startOffset, endOffset, metadata);
                results.Add(new RetrievalResult(chunk, score));

                if (results.Count >= topK)
                {
                    break;
                }
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var tx = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = $"DELETE FROM {_options.TableName} WHERE chunk_id = @chunk_id AND collection_name = @collection;";
            var pId = cmd.Parameters.Add("@chunk_id", SqliteType.Text);
            cmd.Parameters.AddWithValue("@collection", CollectionName);

            foreach (var id in chunkIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pId.Value = id;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the count of chunks stored in this collection.
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {_options.TableName} WHERE collection_name = @collection;";
            cmd.Parameters.AddWithValue("@collection", CollectionName);
            var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            // Register SIMD-accelerated Cosine similarity SQLite user function
            _connection.CreateFunction<byte[], byte[], double>(
                "cosine_similarity",
                static (queryBytes, candidateBytes) =>
                {
                    if (queryBytes is null || candidateBytes is null || queryBytes.Length == 0 || candidateBytes.Length == 0)
                    {
                        return 0.0;
                    }
                    var q = MemoryMarshal.Cast<byte, float>(queryBytes);
                    var c = MemoryMarshal.Cast<byte, float>(candidateBytes);
                    return CosineKernel.Cosine(q, c);
                },
                isDeterministic: true);

            if (_options.AutoCreateSchema)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS {_options.TableName} (
                        chunk_id TEXT PRIMARY KEY,
                        collection_name TEXT NOT NULL,
                        document_id TEXT NOT NULL,
                        chunk_index INTEGER NOT NULL,
                        text TEXT NOT NULL,
                        start_char_offset INTEGER NOT NULL,
                        end_char_offset INTEGER NOT NULL,
                        metadata_json TEXT NOT NULL,
                        model_id TEXT NOT NULL,
                        vector BLOB NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_{_options.TableName}_collection
                        ON {_options.TableName}(collection_name);
                    """;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            _connection.Dispose();
        }
        _lock.Dispose();
        _initLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_leaveOpen)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        _lock.Dispose();
        _initLock.Dispose();
    }
}
