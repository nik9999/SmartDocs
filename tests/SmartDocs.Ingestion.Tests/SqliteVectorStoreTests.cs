using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;
using SmartDocs.Retrieval.VectorStores;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class SqliteVectorStoreTests
{
    private static readonly float[] VectorX = [1f, 0f, 0f];
    private static readonly float[] VectorY = [0f, 1f, 0f];
    private static readonly float[] VectorDiag = [0.7071f, 0.7071f, 0f];
    private static readonly float[] Vector2D10 = [1f, 0f];
    private static readonly float[] Vector2D01 = [0f, 1f];

    private static DocumentMetadata CreateMetadata(string id, string dept = "Engineering") =>
        new(id, "silo", dept, "Office", "Internal", "doc", 2026, "Author", DateOnly.MinValue, id);

    [Fact]
    public async Task EnsureCollectionExistsAsync_CreatesTableSuccessfully()
    {
        var dbPath = Guid.NewGuid().ToString("N") + ".db";
        var connStr = $"Data Source={dbPath}";

        try
        {
            await using var store = new SqliteVectorStore(connStr, "test-col");
            await store.EnsureCollectionExistsAsync();

            var count = await store.CountAsync();
            Assert.Equal(0, count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.File.Exists(dbPath))
            {
                System.IO.File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task UpsertAndSearchAsync_RanksByCosineSimilarity()
    {
        var memDbName = "mem_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={memDbName};Mode=Memory;Cache=Shared";

        await using var store = new SqliteVectorStore(connStr, "unit-test");
        await store.EnsureCollectionExistsAsync();

        var meta = CreateMetadata("doc-1");

        // Chunk 1: points along x-axis [1, 0, 0]
        var chunk1 = new DocumentChunk("c1", "doc-1", 0, "Chunk about electronics", 0, 10, meta);
        var ec1 = new EmbeddedChunk(chunk1, VectorX, "test-model");

        // Chunk 2: points along y-axis [0, 1, 0]
        var chunk2 = new DocumentChunk("c2", "doc-1", 1, "Chunk about finances", 11, 20, meta);
        var ec2 = new EmbeddedChunk(chunk2, VectorY, "test-model");

        // Chunk 3: points diagonally [0.707, 0.707, 0]
        var chunk3 = new DocumentChunk("c3", "doc-1", 2, "Chunk about general topic", 21, 30, meta);
        var ec3 = new EmbeddedChunk(chunk3, VectorDiag, "test-model");

        await store.UpsertAsync([ec1, ec2, ec3]);

        Assert.Equal(3, await store.CountAsync());

        // Query vector: [1, 0, 0] (identical to Chunk 1, closest to Chunk 3, orthogonal to Chunk 2)
        var results = await store.SearchAsync(VectorX, topK: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("c1", results[0].Chunk.ChunkId);
        Assert.True(results[0].Score > 0.99);

        Assert.Equal("c3", results[1].Chunk.ChunkId);
        Assert.True(results[1].Score > 0.7);

        Assert.Equal("c2", results[2].Chunk.ChunkId);
        Assert.True(results[2].Score < 0.1);
    }

    [Fact]
    public async Task SearchAsync_AppliesMetadataPreFilter()
    {
        var memDbName = "mem_filter_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={memDbName};Mode=Memory;Cache=Shared";

        await using var store = new SqliteVectorStore(connStr, "filter-test");
        await store.EnsureCollectionExistsAsync();

        var metaEng = CreateMetadata("doc-eng", dept: "Engineering");
        var metaHr = CreateMetadata("doc-hr", dept: "HR");

        var cEng = new DocumentChunk("c-eng", "doc-eng", 0, "Engineering spec", 0, 10, metaEng);
        var ecEng = new EmbeddedChunk(cEng, Vector2D10, "model");

        var cHr = new DocumentChunk("c-hr", "doc-hr", 0, "HR leave rules", 0, 10, metaHr);
        var ecHr = new EmbeddedChunk(cHr, Vector2D10, "model");

        await store.UpsertAsync([ecEng, ecHr]);

        // Filter: only HR department
        var hrFilter = MetadataFilter.Where(m => m.Department == "HR");
        var results = await store.SearchAsync(Vector2D10, topK: 5, filter: hrFilter);

        Assert.Single(results);
        Assert.Equal("c-hr", results[0].Chunk.ChunkId);
        Assert.Equal("HR", results[0].Chunk.Metadata.Department);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSpecifiedChunks()
    {
        var memDbName = "mem_del_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={memDbName};Mode=Memory;Cache=Shared";

        await using var store = new SqliteVectorStore(connStr, "delete-test");
        await store.EnsureCollectionExistsAsync();

        var meta = CreateMetadata("doc-del");
        var ec1 = new EmbeddedChunk(new DocumentChunk("del-1", "doc-del", 0, "Text 1", 0, 6, meta), Vector2D10, "model");
        var ec2 = new EmbeddedChunk(new DocumentChunk("del-2", "doc-del", 1, "Text 2", 7, 13, meta), Vector2D01, "model");

        await store.UpsertAsync([ec1, ec2]);
        Assert.Equal(2, await store.CountAsync());

        await store.DeleteAsync(["del-1"]);
        Assert.Equal(1, await store.CountAsync());

        var results = await store.SearchAsync(Vector2D10, topK: 10);
        Assert.Single(results);
        Assert.Equal("del-2", results[0].Chunk.ChunkId);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingChunkOnConflict()
    {
        var memDbName = "mem_upd_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={memDbName};Mode=Memory;Cache=Shared";

        await using var store = new SqliteVectorStore(connStr, "update-test");
        await store.EnsureCollectionExistsAsync();

        var meta = CreateMetadata("doc-upd");
        var ec1 = new EmbeddedChunk(new DocumentChunk("upd-1", "doc-upd", 0, "Original text", 0, 10, meta), Vector2D10, "model");
        await store.UpsertAsync([ec1]);

        var ec1Updated = new EmbeddedChunk(new DocumentChunk("upd-1", "doc-upd", 0, "Updated text", 0, 10, meta), Vector2D01, "model");
        await store.UpsertAsync([ec1Updated]);

        Assert.Equal(1, await store.CountAsync());

        // Searching along [0, 1] should find updated text with high score
        var results = await store.SearchAsync(Vector2D01, topK: 1);
        Assert.Single(results);
        Assert.Equal("Updated text", results[0].Chunk.Text);
        Assert.True(results[0].Score > 0.99);
    }
}
