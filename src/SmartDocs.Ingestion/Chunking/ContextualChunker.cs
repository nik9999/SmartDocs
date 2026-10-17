using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Chunking;

/// <summary>
/// Anthropic Contextual Retrieval (late-2024): wraps any inner
/// <see cref="IChunker"/> and prepends a one-sentence LLM-generated context
/// describing how the chunk fits the source document. Reproduces the
/// 49% (top-20) / 67% (top-20 + rerank) retrieval-failure reduction reported
/// in the original blog post.
///
/// The chunk's offsets and ID still reference the *original* extracted
/// text; only the <see cref="DocumentChunk.Text"/> field is augmented.
/// Citations therefore continue to point at the right slice of the source.
/// </summary>
public sealed class ContextualChunker : IChunker
{
    private readonly IChunker _inner;
    private readonly IChatClient _chat;

    private static readonly System.Text.CompositeFormat PromptTemplate =
        System.Text.CompositeFormat.Parse(
        """
        <document>
        {0}
        </document>

        Ниже приведен фрагмент (чанк), который необходимо соотнести с контекстом всего документа:
        <chunk>
        {1}
        </chunk>

        Сформулируйте краткий ситуационный контекст (одно-два предложения) на русском языке, объясняющий, к чему относится данный фрагмент в рамках документа, для улучшения качества поисковой выдачи. Отвечайте строго на русском языке, формулируя только сам контекст и ничего лишнего.
        """);

    public ContextualChunker(IChunker inner, IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(chat);
        _inner = inner;
        _chat = chat;
    }

    public string Strategy => $"contextual({_inner.Strategy})";

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var docText = document.Content;
        await foreach (var raw in _inner.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contextualDoc = GetContextualDocumentText(docText, raw.StartCharOffset, raw.EndCharOffset);
            var prompt = string.Format(System.Globalization.CultureInfo.InvariantCulture, PromptTemplate, contextualDoc, raw.Text);
            // ^ CompositeFormat-based overload selected by passing PromptTemplate as the format argument.
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            var context = (response.Text ?? string.Empty).Trim();

            var augmented = string.IsNullOrEmpty(context)
                ? raw.Text
                : $"{context}\n\n{raw.Text}";
            yield return raw with { Text = augmented };
        }
    }

    private static string GetContextualDocumentText(string docText, int chunkStart, int chunkEnd, int maxContextChars = 8000)
    {
        if (docText.Length <= maxContextChars)
        {
            return docText;
        }

        // For large documents: take header (title + TOC: first ~2500 chars) + surrounding window (~5500 chars)
        int headerLen = Math.Min(2500, docText.Length);
        var header = docText[..headerLen];

        int windowRadius = (maxContextChars - headerLen) / 2;
        int winStart = Math.Max(0, chunkStart - windowRadius);
        int winEnd = Math.Min(docText.Length, chunkEnd + windowRadius);

        if (winStart <= headerLen)
        {
            return docText[..Math.Min(docText.Length, maxContextChars)];
        }

        return $"{header}\n\n[...]\n\n{docText[winStart..winEnd]}";
    }
}
