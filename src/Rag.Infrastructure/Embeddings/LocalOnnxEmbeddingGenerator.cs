using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Rag.Core.Contracts;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Configuration;

namespace Rag.Infrastructure.Embeddings;

/// <summary>
/// Generates embeddings using a local ONNX model (paraphrase-multilingual-MiniLM-L12-v2).
/// Includes a built-in WordPiece tokenizer, mean pooling, and L2 normalization.
/// </summary>
public sealed class LocalOnnxEmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private const string ClsToken = "[CLS]";
    private const string SepToken = "[SEP]";
    private const string PadToken = "[PAD]";

    private readonly InferenceSession _session;
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<int, string> _inverseVocab;
    private readonly int _maxLength;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _padId;
    private bool _disposed;

    /// <summary>
    /// Creates the embedding generator from the given options.
    /// </summary>
    public LocalOnnxEmbeddingGenerator(EmbeddingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ModelPath))
            throw new ArgumentException("ModelPath must not be empty.", nameof(options));

        var modelPath = Path.Combine(options.ModelPath, options.ModelName);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ONNX model not found at '{modelPath}'.",
                modelPath);

        var vocabPath = Path.Combine(options.ModelPath, "vocab.txt");
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException(
                $"Vocab file not found at '{vocabPath}'.",
                vocabPath);

        _maxLength = options.MaxLength;

        // Load vocabulary
        _vocab = LoadVocabulary(vocabPath);
        _inverseVocab = _vocab.ToDictionary(kv => kv.Value, kv => kv.Key);

        _clsId = GetTokenId(ClsToken);
        _sepId = GetTokenId(SepToken);
        _padId = GetTokenId(PadToken);

        // Load ONNX model
        _session = new InferenceSession(modelPath, new SessionOptions());

        // Discover input/output metadata at initialization time
        InputMetadata = _session.InputMetadata;
        OutputMetadata = _session.OutputMetadata;
        var outputMeta = OutputMetadata.Values.First();
        OutputDimensions = outputMeta.Dimensions[2];
    }

    /// <summary>
    /// Input tensor metadata discovered from the ONNX model.
    /// </summary>
    public IReadOnlyDictionary<string, NodeMetadata> InputMetadata { get; }

    /// <summary>
    /// Output tensor metadata discovered from the ONNX model.
    /// </summary>
    public IReadOnlyDictionary<string, NodeMetadata> OutputMetadata { get; }

    /// <summary>
    /// Embedding dimension derived from the model output shape.
    /// </summary>
    public int OutputDimensions { get; }

    /// <inheritdoc />
    public async Task<Embedding> GenerateAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalOnnxEmbeddingGenerator));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be null, empty, or whitespace.", nameof(text));

        // Tokenize
        var tokenIds = WordPieceEncode(text);

        // Pad or truncate to max length
        var inputIds = PadOrTruncate(tokenIds, _maxLength, _padId);
        var attentionMask = new long[_maxLength];
        for (var i = 0; i < inputIds.Length; i++)
        {
            attentionMask[i] = inputIds[i] == _padId ? 0L : 1L;
        }

        // Token type IDs (all zeros for single sentence)
        var tokenTypeIds = Enumerable.Repeat(0L, _maxLength).ToArray();

        // Create ONNX tensors using Memory<T> constructor
        var inputIdsLong = inputIds.Select(x => (long)x).ToArray();
        var inputIdsTensor = new DenseTensor<long>(inputIdsLong.AsMemory(), new[] { 1, _maxLength });
        var attentionMaskTensor = new DenseTensor<long>(attentionMask.AsMemory(), new[] { 1, _maxLength });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds.AsMemory(), new[] { 1, _maxLength });

        // Discover input names from metadata
        var inputIdsName = InputMetadata.Keys.First(k => k.Contains("input_ids", StringComparison.OrdinalIgnoreCase));
        var attentionMaskName = InputMetadata.Keys.First(k => k.Contains("attention", StringComparison.OrdinalIgnoreCase));
        var tokenTypeIdsName = InputMetadata.Keys.First(k => k.Contains("token_type", StringComparison.OrdinalIgnoreCase) || k.Contains("segment", StringComparison.OrdinalIgnoreCase));

        // Prepare inputs
        var inputs = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor(inputIdsName, inputIdsTensor),
            NamedOnnxValue.CreateFromTensor(attentionMaskName, attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor(tokenTypeIdsName, tokenTypeIdsTensor)
        };

        // Run inference
        using var results = _session.Run(inputs);

        // Get output tensor (token embeddings)
        var outputTensor = (DenseTensor<float>)results.First().AsTensor<float>();

        var sequenceLength = outputTensor.Dimensions[1];
        var embeddingDim = outputTensor.Dimensions[2];

        // Mean pooling with attention mask
        var pooled = MeanPooling(outputTensor, attentionMask, sequenceLength, embeddingDim);

        // L2 normalization
        var normalized = L2Normalize(pooled);

        // Validate all values are finite
        for (var i = 0; i < normalized.Length; i++)
        {
            if (float.IsNaN(normalized[i]) || float.IsInfinity(normalized[i]))
                throw new InvalidOperationException(
                    $"Embedding contains non-finite value at index {i}: {normalized[i]}");
        }

        var model = "paraphrase-multilingual-MiniLM-L12-v2";
        return new Embedding(normalized, model);
    }

    /// <summary>
    /// Loads the WordPiece vocabulary from vocab.txt.
    /// </summary>
    private static Dictionary<string, int> LoadVocabulary(string vocabPath)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(vocabPath);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Split('\t')[0]; // Handle possible tab-separated format
            vocab[line] = i;
        }
        return vocab;
    }

    /// <summary>
    /// Encodes text using WordPiece tokenization.
    /// </summary>
    private int[] WordPieceEncode(string text)
    {
        // Normalize text
        text = NormalizeText(text);

        // Split into initial tokens (words and subwords)
        var tokens = SplitIntoTokens(text);

        // WordPiece encode each token
        var result = new List<int> { _clsId }; // [CLS]

        foreach (var token in tokens)
        {
            var subtokens = WordPieceTokenize(token);
            result.AddRange(subtokens);
        }

        result.Add(_sepId); // [SEP]

        return result.ToArray();
    }

    /// <summary>
    /// Normalizes text for tokenization.
    /// </summary>
    private static string NormalizeText(string text)
    {
        // Basic normalization: lowercase, collapse whitespace
        text = text.ToLowerInvariant();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        text = text.Trim();
        return text;
    }

    /// <summary>
    /// Splits text into initial tokens (words and punctuation).
    /// </summary>
    private string[] SplitIntoTokens(string text)
    {
        // Split on whitespace first
        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();

        foreach (var word in words)
        {
            // Split word into subword-friendly chunks
            // Handle common patterns: hyphens, slashes, etc.
            var parts = System.Text.RegularExpressions.Regex.Split(word, @"([\-\/])");
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                    tokens.Add(part);
            }
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Tokenizes a single token using WordPiece algorithm.
    /// </summary>
    private int[] WordPieceTokenize(string token)
    {
        // If token is in vocab, return it directly
        if (_vocab.TryGetValue(token, out var id))
            return new[] { id };

        // Try to find the longest matching subword
        var result = new List<int>();
        var remaining = token;
        var isNonStart = false;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= 2)
            {
                // Too short to be a subword, use unknown token
                var unkId = GetTokenId("[UNK]");
                result.Add(unkId);
                break;
            }

            var modified = (isNonStart ? "##" : "") + remaining;
            var found = false;

            // Try progressively shorter suffixes
            for (var i = remaining.Length - 1; i >= 1; i--)
            {
                var subword = remaining.Substring(0, i);
                if (_vocab.TryGetValue(modified.Substring(0, subword.Length + (isNonStart ? 2 : 0)), out id))
                {
                    result.Add(id);
                    remaining = remaining.Substring(i);
                    isNonStart = true;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var unkId = GetTokenId("[UNK]");
                result.Add(unkId);
                break;
            }
        }

        return result.Count > 0 ? result.ToArray() : new[] { GetTokenId("[UNK]") };
    }

    /// <summary>
    /// Gets the token ID for a given token string.
    /// </summary>
    private int GetTokenId(string token)
    {
        return _vocab.TryGetValue(token, out var id) ? id : 1; // 1 is typically [UNK]
    }

    /// <summary>
    /// Pads or truncates an array to the specified length.
    /// </summary>
    private static int[] PadOrTruncate(int[] source, int length, int paddingValue)
    {
        if (source.Length >= length)
            return source[..length];

        var result = new int[length];
        Array.Copy(source, result, source.Length);
        for (var i = source.Length; i < length; i++)
            result[i] = paddingValue;

        return result;
    }

    /// <summary>
    /// Applies mean pooling using attention mask to ignore padding tokens.
    /// </summary>
    private static float[] MeanPooling(
        DenseTensor<float> embeddings,
        long[] attentionMask,
        int sequenceLength,
        int embeddingDim)
    {
        var result = new float[embeddingDim];

        for (var t = 0; t < sequenceLength; t++)
        {
            if (attentionMask[t] == 0)
                continue;

            for (var d = 0; d < embeddingDim; d++)
            {
                result[d] += embeddings[0, t, d];
            }
        }

        var count = attentionMask.Take(sequenceLength).Count(m => m > 0);
        if (count > 0)
        {
            for (var d = 0; d < embeddingDim; d++)
            {
                result[d] /= count;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies L2 normalization to the vector.
    /// </summary>
    private static float[] L2Normalize(float[] vector)
    {
        var norm = 0.0f;
        for (var i = 0; i < vector.Length; i++)
        {
            norm += vector[i] * vector[i];
        }
        norm = (float)Math.Sqrt(norm);

        if (norm == 0)
            return vector;

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / norm;
        }

        return normalized;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _session?.Dispose();
        _disposed = true;
    }
}
