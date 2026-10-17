using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;
using SmartDocs.Retrieval.Filtering;
using SmartDocs.Retrieval.Hybrid;
using SmartDocs.Retrieval.VectorStores;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class SqliteFilterCompilerTests
{
    private static DocumentMetadata CreateMetadata(string id, string dept = "Engineering", int year = 2026, string title = "Doc") =>
        new(id, "silo", dept, "Office", "Internal", "doc", year, "Author", DateOnly.MinValue, title);

    [Fact]
    public void TryCompile_EqualityAndInequality_ProducesCorrectSqlAndParameters()
    {
        var filter = MetadataFilter.Where(m => m.Department == "Engineering" && m.Office != "Paris");
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(metadata_json, '$.Department') =", compiled.SqlWhere);
        Assert.Contains("json_extract(metadata_json, '$.Office') !=", compiled.SqlWhere);
        Assert.Equal(2, compiled.Parameters.Count);
        Assert.Contains("Engineering", compiled.Parameters.Values);
        Assert.Contains("Paris", compiled.Parameters.Values);
    }

    [Fact]
    public void TryCompile_NullChecks_ProducesIsNull()
    {
        var filter = MetadataFilter.Where(m => m.Author == null && m.Title != null);
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(metadata_json, '$.Author') IS NULL", compiled.SqlWhere);
        Assert.Contains("json_extract(metadata_json, '$.Title') IS NOT NULL", compiled.SqlWhere);
        Assert.Empty(compiled.Parameters);
    }

    [Fact]
    public void TryCompile_FiscalYear_CastsToInteger()
    {
        var filter = MetadataFilter.Where(m => m.FiscalYear >= 2024 && m.FiscalYear < 2027);
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.NotNull(compiled);
        Assert.Contains("CAST(json_extract(metadata_json, '$.FiscalYear') AS INTEGER) >=", compiled.SqlWhere);
        Assert.Contains("CAST(json_extract(metadata_json, '$.FiscalYear') AS INTEGER) <", compiled.SqlWhere);
        Assert.Equal(2, compiled.Parameters.Count);
        Assert.Contains(2024, compiled.Parameters.Values);
        Assert.Contains(2027, compiled.Parameters.Values);
    }

    [Fact]
    public void TryCompile_StringMethods_ProducesLikeWithEscape()
    {
        var filter = MetadataFilter.Where(m => m.Title.Contains("Архитектура") && m.Department.StartsWith("Инж"));
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(metadata_json, '$.Title') LIKE", compiled.SqlWhere);
        Assert.Contains("json_extract(metadata_json, '$.Department') LIKE", compiled.SqlWhere);
        Assert.Contains("ESCAPE '\\'", compiled.SqlWhere);

        Assert.Contains("%Архитектура%", compiled.Parameters.Values);
        Assert.Contains("Инж%", compiled.Parameters.Values);
    }

    [Fact]
    public void TryCompile_SetMembership_ProducesInClause()
    {
        string[] allowedDepts = ["Engineering", "HR", "Legal"];
        var filter = MetadataFilter.Where(m => allowedDepts.Contains(m.Department));
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(metadata_json, '$.Department') IN (", compiled.SqlWhere);
        Assert.Equal(3, compiled.Parameters.Count);
        Assert.Contains("Engineering", compiled.Parameters.Values);
        Assert.Contains("HR", compiled.Parameters.Values);
        Assert.Contains("Legal", compiled.Parameters.Values);
    }

    [Fact]
    public void TryCompile_ComposedFilters_InlinesLambdasCorrectly()
    {
        var f1 = MetadataFilter.Where(m => m.Department == "Engineering");
        var f2 = MetadataFilter.Where(m => m.FiscalYear == 2026);
        var combined = f1.And(f2);

        var compiled = SqliteFilterCompiler.TryCompile(combined);

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(metadata_json, '$.Department') =", compiled.SqlWhere);
        Assert.Contains("CAST(json_extract(metadata_json, '$.FiscalYear') AS INTEGER) =", compiled.SqlWhere);
        Assert.Contains("AND", compiled.SqlWhere);
    }

    [Fact]
    public void TryCompile_TableAlias_PrefixesJsonColumn()
    {
        var filter = MetadataFilter.Where(m => m.Department == "HR");
        var compiled = SqliteFilterCompiler.TryCompile(filter, tableAlias: "v");

        Assert.NotNull(compiled);
        Assert.Contains("json_extract(v.metadata_json, '$.Department') =", compiled.SqlWhere);
    }

    [Fact]
    public void TryCompile_UnsupportedExpression_ReturnsNull()
    {
        // Calling a custom method that cannot be mapped to SQL
        var filter = MetadataFilter.Where(m => CustomEvaluation(m.Department));
        var compiled = SqliteFilterCompiler.TryCompile(filter);

        Assert.Null(compiled);
    }

    private static bool CustomEvaluation(string val) => val.Length > 2;

    [Fact]
    public async Task SqliteVectorStore_SearchAsync_ExecutesDatabasePreFilter()
    {
        var memDbName = "mem_prefilter_" + Guid.NewGuid().ToString("N");
        var connStr = $"Data Source={memDbName};Mode=Memory;Cache=Shared";

        await using var store = new SqliteVectorStore(connStr, "filter-test");
        await store.EnsureCollectionExistsAsync();

        var metaEng2024 = CreateMetadata("doc-1", dept: "Engineering", year: 2024);
        var metaEng2026 = CreateMetadata("doc-2", dept: "Engineering", year: 2026);
        var metaHr2026 = CreateMetadata("doc-3", dept: "HR", year: 2026);

        float[] vec = [1f, 0f, 0f];

        var c1 = new EmbeddedChunk(new DocumentChunk("c1", "doc-1", 0, "Eng 2024", 0, 10, metaEng2024), vec, "m");
        var c2 = new EmbeddedChunk(new DocumentChunk("c2", "doc-2", 0, "Eng 2026", 0, 10, metaEng2026), vec, "m");
        var c3 = new EmbeddedChunk(new DocumentChunk("c3", "doc-3", 0, "HR 2026", 0, 10, metaHr2026), vec, "m");

        await store.UpsertAsync([c1, c2, c3]);

        // Filter: Department == "Engineering" AND FiscalYear >= 2025
        var filter = MetadataFilter.Where(m => m.Department == "Engineering" && m.FiscalYear >= 2025);
        var results = await store.SearchAsync(vec, topK: 10, filter: filter);

        Assert.Single(results);
        Assert.Equal("c2", results[0].Chunk.ChunkId);
        Assert.Equal(2026, results[0].Chunk.Metadata.FiscalYear);
    }

    [Fact]
    public async Task SqliteHybridRetriever_RetrieveAsync_WithFilter_FiltersResults()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hybrid_filter_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        float[] vec = [1f, 0f, 0f];
        var embedder = new StubEmbedder(vec);

        try
        {
            await using var retriever = new SqliteHybridRetriever(embedder, connStr);

            var metaEng = CreateMetadata("doc-eng", dept: "Engineering", title: "Трансформатор");
            var metaLegal = CreateMetadata("doc-legal", dept: "Legal", title: "Трансформатор");

            var cEng = new DocumentChunk("c-eng", "doc-eng", 0, "Трансформатор силовой инженерный", 0, 30, metaEng);
            var cLegal = new DocumentChunk("c-legal", "doc-legal", 0, "Трансформатор силовой договорной", 0, 30, metaLegal);

            await retriever.IndexChunksAsync([
                new EmbeddedChunk(cEng, vec, "m"),
                new EmbeddedChunk(cLegal, vec, "m")
            ]);

            // Query with filter for Legal department only
            var filter = MetadataFilter.Where(m => m.Department == "Legal");
            var results = await retriever.RetrieveAsync("трансформатор", topK: 5, filter: filter);

            Assert.Single(results);
            Assert.Equal("c-legal", results[0].Chunk.ChunkId);
            Assert.Equal("Legal", results[0].Chunk.Metadata.Department);
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

    private sealed class StubEmbedder(float[] vec) : IEmbeddingService
    {
        public string EmbeddingModel => "test";
        public int Dimensions => vec.Length;

        public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<ReadOnlyMemory<float>>(vec);

        public IAsyncEnumerable<EmbeddedChunk> EmbedAsync(IAsyncEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
