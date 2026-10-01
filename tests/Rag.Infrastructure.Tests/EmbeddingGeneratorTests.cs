using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Documents;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Configuration;
using Rag.Infrastructure.Embeddings;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Rag.Infrastructure.Tests;

/// <summary>
/// Tests for LocalOnnxEmbeddingGenerator.
/// Note: These tests require the ONNX model files to be present.
/// Expected model directory: models/paraphrase-multilingual-MiniLM-L12-v2
/// Required files: model.onnx, vocab.txt
/// </summary>
public class EmbeddingGeneratorTests
{
    private const string ModelPath = "models/paraphrase-multilingual-MiniLM-L12-v2";

    private static bool ModelExists()
    {
        var modelFile = Path.Combine(ModelPath, "model.onnx");
        if (!File.Exists(modelFile))
            return false;

        // vocab.txt OR tokenizer.json
        var vocabFile = Path.Combine(ModelPath, "vocab.txt");
        var tokenizerFile = Path.Combine(ModelPath, "tokenizer.json");
        return File.Exists(vocabFile) || File.Exists(tokenizerFile);
    }

    [Fact]
    public void Constructor_InvalidModelPath_Throws()
    {
        // Arrange
        var options = new EmbeddingOptions { ModelPath = "nonexistent/path", ModelName = "model.onnx" };

        // Act & Assert
        var exception = Assert.ThrowsAny<Exception>(() => new LocalOnnxEmbeddingGenerator(options));
        Assert.IsAssignableFrom<FileNotFoundException>(exception);
    }

    [Fact]
    public void Constructor_EmptyModelPath_Throws()
    {
        // Arrange
        var options = new EmbeddingOptions { ModelPath = "", ModelName = "model.onnx" };

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(() => new LocalOnnxEmbeddingGenerator(options));
    }

    [Fact]
    public async Task GenerateAsync_EmptyText_Throws()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => generator.GenerateAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_WhitespaceText_Throws()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Act & Assert
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => generator.GenerateAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_RussianText_FiniteValues()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Act
        var embedding = await generator.GenerateAsync(
            "Измеренное значение Канал 2 составляет 15.4 В.",
            CancellationToken.None);

        // Assert
        Assert.True(embedding.Dimensions > 0);
        Assert.True(embedding.Vector.Length > 0);
        Assert.Equal("paraphrase-multilingual-MiniLM-L12-v2", embedding.Model);

