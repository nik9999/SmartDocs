using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval.Decorators;

/// <summary>
/// HyDE (Hypothetical Document Embeddings) — generate a hypothetical
/// answer to the query first, then retrieve neighbors of that hypothetical
/// answer's text rather than the bare query. Helps when the query is short
/// or ambiguous and the corpus contains long-form passages.
/// </summary>
public sealed class HydeRetriever : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IChatClient _chat;

    private const string Prompt =
        "Напишите краткий гипотетический ответ (2-3 предложения) на следующий вопрос на русском языке. " +
        "Ответу не обязательно быть фактически точным — главное, чтобы по стилю и лексике он соответствовал " +
        "фрагменту статьи из технической базы знаний. Отвечайте строго на русском языке, только текстом фрагмента, без вводных слов и пояснений.\n\n" +
        "Вопрос: {0}";

    public HydeRetriever(IRetriever inner, IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(chat);
        _inner = inner;
        _chat = chat;
    }

    public string Strategy => $"hyde({_inner.Strategy})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var prompt = Prompt.Replace("{0}", query, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var hypothetical = (response.Text ?? string.Empty).Trim();
        var searchText = string.IsNullOrEmpty(hypothetical) ? query : hypothetical;
        return await _inner.RetrieveAsync(searchText, topK, cancellationToken).ConfigureAwait(false);
    }
}
