namespace Rag.Application.Generation;

using Core.Contracts;
using Core.Documents;
using Core.Generation;
using Core.Retrieval;
using Documents;
using Retrieval;

/// <summary>
/// Orchestrates the full RAG pipeline: retrieval → context → prompt → generation.
/// </summary>
public sealed class RagService : IRagService
{
    private readonly IRetrievalService _retrievalService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IGigaChatService _gigaChatService;

    public RagService(
        IRetrievalService retrievalService,
        IPromptBuilder promptBuilder,
        IGigaChatService gigaChatService)
    {
        _retrievalService = retrievalService;
        _promptBuilder = promptBuilder;
        _gigaChatService = gigaChatService;
    }

    public async Task<RagResponse> AskAsync(
        string query,
        CancellationToken cancellationToken)
    {
        // 1. Retrieve relevant chunks
        var retrievalQuery = new RetrievalQuery(query, topK: 10);
        var results = await _retrievalService.SearchAsync(retrievalQuery, cancellationToken)
            .ConfigureAwait(false);

        // 2. Build deterministic citations
        var citations = new List<Citation>();
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            citations.Add(new Citation(
                citationId: i + 1,
                documentId: result.DocumentId,
                chunkId: result.ChunkId,
                text: result.Text,
                source: result.Source.ToString()));
        }

        // 3. Build RAG context
        var ragContext = new RagContext(
            query: query,
            results: results,
            citations: citations);

        // 4. Build prompt from context
        var prompt = _promptBuilder.Build(ragContext);

        // 5. Generate response
        var answer = await _gigaChatService.GenerateAsync(prompt, cancellationToken)
            .ConfigureAwait(false);

        // 6. Return final response with citations
        return new RagResponse(
            answer: answer,
            citations: citations,
            query: query);
    }
}
