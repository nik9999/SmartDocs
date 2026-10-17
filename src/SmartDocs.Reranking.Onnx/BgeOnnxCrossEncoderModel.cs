using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using SmartDocs.Reranking;

namespace SmartDocs.Reranking.Onnx;

/// <summary>
/// A genuine ONNX cross-encoder over <c>BAAI/bge-reranker-v2-m3</c> — the
/// real self-hosted inference path behind <see cref="ICrossEncoderModel"/>.
/// Tokenizes each (query, document) pair into the XLM-RoBERTa pair encoding,
/// batches the pairs through the ONNX session, and maps the single relevance
/// logit per pair to a probability in <c>[0, 1]</c> with a sigmoid.
/// <para>
/// This is the only type in the solution that pulls the native ONNX runtime,
/// which is why it lives in the opt-in <c>SmartDocs.Reranking.Onnx</c> adapter
/// rather than the lean reranking core. It is exercised only when the model
/// files are present on disk; CI does not ship the multi-hundred-megabyte
/// model, so no unit test runs real inference against it.
/// </para>
/// </summary>
public sealed class BgeOnnxCrossEncoderModel : ICrossEncoderModel, IDisposable
{
    // XLM-RoBERTa special-token ids for bge-reranker-v2-m3's vocab. The pair
    // encoding is [<s>] query [</s></s>] doc [</s>] (bos=0, eos=2, pad=1). We
    // prefer the tokenizer's own ids when it exposes them and fall back to
    // these constants otherwise.
    private const int DefaultBosId = 0;
    private const int DefaultEosId = 2;
    private const int DefaultPadId = 1;

    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly int _batchSize;
    private readonly int _bosId;
    private readonly int _eosId;
    private readonly int _padId;

    /// <summary>Stable identifier for this cross-encoder model.</summary>
    public string ModelId => "bge-reranker-v2-m3";

    private BgeOnnxCrossEncoderModel(InferenceSession session, Tokenizer tokenizer, int maxLength, int batchSize)
    {
        _session = session;
        _tokenizer = tokenizer;
        _maxLength = maxLength;
        _batchSize = batchSize;

        // Prefer the tokenizer's special-token ids where available; the
        // SentencePiece XLM-R tokenizer exposes bos/eos but not a distinct pad,
        // so the pad id falls back to the XLM-R convention (1).
        if (tokenizer is SentencePieceTokenizer sp)
        {
            _bosId = sp.BeginningOfSentenceId;
            _eosId = sp.EndOfSentenceId;
            _padId = DefaultPadId;
        }
        else
        {
            _bosId = DefaultBosId;
            _eosId = DefaultEosId;
            _padId = DefaultPadId;
        }
    }

