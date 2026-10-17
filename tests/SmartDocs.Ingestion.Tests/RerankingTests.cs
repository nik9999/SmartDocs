using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.Documents;
using SmartDocs.Reranking;
using SmartDocs.Reranking.DependencyInjection;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class RerankingTests
{
    private static DocumentMetadata CreateMetadata(string id) =>
        new(id, "silo", "Engineering", "Office", "Internal", "doc", 2026, "Author", DateOnly.MinValue, id);

    private static RetrievalResult CreateResult(string id, string text, double score) =>
        new(new DocumentChunk(id, "doc-1", 0, text, 0, text.Length, CreateMetadata(id)), score);

    [Fact]
    public async Task NoOpReranker_PreservesOriginalOrderAndTruncatesToTopK()
    {
        var reranker = new NoOpReranker();
        Assert.Equal("no-op", reranker.Implementation);

        var candidates = new List<RetrievalResult>
        {
            CreateResult("c1", "First text", 0.3),
            CreateResult("c2", "Second text", 0.9),
            CreateResult("c3", "Third text", 0.1),
        };

        var results = await reranker.RerankAsync("query", candidates, topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("c1", results[0].Chunk.ChunkId);
        Assert.Equal("c2", results[1].Chunk.ChunkId);
    }

    private sealed class StubCrossEncoderModel(Func<string, IReadOnlyList<string>, IReadOnlyList<float>> scorer) : ICrossEncoderModel
    {
        public string ModelId => "stub-cross-encoder";
        public IReadOnlyList<float> Score(string query, IReadOnlyList<string> documents) => scorer(query, documents);
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_ReordersCandidatesByModelScore()
    {
        var model = new StubCrossEncoderModel((query, docs) =>
        {
            // docs[0] -> 0.2, docs[1] -> 0.95, docs[2] -> 0.6
            return [0.2f, 0.95f, 0.6f];
        });

        var reranker = new OnnxCrossEncoderReranker(model);
        Assert.Equal("onnx-cross-encoder(stub-cross-encoder)", reranker.Implementation);

        var candidates = new List<RetrievalResult>
        {
            CreateResult("c1", "Text 1", 0.5),
            CreateResult("c2", "Text 2", 0.5),
            CreateResult("c3", "Text 3", 0.5),
        };

        var results = await reranker.RerankAsync("query", candidates, topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("c2", results[0].Chunk.ChunkId);
        Assert.Equal(0.95, results[0].Score, precision: 2);
        Assert.Equal("c3", results[1].Chunk.ChunkId);
        Assert.Equal(0.6, results[1].Score, precision: 2);
    }

    private sealed class MockChatClient(Func<string, string> responseFunc) : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("mock");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var lastUserMsg = chatMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
            var reply = responseFunc(lastUserMsg);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task LlmRerank_ScoresAndReorders_HandlesCommaAndDot()
    {
        var chat = new MockChatClient(prompt =>
        {
            // Verify prompt is in Russian
            Assert.Contains("Оцените, насколько фрагмент текста отвечает", prompt);

            if (prompt.Contains("Пассаж 1", StringComparison.Ordinal))
            {
                return "Оценка: 0,85"; // Russian comma format
            }
            if (prompt.Contains("Пассаж 2", StringComparison.Ordinal))
            {
                return "0.3"; // Standard dot format
            }
            return "0.95";
        });

        var reranker = new LlmRerank(chat);
        Assert.Equal("llm-as-judge", reranker.Implementation);

        var candidates = new List<RetrievalResult>
        {
            CreateResult("c1", "Пассаж 1", 0.1),
            CreateResult("c2", "Пассаж 2", 0.1),
            CreateResult("c3", "Пассаж 3", 0.1),
        };

        var results = await reranker.RerankAsync("вопрос", candidates, topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("c3", results[0].Chunk.ChunkId);
        Assert.Equal(0.95, results[0].Score, precision: 2);
        Assert.Equal("c1", results[1].Chunk.ChunkId);
        Assert.Equal(0.85, results[1].Score, precision: 2);
    }

    private sealed class StubRetriever(Func<string, int, IReadOnlyList<RetrievalResult>> handler) : IRetriever
    {
        public string Strategy => "stub-retriever";

        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
            string query,
            int topK,
            CancellationToken cancellationToken = default)
            => Task.FromResult(handler(query, topK));
    }

    [Fact]
    public async Task RerankingMiddleware_RetrievesBroadCandidateCountAndReranks()
    {
        int requestedCandidateK = 0;
        var inner = new StubRetriever((query, topK) =>
        {
            requestedCandidateK = topK;
            return
            [
                CreateResult("c1", "Text 1", 0.1),
                CreateResult("c2", "Text 2", 0.2),
                CreateResult("c3", "Text 3", 0.3),
                CreateResult("c4", "Text 4", 0.4),
                CreateResult("c5", "Text 5", 0.5),
            ];
        });

        var model = new StubCrossEncoderModel((q, docs) => [0.1f, 0.9f, 0.3f, 0.8f, 0.2f]);
        var reranker = new OnnxCrossEncoderReranker(model);

        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 5, minScore: 0.0);
        Assert.Equal("stub-retriever+rerank(onnx-cross-encoder(stub-cross-encoder))", middleware.Strategy);

        var results = await middleware.RetrieveAsync("query", topK: 2);

        Assert.Equal(5, requestedCandidateK);
        Assert.Equal(2, results.Count);
        Assert.Equal("c2", results[0].Chunk.ChunkId);
        Assert.Equal(0.9, results[0].Score, precision: 2);
        Assert.Equal("c4", results[1].Chunk.ChunkId);
        Assert.Equal(0.8, results[1].Score, precision: 2);
    }

    [Fact]
    public async Task RerankingMiddleware_FiltersOutResultsBelowMinScoreFloor()
    {
        var inner = new StubRetriever((query, topK) =>
        [
            CreateResult("c1", "Text 1", 0.1),
            CreateResult("c2", "Text 2", 0.2),
            CreateResult("c3", "Text 3", 0.3),
        ]);

        var model = new StubCrossEncoderModel((q, docs) => [0.85f, 0.45f, 0.2f]);
        var reranker = new OnnxCrossEncoderReranker(model);

        // MinScore = 0.5 drops candidates below 0.5
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 3, minScore: 0.5);
        var results = await middleware.RetrieveAsync("query", topK: 3);

        Assert.Single(results);
        Assert.Equal("c1", results[0].Chunk.ChunkId);
        Assert.Equal(0.85, results[0].Score, precision: 2);

        // If minScore = 0.9, none clear the bar -> returns empty list so generator can abstain
        var strictMiddleware = new RerankingMiddleware(inner, reranker, candidateCount: 3, minScore: 0.9);
        var strictResults = await strictMiddleware.RetrieveAsync("query", topK: 3);
        Assert.Empty(strictResults);
    }

    [Fact]
    public void DependencyInjection_DecoratesExistingRetriever()
    {
        var services = new ServiceCollection();

        services.Configure<SmartDocsOptions>(opt =>
        {
            opt.Reranking.Mode = "none";
            opt.Reranking.CandidateCount = 15;
            opt.Reranking.MinScore = 0.2;
        });

        // Register initial retriever
        services.AddSingleton<IRetriever>(new StubRetriever((_, _) => []));

        // Add reranking middleware
        services.AddSmartDocsReranking();

        using var provider = services.BuildServiceProvider();
        var retriever = provider.GetRequiredService<IRetriever>();

        Assert.IsType<RerankingMiddleware>(retriever);
        var mw = (RerankingMiddleware)retriever;
        Assert.Equal(15, mw.CandidateCount);
        Assert.Equal(0.2, mw.MinScore);
        Assert.Contains("+rerank(no-op)", mw.Strategy);
    }
}
