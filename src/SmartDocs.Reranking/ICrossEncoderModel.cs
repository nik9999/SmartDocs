namespace SmartDocs.Reranking;

/// <summary>
/// A self-hosted cross-encoder relevance model. Given a query and a batch of
/// candidate document texts, returns one relevance score in <c>[0, 1]</c> per
/// document, aligned to the input order.
/// <para>
/// Implementations own the heavy lifting: tokenizing each (query, document)
/// pair, batching the pairs through the underlying model, and mapping the raw
/// per-pair logit to a probability with a sigmoid so every returned score lands
/// in <c>[0, 1]</c>. The returned list MUST have the same length as the input
/// <c>documents</c> and preserve its order, so callers can zip the scores back
/// onto their candidates positionally.
/// </para>
/// <para>
/// This abstraction lives in the lean reranking library on purpose: it carries
/// no native dependency, so <see cref="OnnxCrossEncoderReranker"/> is fully
/// unit-testable against a stub. The real ONNX-backed implementation ships in
/// the opt-in <c>SmartDocs.Reranking.Onnx</c> adapter.
/// </para>
/// </summary>
public interface ICrossEncoderModel
{
    /// <summary>Short, stable model identifier (e.g. <c>bge-reranker-v2-m3</c>).</summary>
    string ModelId { get; }

    /// <summary>
    /// Score each document in <paramref name="documents"/> for relevance to
    /// <paramref name="query"/>. Returns one score in <c>[0, 1]</c> per
    /// document, in the same order as the input.
    /// </summary>
    /// <param name="query">The user query.</param>
    /// <param name="documents">Candidate document texts to score, in order.</param>
    /// <returns>Per-document relevance scores aligned to <paramref name="documents"/>.</returns>
    IReadOnlyList<float> Score(string query, IReadOnlyList<string> documents);
}
