using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Rag.Core.Contracts;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Configuration;
using Tokenizers.DotNet;

namespace Rag.Infrastructure.Embeddings;

/// <summary>
/// Generates embeddings using a local ONNX model (paraphrase-multilingual-MiniLM-L12-v2).
/// Uses Tokenizers.DotNet (HuggingFace tokenizers.rs wrapper) for correct SentencePiece Unigram tokenization.
/// Includes mean pooling and L2 normalization.
/// </summary>
public sealed class LocalOnnxEmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private const int PadTokenId = 1; // <pad> token ID from tokenizer.json added_tokens

    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly int _embeddingDim;
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

        _maxLength = options.MaxLength;

        // Load tokenizer from tokenizer.json (SentencePiece Unigram)
        var tokenizerPath = Path.Combine(options.ModelPath, "tokenizer.json");
        if (!File.Exists(tokenizerPath))
            throw new FileNotFoundException(
                $"tokenizer.json not found in '{options.ModelPath}'.",
                tokenizerPath);

        try
        {
            _tokenizer = new Tokenizer(vocabPath: tokenizerPath);
        }
        catch (TokenizerException ex)
        {
            throw new InvalidOperationException(
                $"Failed to load tokenizer from '{tokenizerPath}': {ex.Message}", ex);
        }

        // Load ONNX model
        _session = new InferenceSession(modelPath, new SessionOptions());

        // Discover input/output metadata at initialization time
        InputMetadata = _session.InputMetadata;
        OutputMetadata = _session.OutputMetadata;
        var outputMeta = OutputMetadata.Values.First();
        _embeddingDim = outputMeta.Dimensions[2];
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
    public int OutputDimensions => _embeddingDim;

    /// <inheritdoc />
    public async Task<Embedding> GenerateAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalOnnxEmbeddingGenerator));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text must not be null, empty, or whitespace.", nameof(text));

        // Tokenize using HuggingFace tokenizers (SentencePiece Unigram)
        var tokenIds = _tokenizer.Encode(text);

        // Truncate to max length
        var truncatedCount = Math.Min(tokenIds.Length, _maxLength);
        var inputIds = new int[truncatedCount];
        Array.Copy(tokenIds, inputIds, truncatedCount);

        // Create attention mask (1 for real tokens, 0 for padding)
        var attentionMask = new long[truncatedCount];
        for (var i = 0; i < truncatedCount; i++)
        {
            attentionMask[i] = 1L;
        }

        // Token type IDs (all zeros for single sentence)
        var tokenTypeIds = Enumerable.Repeat(0L, truncatedCount).ToArray();

        // Create ONNX tensors
        var inputIdsLong = inputIds.Select(x => (long)x).ToArray();
        var inputIdsTensor = new DenseTensor<long>(inputIdsLong.AsMemory(), new[] { 1, truncatedCount });
        var attentionMaskTensor = new DenseTensor<long>(attentionMask.AsMemory(), new[] { 1, truncatedCount });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds.AsMemory(), new[] { 1, truncatedCount });

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