    /// <summary>
    /// Load a <see cref="BgeOnnxCrossEncoderModel"/> from an exported ONNX model
    /// and its SentencePiece tokenizer model.
    /// </summary>
    /// <param name="onnxModelPath">
    /// Path to the exported <c>model.onnx</c> for <c>BAAI/bge-reranker-v2-m3</c>.
    /// </param>
    /// <param name="sentencePieceModelPath">
    /// Path to the model's SentencePiece model file (e.g. <c>sentencepiece.bpe.model</c>).
    /// </param>
    /// <param name="maxLength">Maximum combined sequence length (truncated to this many tokens).</param>
    /// <param name="batchSize">How many (query, document) pairs to run per inference batch.</param>
    /// <returns>A ready-to-use cross-encoder model.</returns>
    /// <exception cref="FileNotFoundException">Thrown when either model file is missing.</exception>
    public static BgeOnnxCrossEncoderModel Create(
        string onnxModelPath,
        string sentencePieceModelPath,
        int maxLength = 512,
        int batchSize = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(onnxModelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentencePieceModelPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        if (!File.Exists(onnxModelPath))
        {
            throw new FileNotFoundException(
                $"ONNX model file not found at '{onnxModelPath}'. Download and export " +
                "BAAI/bge-reranker-v2-m3 from https://huggingface.co/BAAI/bge-reranker-v2-m3 " +
                "to ONNX, then point onnxModelPath at the resulting model.onnx.",
                onnxModelPath);
        }
        if (!File.Exists(sentencePieceModelPath))
        {
            throw new FileNotFoundException(
                $"SentencePiece model file not found at '{sentencePieceModelPath}'. It ships " +
                "alongside BAAI/bge-reranker-v2-m3 at https://huggingface.co/BAAI/bge-reranker-v2-m3 " +
                "(e.g. sentencepiece.bpe.model); point sentencePieceModelPath at it.",
                sentencePieceModelPath);
        }

        var session = new InferenceSession(onnxModelPath);
        using var spStream = File.OpenRead(sentencePieceModelPath);
        // bge-reranker-v2-m3 uses an XLM-RoBERTa SentencePiece tokenizer. We add
        // the bos/eos markers ourselves per pair, so build the tokenizer without
        // auto-inserting them here.
        var tokenizer = SentencePieceTokenizer.Create(
            spStream,
            addBeginningOfSentence: false,
            addEndOfSentence: false);

        return new BgeOnnxCrossEncoderModel(session, tokenizer, maxLength, batchSize);
    }

    /// <inheritdoc />
    public IReadOnlyList<float> Score(string query, IReadOnlyList<string> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            return Array.Empty<float>();
        }

        // The tokenizer was built without auto bos/eos; we add the XLM-R special
        // tokens ourselves in BuildPairEncoding, so encode the raw text here.
        var queryIds = _tokenizer.EncodeToIds(query);
        var results = new float[documents.Count];

        // Process the documents in batches of _batchSize.
        for (int start = 0; start < documents.Count; start += _batchSize)
        {
            int count = Math.Min(_batchSize, documents.Count - start);

            // Build the per-pair token-id rows for this batch.
            var rows = new List<int[]>(count);
            int maxLen = 0;
            for (int j = 0; j < count; j++)
            {
                var docIds = _tokenizer.EncodeToIds(documents[start + j]);
                var row = BuildPairEncoding(queryIds, docIds);
                rows.Add(row);
                maxLen = Math.Max(maxLen, row.Length);
            }

            // Right-pad each row to the batch's max length with the pad id and
            // build the matching attention mask (1 for real tokens, 0 for pad).
            var idsTensor = new DenseTensor<long>(new[] { count, maxLen });
            var maskTensor = new DenseTensor<long>(new[] { count, maxLen });
            for (int j = 0; j < count; j++)
            {
                var row = rows[j];
                for (int t = 0; t < maxLen; t++)
                {
                    bool real = t < row.Length;
                    idsTensor[j, t] = real ? row[t] : _padId;
                    maskTensor[j, t] = real ? 1L : 0L;
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", idsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
            };

            // Real inference: this runs the native ONNX session and is reached
            // only when the model files are present (never in CI).
            using var outputs = _session.Run(inputs);
            var logits = outputs[0].AsTensor<float>();

            // bge-reranker emits a single relevance logit per row. Read it from
            // the first column and map it to [0, 1] with a sigmoid.
            for (int j = 0; j < count; j++)
            {
                float logit = logits[j, 0];
                results[start + j] = Sigmoid(logit);
            }
        }

        return results;
    }

    // XLM-R pair encoding: [<s>] query [</s></s>] doc [</s>], truncated to _maxLength.
    private int[] BuildPairEncoding(IReadOnlyList<int> queryIds, IReadOnlyList<int> docIds)
    {
        // Fixed special tokens cost: <s> + </s></s> + </s> = 4 slots.
        const int SpecialTokenCount = 4;
        int budget = Math.Max(0, _maxLength - SpecialTokenCount);

        // Reserve up to half the budget for the query, the rest for the document.
        int queryBudget = Math.Min(queryIds.Count, budget / 2);
        int docBudget = Math.Min(docIds.Count, budget - queryBudget);

        var encoding = new List<int>(queryBudget + docBudget + SpecialTokenCount)
        {
            _bosId,
        };
        for (int i = 0; i < queryBudget; i++)
        {
            encoding.Add(queryIds[i]);
        }
        encoding.Add(_eosId);
        encoding.Add(_eosId);
        for (int i = 0; i < docBudget; i++)
        {
            encoding.Add(docIds[i]);
        }
        encoding.Add(_eosId);

        return [.. encoding];
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    /// <inheritdoc />
    public void Dispose()
    {
        _session.Dispose();
        (_tokenizer as IDisposable)?.Dispose();
    }
}
