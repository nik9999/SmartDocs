using Rag.Application.Generation;
using Rag.Application.Retrieval;
using Rag.Core.Contracts;
using Rag.Core.Generation;
using Rag.Core.Retrieval;
using Xunit;

namespace Rag.Application.Tests;

public class RagServiceTests
{
    private sealed class FakeRetrievalService : IRetrievalService
    {
        public RetrievalQuery? LastQuery { get; private set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            RetrievalQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(
                new List<RetrievalResult>
                {
                    new RetrievalResult(
                        Guid.NewGuid(), Guid.NewGuid(), "chunk text", 0.9, 1, RetrievalSource.Hybrid)
                });
        }
    }

    private sealed class FakePromptBuilder : IPromptBuilder
    {
        public RagContext? LastContext { get; private set; }
        public string? LastBuiltPrompt { get; private set; }

        public string Build(RagContext context)
        {
            LastContext = context;
            LastBuiltPrompt = $"Prompt for: {context.Query}";
            return LastBuiltPrompt;
        }
    }

    private sealed class FakeGigaChatService : IGigaChatService
    {
        public string? LastPrompt { get; private set; }

        public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult("Generated answer");
        }
    }

    [Fact]
    public async Task AskAsync_CallsRetrievalService()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        await service.AskAsync("What is RAG?", CancellationToken.None);

        // Assert
        Assert.Equal(1, retrieval.CallCount);
        Assert.Equal("What is RAG?", retrieval.LastQuery!.Text);
    }

    [Fact]
    public async Task AskAsync_CreatesContextWithQuery()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        await service.AskAsync("test query", CancellationToken.None);

        // Assert
        Assert.NotNull(promptBuilder.LastContext);
        Assert.Equal("test query", promptBuilder.LastContext.Query);
    }

    [Fact]
    public async Task AskAsync_CreatesDeterministicCitations()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        await service.AskAsync("test", CancellationToken.None);

        // Assert
        Assert.NotNull(promptBuilder.LastContext);
        Assert.Single(promptBuilder.LastContext.Citations);
        Assert.Equal(1, promptBuilder.LastContext.Citations[0].CitationId);
    }

    [Fact]
    public async Task AskAsync_PassesContextToPromptBuilder()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        await service.AskAsync("query", CancellationToken.None);

        // Assert
        Assert.NotNull(promptBuilder.LastContext);
        Assert.NotNull(promptBuilder.LastBuiltPrompt);
    }

    [Fact]
    public async Task AskAsync_PassesPromptToGenerationService()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        await service.AskAsync("query", CancellationToken.None);

        // Assert
        Assert.Equal(promptBuilder.LastBuiltPrompt, generation.LastPrompt);
    }

    [Fact]
    public async Task AskAsync_ReturnsRagResponseWithAnswer()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        var response = await service.AskAsync("query", CancellationToken.None);

        // Assert
        Assert.Equal("Generated answer", response.Answer);
    }

    [Fact]
    public async Task AskAsync_ReturnsRagResponseWithCitations()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        var response = await service.AskAsync("query", CancellationToken.None);

        // Assert
        Assert.NotNull(response.Citations);
        Assert.Single(response.Citations);
    }

    [Fact]
    public async Task AskAsync_ReturnsRagResponseWithQuery()
    {
        // Arrange
        var retrieval = new FakeRetrievalService();
        var promptBuilder = new FakePromptBuilder();
        var generation = new FakeGigaChatService();
        var service = new RagService(retrieval, promptBuilder, generation);

        // Act
        var response = await service.AskAsync("original query", CancellationToken.None);

        // Assert
        Assert.Equal("original query", response.Query);
    }
}
