using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;
using SmartDocs.Core.Numerics;
using SmartDocs.Retrieval.Filtering;

namespace SmartDocs.Retrieval.Hybrid;

/// <summary>
/// Single-store hybrid retriever over an embedded SQLite database.
/// Runs dense ranking (via SIMD-accelerated <c>cosine_similarity</c>), sparse ranking (via SQLite FTS5 BM25),
/// and Reciprocal Rank Fusion (RRF) <em>in a single database round-trip</em> — the "database is the hybrid"
/// pattern running fully embedded and offline without any external services.
/// </summary>
public sealed partial class SqliteHybridRetriever : IRetriever, IDisposable, IAsyncDisposable
{
    private readonly IEmbeddingService _embeddings;
    private readonly SqliteHybridOptions _options;
    private readonly SqliteConnection _connection;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    [GeneratedRegex(@"[\p{L}\d_]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Creates a new <see cref="SqliteHybridRetriever"/> over a connection string.
    /// </summary>
    public SqliteHybridRetriever(
        IEmbeddingService embeddings,
        string connectionString,
        string collectionName = "default",
        SqliteHybridOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _embeddings = embeddings;
        _options = options ?? new SqliteHybridOptions
        {
            ConnectionString = connectionString,
            CollectionName = collectionName
        };
        _options.ConnectionString = connectionString;
        _options.CollectionName = collectionName;
        _connection = new SqliteConnection(connectionString);
        _leaveOpen = false;
    }

    /// <summary>
    /// Creates a new <see cref="SqliteHybridRetriever"/> over an existing open SQLite connection.
    /// </summary>
    public SqliteHybridRetriever(
        IEmbeddingService embeddings,
        SqliteConnection connection,
        string collectionName = "default",
        bool leaveOpen = false,
        SqliteHybridOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _embeddings = embeddings;
        _options = options ?? new SqliteHybridOptions
        {
            ConnectionString = connection.ConnectionString,
            CollectionName = collectionName
        };
        _options.ConnectionString = connection.ConnectionString;
        _options.CollectionName = collectionName;
        _connection = connection;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc />
    public string Strategy => "hybrid-sqlite";

    /// <summary>
    /// Ensures that the vector table, FTS5 virtual table, triggers, and indices exist.
    /// </summary>
    public async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates chunks into the SQLite database. Automatically populates both
    /// the dense vector table and the FTS5 full-text index.
    /// </summary>
    public async Task IndexChunksAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
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
                INSERT INTO {_options.VectorsTableName} (
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

            pCol.Value = _options.CollectionName;

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
    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
        => RetrieveAsync(query, topK, filter: null, cancellationToken);

    /// <summary>
    /// Retrieves up to <paramref name="topK"/> results for <paramref name="query"/>,
    /// optionally restricted by <paramref name="filter"/> before ranking.
    /// </summary>
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        MetadataFilter? filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ThrowIfDisposed();
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        // 1. Produce dense query embedding with model query-task prefix applied
        var queryVec = await _embeddings.EmbedQueryAsync(query, cancellationToken).ConfigureAwait(false);
        var queryBytes = MemoryMarshal.AsBytes(queryVec.Span).ToArray();

        // 2. Format FTS5 query string with prefix wildcard matching for morphology
        var ftsQuery = FormatFtsQuery(query);
        var candidateK = Math.Max(topK * _options.CandidateMultiplier, topK + 5);

        // 3. Compile metadata filter to SQL WHERE clause with alias 'v'
        var compiledFilter = filter is null ? null : SqliteFilterCompiler.TryCompile(filter, tableAlias: "v");
        var useSqlFilter = compiledFilter is not null;
        var filterCondition = useSqlFilter ? $" AND ({compiledFilter!.SqlWhere})" : string.Empty;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();

            if (string.IsNullOrEmpty(ftsQuery))
            {
                // Sparse leg has no search terms (e.g. only punctuation); run pure dense search
                cmd.CommandText = $"""
                    SELECT v.chunk_id, v.document_id, v.chunk_index, v.text,
                           v.start_char_offset, v.end_char_offset, v.metadata_json,
                           cosine_similarity(@queryVec, v.vector) AS score
                    FROM {_options.VectorsTableName} v
                    WHERE v.collection_name = @collection{filterCondition}
                    ORDER BY score DESC
                    LIMIT @topK;
                    """;
                cmd.Parameters.AddWithValue("@queryVec", queryBytes);
                cmd.Parameters.AddWithValue("@collection", _options.CollectionName);
                cmd.Parameters.AddWithValue("@topK", topK);
            }
            else
            {
                // Full single-store hybrid RRF query inside SQLite with pre-filtering
                cmd.CommandText = $"""
                    WITH dense_ranks AS (
                        SELECT v.chunk_id,
                               ROW_NUMBER() OVER (ORDER BY cosine_similarity(@queryVec, v.vector) DESC) AS rank
                        FROM {_options.VectorsTableName} v
                        WHERE v.collection_name = @collection{filterCondition}
                        LIMIT @candidateK
                    ),
                    sparse_ranks AS (
                        SELECT f.chunk_id,
                               ROW_NUMBER() OVER (ORDER BY f.rank) AS rank
                        FROM {_options.FtsTableName} f
                        JOIN {_options.VectorsTableName} v ON v.chunk_id = f.chunk_id
                        WHERE f.{_options.FtsTableName} MATCH @ftsQuery
                          AND f.collection_name = @collection{filterCondition}
                        LIMIT @candidateK
                    ),
                    combined AS (
                        SELECT chunk_id, 1.0 / (@rrfConstant + rank) AS score FROM dense_ranks
                        UNION ALL
                        SELECT chunk_id, 1.0 / (@rrfConstant + rank) AS score FROM sparse_ranks
                    ),
                    fused AS (
                        SELECT chunk_id, SUM(score) AS total_score
                        FROM combined
                        GROUP BY chunk_id
                        ORDER BY total_score DESC
                        LIMIT @topK
                    )
                    SELECT v.chunk_id, v.document_id, v.chunk_index, v.text,
                           v.start_char_offset, v.end_char_offset, v.metadata_json,
                           f.total_score
                    FROM fused f
                    JOIN {_options.VectorsTableName} v ON v.chunk_id = f.chunk_id
                    ORDER BY f.total_score DESC;
                    """;

                cmd.Parameters.AddWithValue("@queryVec", queryBytes);
                cmd.Parameters.AddWithValue("@ftsQuery", ftsQuery);
                cmd.Parameters.AddWithValue("@collection", _options.CollectionName);
                cmd.Parameters.AddWithValue("@candidateK", candidateK);
                cmd.Parameters.AddWithValue("@rrfConstant", _options.RrfConstant);
                cmd.Parameters.AddWithValue("@topK", topK);
            }

            if (useSqlFilter)
            {
                foreach (var (pName, pVal) in compiledFilter!.Parameters)
                {
                    cmd.Parameters.AddWithValue(pName, pVal ?? DBNull.Value);
                }
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
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string FormatFtsQuery(string query)
    {
        var matches = TokenRegex().Matches(query);
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var terms = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            var term = match.Value.Trim();
            if (term.Length >= 2)
            {
                // Escape inner quotes if any and append prefix wildcard for morphological variants
                var safeTerm = term.Replace("\"", "\"\"", StringComparison.Ordinal);
                terms.Add($"\"{safeTerm}\"*");
            }
        }

        return terms.Count > 0 ? string.Join(" OR ", terms) : string.Empty;
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

            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            // Register SIMD Cosine similarity function on this connection
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
                    CREATE TABLE IF NOT EXISTS {_options.VectorsTableName} (
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

                    CREATE INDEX IF NOT EXISTS idx_{_options.VectorsTableName}_collection
                        ON {_options.VectorsTableName}(collection_name);

                    CREATE VIRTUAL TABLE IF NOT EXISTS {_options.FtsTableName} USING fts5(
                        chunk_id UNINDEXED,
                        collection_name UNINDEXED,
                        text,
                        tokenize = 'unicode61'
                    );

                    CREATE TRIGGER IF NOT EXISTS trg_{_options.VectorsTableName}_ai
                    AFTER INSERT ON {_options.VectorsTableName} BEGIN
                        INSERT INTO {_options.FtsTableName}(chunk_id, collection_name, text)
                        VALUES (new.chunk_id, new.collection_name, new.text);
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_{_options.VectorsTableName}_ad
                    AFTER DELETE ON {_options.VectorsTableName} BEGIN
                        DELETE FROM {_options.FtsTableName} WHERE chunk_id = old.chunk_id;
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_{_options.VectorsTableName}_au
                    AFTER UPDATE ON {_options.VectorsTableName} BEGIN
                        DELETE FROM {_options.FtsTableName} WHERE chunk_id = old.chunk_id;
                        INSERT INTO {_options.FtsTableName}(chunk_id, collection_name, text)
                        VALUES (new.chunk_id, new.collection_name, new.text);
                    END;
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
