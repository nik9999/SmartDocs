using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval.Decorators;

/// <summary>
/// Step-Back Prompting (Zheng et al., 2023) — before retrieving, ask the chat
/// model for ONE more-general "step-back" question that abstracts away the
/// specifics of the user's query. We then retrieve on BOTH the original query
/// and the broader step-back query and fuse the two ranked lists with RRF.
/// The step-back leg surfaces high-level / principle-level passages that a
/// narrow literal query often misses, while the original leg keeps the
/// specifics — the fusion gets the best of both.
/// </summary>
public sealed class StepBackRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IChatClient _chat;
    private readonly RrfMerger _merger;

    private const string Prompt =
        "Вы эксперт по структурированию знаний. Ваша задача — сделать «шаг назад» и " +
        "перефразировать вопрос в ОДИН более общий, концептуальный вопрос на русском языке, " +
        "затрагивающий основополагающие принципы или базовые понятия темы. " +
        "Ответьте строго на русском языке, сформулировав только сам обобщенный вопрос, без вводных слов и пояснений.\n\n" +
        "Вопрос: {0}";

    public StepBackRetriever(IRetriever inner, IChatClient chat, RrfMerger? merger = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(chat);
        _inner = inner;
        _chat = chat;
        _merger = merger ?? new RrfMerger();
    }

    public string Strategy => $"stepback({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var prompt = Prompt.Replace("{0}", query, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var stepBack = (response.Text ?? string.Empty).Trim();

        // Pull a deeper candidate pool per leg so the fusion has room to work.
        var candidateK = Math.Max(topK * 2, topK + 5);
        var original = await _inner.RetrieveAsync(query, candidateK, cancellationToken).ConfigureAwait(false);

        // If the model returned nothing usable, fall back to the original leg
        // alone rather than re-issuing the same query as a degenerate "step-back".
        if (string.IsNullOrEmpty(stepBack) ||
            string.Equals(stepBack, query, StringComparison.OrdinalIgnoreCase))
        {
            return [.. original.Take(topK)];
        }

        var broader = await _inner.RetrieveAsync(stepBack, candidateK, cancellationToken).ConfigureAwait(false);
        return _merger.Merge(original, broader, topK);
    }
}
