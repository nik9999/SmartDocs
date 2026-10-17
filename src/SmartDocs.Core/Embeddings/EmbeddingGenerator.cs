using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace SmartDocs.Core.Embeddings;

/// <summary>
/// In-process ONNX Runtime implementation of <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>
/// for embedding models (e.g. BGE-M3).
/// </summary>
public sealed class EmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly int _dimensions;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="EmbeddingGenerator"/>.
    /// </summary>
    /// <param name="modelPath">Path to model.onnx.</param>
    /// <param name="sentencePieceModelPath">Path to sentencepiece.bpe.model.</param>
    /// <param name="dimensions">Embedding dimension (default 1024 for BGE-M3).</param>
    public EmbeddingGenerator(string modelPath, string? sentencePieceModelPath = null, int dimensions = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var resolvedModelPath = ResolvePath(modelPath);
        if (!File.Exists(resolvedModelPath))
        {
            throw new FileNotFoundException($"ONNX embedding model file not found at: {resolvedModelPath}", resolvedModelPath);
        }

        var resolvedSpmPath = !string.IsNullOrWhiteSpace(sentencePieceModelPath)
            ? ResolvePath(sentencePieceModelPath)
            : Path.Combine(Path.GetDirectoryName(resolvedModelPath)!, "sentencepiece.bpe.model");

        if (!File.Exists(resolvedSpmPath))
        {
            throw new FileNotFoundException($"SentencePiece model file not found at: {resolvedSpmPath}", resolvedSpmPath);
        }

        var sessionOptions = new SessionOptions();
        sessionOptions.AppendExecutionProvider_CPU(1);
        _session = new InferenceSession(resolvedModelPath, sessionOptions);

        using var stream = File.OpenRead(resolvedSpmPath);
        _tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: true, addEndOfSentence: true);
        _dimensions = dimensions;
    }

    /// <inheritdoc />
    public EmbeddingGeneratorMetadata Metadata => new("bge-m3-onnx", null, "bge-m3", _dimensions);

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(values);

        var list = values.ToList();
        var embeddings = new List<Embedding<float>>(list.Count);

        foreach (var text in list)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tokenIds = _tokenizer.EncodeToIds(text);
            if (tokenIds.Count == 0)
            {
                // Fallback for empty text: [CLS], [SEP]
                tokenIds = [1, 2];
            }

            int seqLen = Math.Min(tokenIds.Count, 8192);
            long[] inputIds = new long[seqLen];
            long[] attentionMask = new long[seqLen];

            for (int i = 0; i < seqLen; i++)
            {
                int rawId = tokenIds[i];
                // HuggingFace XLM-RoBERTa / BGE-M3 mapping:
                // <s> = 0, <pad> = 1, </s> = 2, <unk> = 3.
                // Raw SentencePiece has <unk>=0, <s>=1, </s>=2, followed by vocabulary starting at index 3.
                long hfId = rawId switch
                {
                    1 => 0L,              // <s> (BOS / CLS token)
                    2 => 2L,              // </s> (EOS / SEP token)
                    0 => 3L,              // <unk>
                    _ => (long)rawId + 1L // shifted by +1 because HuggingFace inserted <pad> at index 1
                };

                inputIds[i] = hfId;
                attentionMask[i] = 1L;
            }

            var dim = new[] { 1, seqLen };
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dim)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, dim))
            };

            using var outputs = _session.Run(inputs);
            var outputTensor = outputs.First(static o => o.Name == "last_hidden_state").AsTensor<float>();

            // CLS pooling: token 0 representation, length _dimensions
            float[] vector = new float[_dimensions];
            double sumSquares = 0.0;
            for (int i = 0; i < _dimensions; i++)
            {
                float val = outputTensor[0, 0, i];
                vector[i] = val;
                sumSquares += (double)val * val;
            }

            // L2-normalization
            float norm = (float)Math.Sqrt(sumSquares);
            if (norm > 0)
            {
                for (int i = 0; i < _dimensions; i++)
                {
                    vector[i] /= norm;
                }
            }

            embeddings.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _session.Dispose();
        }
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        var baseDir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(baseDir))
        {
            var candidate = Path.Combine(baseDir, path);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            var parent = Directory.GetParent(baseDir);
            if (parent == null)
            {
                break;
            }

            baseDir = parent.FullName;
        }

        return Path.GetFullPath(path);
    }
}