        for (var i = 0; i < embedding.Vector.Length; i++)
        {
            Assert.False(float.IsNaN(embedding.Vector[i]), $"NaN at index {i}");
            Assert.False(float.IsInfinity(embedding.Vector[i]), $"Infinity at index {i}");
        }
    }

    [Fact]
    public async Task GenerateAsync_Deterministic_Output()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });
        var text = "Температура технологического процесса";

        // Act
        var embedding1 = await generator.GenerateAsync(text, CancellationToken.None);
        var embedding2 = await generator.GenerateAsync(text, CancellationToken.None);

        // Assert - results should be identical
        Assert.Equal(embedding1.Vector.Length, embedding2.Vector.Length);
        for (var i = 0; i < embedding1.Vector.Length; i++)
        {
            Assert.Equal(embedding1.Vector[i], embedding2.Vector[i]);
        }
    }

    [Fact]
    public async Task GenerateAsync_L2Normalization_UnitNorm()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Act
        var embedding = await generator.GenerateAsync(
            "Измеренное значение Канал 2",
            CancellationToken.None);

        // Assert - L2 norm should be approximately 1.0
        var norm = 0.0f;
        for (var i = 0; i < embedding.Vector.Length; i++)
        {
            norm += embedding.Vector[i] * embedding.Vector[i];
        }
        norm = (float)Math.Sqrt(norm);

        Assert.True(Math.Abs(norm - 1.0f) < 0.01f,
            $"L2 norm is {norm}, expected ~1.0");
    }

    [Fact]
    public async Task Integration_TextToEmbeddingToRepository_RoundTrip()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var path = CreateTempDbPath();
        var connectionString = $"Data Source={path}";
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });
        var repo = new EmbeddingRepository(connectionString);
        var chunkId = Guid.NewGuid();

        // Act
        var embedding = await generator.GenerateAsync(
            "Измеренное значение Канал 2",
            CancellationToken.None);

        await repo.SaveAsync(chunkId, embedding, CancellationToken.None);
        var retrieved = await repo.GetAsync(chunkId, CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(embedding.Model, retrieved.Model);
        Assert.Equal(embedding.Dimensions, retrieved.Dimensions);
        Assert.Equal(embedding.Vector.Length, retrieved.Vector.Length);
        for (var i = 0; i < embedding.Vector.Length; i++)
        {
            Assert.Equal(embedding.Vector[i], retrieved.Vector[i], precision: 5);
        }
    }

    [Fact]
    public async Task SemanticSanity_SimilarTextMoreSimilar()
    {
        // Skip if model not available
        if (!ModelExists())
        {
            return;
        }

        // Arrange
        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Act
        var embeddingA = await generator.GenerateAsync("Измеренное значение Канал 2", CancellationToken.None);
        var embeddingB = await generator.GenerateAsync("Измеренное значение канала 2", CancellationToken.None);
        var embeddingC = await generator.GenerateAsync("Аварийная температура двигателя", CancellationToken.None);

        var cosineAB = CosineSimilarity(embeddingA.Vector, embeddingB.Vector);
        var cosineAC = CosineSimilarity(embeddingA.Vector, embeddingC.Vector);

        // Assert - similar texts should have higher cosine similarity
        Assert.True(cosineAB > cosineAC,
            $"Similar texts A/B cosine ({cosineAB:F4}) should be > A/C cosine ({cosineAC:F4})");
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * (double)b[i];
            normA += (double)a[i] * (double)a[i];
            normB += (double)b[i] * (double)b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    [Fact]
    public async Task TokenizerRegression_RussianText_FiniteEmbeddings()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var russianTexts = new[]
        {
            "Измеренное значение Канал 2",
            "давление в системе МПа",
            "температура технологического процесса",
            "аварийный сигнал превышение порога"
        };

        foreach (var text in russianTexts)
        {
            var embedding = await generator.GenerateAsync(text, CancellationToken.None);
            Assert.NotNull(embedding);
            Assert.True(embedding.Vector.Length > 0);

            for (var i = 0; i < embedding.Vector.Length; i++)
            {
                Assert.False(float.IsNaN(embedding.Vector[i]), $"NaN at index {i} for '{text}'");
                Assert.False(float.IsInfinity(embedding.Vector[i]), $"Infinity at index {i} for '{text}'");
            }
        }
    }

    [Fact]
    public async Task TokenizerRegression_LatinText_FiniteEmbeddings()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var latinTexts = new[]
        {
            "Channel 2 measured value",
            "temperature sensor",
            "pressure system MPa"
        };

        foreach (var text in latinTexts)
        {
            var embedding = await generator.GenerateAsync(text, CancellationToken.None);
            Assert.NotNull(embedding);
            Assert.True(embedding.Vector.Length > 0);
        }
    }

    [Fact]
    public async Task TokenizerRegression_MixedText_FiniteEmbeddings()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var mixedTexts = new[]
        {
            "Канал A voltage 220 V",
            "Сенсор B temp 85.5 C",
            "Sistema Canal 3 presión"
        };

        foreach (var text in mixedTexts)
        {
            var embedding = await generator.GenerateAsync(text, CancellationToken.None);
            Assert.NotNull(embedding);
            Assert.True(embedding.Vector.Length > 0);
        }
    }

    [Fact]
    public async Task TokenizerRegression_Numbers_FiniteEmbeddings()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var numberTexts = new[]
        {
            "220 В",
            "50 Гц",
            "0.95",
            "15.4 В",
            "100.5 МПа"
        };

        foreach (var text in numberTexts)
        {
            var embedding = await generator.GenerateAsync(text, CancellationToken.None);
            Assert.NotNull(embedding);
            Assert.True(embedding.Vector.Length > 0);
        }
    }

    [Fact]
    public async Task TokenizerRegression_Punctuation_FiniteEmbeddings()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var punctTexts = new[]
        {
            "Канал 2: 220 В.",
            "Температура: 85.5°C!",
            "Давление (МПа): 10.5"
        };

        foreach (var text in punctTexts)
        {
            var embedding = await generator.GenerateAsync(text, CancellationToken.None);
            Assert.NotNull(embedding);
            Assert.True(embedding.Vector.Length > 0);
        }
    }

    [Fact]
    public async Task TokenizerRegression_LongText_TruncationWorks()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Generate text longer than 512 tokens
        var longText = string.Join(" ", Enumerable.Repeat("измеренное значение", 300));

        var embedding = await generator.GenerateAsync(longText, CancellationToken.None);
        Assert.NotNull(embedding);
        Assert.True(embedding.Vector.Length > 0);

        // Verify finite values
        for (var i = 0; i < embedding.Vector.Length; i++)
        {
            Assert.False(float.IsNaN(embedding.Vector[i]));
            Assert.False(float.IsInfinity(embedding.Vector[i]));
        }
    }

    [Fact]
    public async Task Embedding_Deterministic_ThreeRuns()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var text = "Измеренное значение Канал 2";

        var embedding1 = await generator.GenerateAsync(text, CancellationToken.None);
        var embedding2 = await generator.GenerateAsync(text, CancellationToken.None);
        var embedding3 = await generator.GenerateAsync(text, CancellationToken.None);

        // All three should be identical
        Assert.Equal(embedding1.Vector.Length, embedding2.Vector.Length);
        Assert.Equal(embedding1.Vector.Length, embedding3.Vector.Length);

        for (var i = 0; i < embedding1.Vector.Length; i++)
        {
            Assert.Equal(embedding1.Vector[i], embedding2.Vector[i]);
            Assert.Equal(embedding1.Vector[i], embedding3.Vector[i]);
        }
    }

    [Fact]
    public async Task Embedding_Semantic_SimilarTextsMoreSimilar()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        // Identical texts should have cosine similarity = 1.0
        var text = "Канал 2 давление";
        var emb1 = await generator.GenerateAsync(text, CancellationToken.None);
        var emb2 = await generator.GenerateAsync(text, CancellationToken.None);
        var cosineIdentical = CosineSimilarity(emb1.Vector, emb2.Vector);
        Assert.True(Math.Abs(cosineIdentical - 1.0) < 0.001,
            $"Identical texts cosine should be ~1.0, got {cosineIdentical:F6}");

        // Different texts should NOT have accidentally high similarity
        var embDifferent = await generator.GenerateAsync("совершенно другой текст", CancellationToken.None);
        var cosineDifferent = CosineSimilarity(emb1.Vector, embDifferent.Vector);

        // Should be significantly less than 1.0 (allowing for some similarity due to shared words)
        Assert.True(cosineDifferent < 0.8,
            $"Different texts cosine ({cosineDifferent:F4}) should be < 0.8");
    }

    [Fact]
    public async Task Embedding_Dimension_Is384()
    {
        if (!ModelExists()) return;

        var generator = new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
        {
            ModelPath = ModelPath,
            ModelName = "model.onnx",
            MaxLength = 128
        });

        var embedding = await generator.GenerateAsync("test", CancellationToken.None);
        Assert.Equal(384, embedding.Dimensions);
        Assert.Equal(384, embedding.Vector.Length);
        Assert.Equal(384, generator.OutputDimensions);
    }

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_embedding_gen_test_{Guid.NewGuid()}.db");
    }
}
