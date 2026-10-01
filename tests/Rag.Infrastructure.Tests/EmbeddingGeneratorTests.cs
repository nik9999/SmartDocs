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
        var vocabFile = Path.Combine(ModelPath, "vocab.txt");
        return File.Exists(modelFile) && File.Exists(vocabFile);
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

    private static string CreateTempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rag_embedding_gen_test_{Guid.NewGuid()}.db");
    }
}
