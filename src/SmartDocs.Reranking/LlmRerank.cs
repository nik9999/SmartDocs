using Microsoft.Extensions.AI;
using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// LLM-as-judge reranker — asks an <see cref="IChatClient"/> to score each
/// (query, candidate) pair on a 0..1 relevance scale, then re-sorts. Slower
/// and pricier per query than a dedicated cross-encoder, but useful when no
/// rerank API key is available and a chat model (Ollama llama3.2 fits) is
/// reachable. For a genuine self-hosted cross-encoder, see
/// <see cref="OnnxCrossEncoderReranker"/> backed by an
/// <see cref="ICrossEncoderModel"/> (Ch 9, ONNX adapter).
/// </summary>
public sealed class LlmRerank : IReranker
{
    private readonly IChatClient _chat;

    public LlmRerank(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
    }

    public string Implementation => "llm-as-judge";

    public async Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var rescored = new List<RetrievalResult>(candidates.Count);
        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt =
                $"Оцените, насколько фрагмент текста отвечает на поставленный вопрос, " +
                $"по шкале от 0.0 (не имеет отношения к вопросу) до 1.0 (идеальный исчерпывающий ответ). " +
                $"В ответе напишите только одно вещественное число от 0.0 до 1.0, без пояснений и дополнительного текста.\n\n" +
                $"Вопрос: {query}\n\nФрагмент текста: {c.Chunk.Text}";
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            var raw = (response.Text ?? "0").Trim().Replace(',', '.');
            // Try to extract the first number; default to 0.5 on parse failure.
            var score = TryParseScore(raw, defaultScore: 0.5);
            rescored.Add(new RetrievalResult(c.Chunk, score));
        }

        return [.. rescored.OrderByDescending(r => r.Score).Take(topK)];
    }

    private static double TryParseScore(string raw, double defaultScore)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            if (char.IsDigit(raw[i]) || raw[i] == '.')
            {
                int end = i;
                while (end < raw.Length && (char.IsDigit(raw[end]) || raw[end] == '.'))
                {
                    end++;
                }
                if (double.TryParse(raw[i..end],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v))
                {
                    return Math.Clamp(v, 0.0, 1.0);
                }
                break;
            }
        }
        return defaultScore;
    }
}
