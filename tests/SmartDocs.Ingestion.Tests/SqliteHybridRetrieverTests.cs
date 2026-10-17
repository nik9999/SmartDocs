using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.DependencyInjection;
using SmartDocs.Retrieval.Hybrid;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class SqliteHybridRetrieverTests
{
    private static readonly float[] VectorA = [1f, 0f, 0f];
    private static readonly float[] VectorB = [0f, 1f, 0f];
    private static readonly float[] VectorC = [0.7071f, 0.7071f, 0f];
    private static readonly float[] VectorZero = [0f, 0f, 1f];

    private static DocumentMetadata CreateMetadata(string id) =>
        new(id, "silo", "Engineering", "Office", "Internal", "doc", 2026, "Author", DateOnly.MinValue, id);

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        private readonly Func<string, float[]> _embedFunc;

        public StubEmbeddingService(Func<string, float[]> embedFunc)
        {
            _embedFunc = embedFunc;
        }

        public string EmbeddingModel => "test-embed-model";
        public int Dimensions => 3;

        public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<ReadOnlyMemory<float>>(_embedFunc(query));

        public IAsyncEnumerable<EmbeddedChunk> EmbedAsync(IAsyncEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task EnsureSchemaExistsAsync_CreatesTablesTriggersAndFtsIndex()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_schema_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";
        var embedder = new StubEmbeddingService(_ => VectorA);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);
            await retriever.EnsureSchemaExistsAsync();

            // Verify SQLite schema tables
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'trigger') ORDER BY name;";

            var names = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
            }

            Assert.Contains("smartdocs_vectors", names);
            Assert.Contains("smartdocs_fts", names);
            Assert.Contains("trg_smartdocs_vectors_ai", names);
            Assert.Contains("trg_smartdocs_vectors_ad", names);
            Assert.Contains("trg_smartdocs_vectors_au", names);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task IndexChunksAsync_SyncsToBothVectorTableAndFtsVirtualTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_index_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";
        var embedder = new StubEmbeddingService(_ => VectorA);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);

            var meta = CreateMetadata("doc-1");
            var chunk1 = new DocumentChunk("c1", "doc-1", 0, "Первый чанк про электродвигатели и частотные преобразователи", 0, 60, meta);
            var chunk2 = new DocumentChunk("c2", "doc-1", 1, "Второй чанк про регламент проведения отпуска и командировок", 61, 120, meta);

            var ec1 = new EmbeddedChunk(chunk1, VectorA, "test-embed-model");
            var ec2 = new EmbeddedChunk(chunk2, VectorB, "test-embed-model");

            await retriever.IndexChunksAsync([ec1, ec2]);

            // Query FTS table directly to verify trigger sync
            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM smartdocs_fts WHERE smartdocs_fts MATCH 'электродвигатели';";
            var matchCount = (long)(await cmd.ExecuteScalarAsync())!;

            Assert.Equal(1L, matchCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task RetrieveAsync_FusesDenseAndSparseRanksViaRrf()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_rrf_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        // When query mentions "трансформатор", query vector points along VectorA [1, 0, 0]
        var embedder = new StubEmbeddingService(q => VectorA);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);

            var meta = CreateMetadata("doc-specs");

            // Chunk 1: High semantic similarity (VectorA), AND has exact keyword match "трансформатор"
            // Both dense and sparse rank = 1 -> RRF score = 1/(60+1) + 1/(60+1) = 2/61 ≈ 0.0328
            var c1 = new DocumentChunk("c1", "doc-specs", 0, "Силовой трансформатор с масляным охлаждением", 0, 50, meta);
            var ec1 = new EmbeddedChunk(c1, VectorA, "model");

            // Chunk 2: High semantic similarity (VectorC = [0.707, 0.707, 0]), but NO keyword match
            // Dense rank = 2, Sparse rank = none -> RRF score = 1/(60+2) = 1/62 ≈ 0.0161
            var c2 = new DocumentChunk("c2", "doc-specs", 1, "Электротехническое оборудование высокого напряжения", 51, 100, meta);
            var ec2 = new EmbeddedChunk(c2, VectorC, "model");

            // Chunk 3: Zero semantic similarity (VectorZero = [0, 0, 1]), but HAS keyword match "трансформатор"
            // Dense rank = 3 (or 4), Sparse rank = 2 -> RRF score = 1/(60+sparse) + 1/(60+dense)
            var c3 = new DocumentChunk("c3", "doc-specs", 2, "Инструкция по утилизации старых трансформаторов", 101, 150, meta);
            var ec3 = new EmbeddedChunk(c3, VectorZero, "model");

            // Chunk 4: Irrelevant (VectorB = [0, 1, 0], orthogonal to VectorA, no keyword match)
            var c4 = new DocumentChunk("c4", "doc-specs", 3, "Порядок расчета отпускных выплат сотрудникам", 151, 200, meta);
            var ec4 = new EmbeddedChunk(c4, VectorB, "model");

            await retriever.IndexChunksAsync([ec1, ec2, ec3, ec4]);

            var results = await retriever.RetrieveAsync("силовой трансформатор", topK: 3);

            Assert.NotEmpty(results);
            Assert.True(results.Count <= 3);

            // c1 wins because it is ranked top in BOTH dense and sparse legs!
            Assert.Equal("c1", results[0].Chunk.ChunkId);
            Assert.True(results[0].Score > results[1].Score);

            // Verify strategy name
            Assert.Equal("hybrid-sqlite", retriever.Strategy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task RetrieveAsync_HandlesRussianPrefixMorphology()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_morph_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";
        var embedder = new StubEmbeddingService(_ => VectorZero);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);

            var meta = CreateMetadata("doc-words");
            // The document contains word form "преобразователями"
            var c1 = new DocumentChunk("c1", "doc-words", 0, "Шкаф управления с частотными преобразователями", 0, 50, meta);
            await retriever.IndexChunksAsync([new EmbeddedChunk(c1, VectorZero, "model")]);

            // Query using word form "преобразователь" -> FTS5 prefix match "преобразователь"* matches "преобразователями"
            var results = await retriever.RetrieveAsync("преобразователь", topK: 1);

            Assert.Single(results);
            Assert.Equal("c1", results[0].Chunk.ChunkId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task RetrieveAsync_PureDenseFallbackWhenQueryHasNoFtsTerms()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_fallback_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";
        var embedder = new StubEmbeddingService(_ => VectorA);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);

            var meta = CreateMetadata("doc-punct");
            var c1 = new DocumentChunk("c1", "doc-punct", 0, "Некоторый текст документа", 0, 30, meta);
            await retriever.IndexChunksAsync([new EmbeddedChunk(c1, VectorA, "model")]);

            // Query consists solely of punctuation / single chars which produce empty FTS term list
            var results = await retriever.RetrieveAsync("... ??? !!!", topK: 1);

            Assert.Single(results);
            Assert.Equal("c1", results[0].Chunk.ChunkId);
            Assert.True(results[0].Score > 0.99); // Dense cosine similarity with VectorA
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public void DependencyInjection_RegistersSqliteHybridRetriever()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingService>(new StubEmbeddingService(_ => VectorA));
        services.AddSmartDocsSqliteHybridRetriever("Data Source=:memory:", "test-collection");

        using var provider = services.BuildServiceProvider();
        var retriever = provider.GetService<IRetriever>();

        Assert.NotNull(retriever);
        Assert.IsType<SqliteHybridRetriever>(retriever);
        Assert.Equal("hybrid-sqlite", retriever.Strategy);
    }
}
