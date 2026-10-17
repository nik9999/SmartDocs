using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// A genuine cross-encoder reranker — it delegates scoring to an
/// <see cref="ICrossEncoderModel"/> (the real model lives behind the opt-in
/// <c>SmartDocs.Reranking.Onnx</c> adapter) and re-sorts candidates by the
/// model's relevance score. Unlike <see cref="LlmRerank"/>, scoring is a single
/// batched, CPU-bound inference pass rather than one chat call per candidate.
/// <para>
/// The class is deliberately model-agnostic: it depends only on the
/// <see cref="ICrossEncoderModel"/> port, so it is fully unit-testable with a
/// stub model and carries no native dependency itself.
/// </para>
/// </summary>
public sealed class OnnxCrossEncoderReranker : IReranker
{
    private readonly ICrossEncoderModel _model;

    public OnnxCrossEncoderReranker(ICrossEncoderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    public string Implementation => $"onnx-cross-encoder({_model.ModelId})";

    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        cancellationToken.ThrowIfCancellationRequested();

        if (candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        // Inference is synchronous and CPU-bound, so there is nothing to await.
        var texts = candidates.Select(c => c.Chunk.Text).ToList();
        var scores = _model.Score(query, texts);

        IReadOnlyList<RetrievalResult> reranked =
        [
            .. candidates
                .Select((c, i) => new RetrievalResult(c.Chunk, scores[i]))
                .OrderByDescending(r => r.Score)
                .Take(topK)
        ];

        return Task.FromResult(reranked);
    }
}
