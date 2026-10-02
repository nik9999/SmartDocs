using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Chunking;
using Rag.Infrastructure.Configuration;
using Rag.Infrastructure.Embeddings;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Persistence.Repositories;
using Rag.Infrastructure.Search;
using Tokenizers.DotNet;


namespace Rag.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection extensions for the Infrastructure layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Infrastructure layer services.
    /// </summary>
    public static IServiceCollection AddRagInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration
            .GetSection("Rag:Storage:Sqlite:ConnectionString")
            .Value ?? "Data Source=data/rag.db";

        services.AddSingleton<SqliteDatabase>(sp => new SqliteDatabase(connectionString));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDocumentRepository, DocumentRepository>(sp =>
            new DocumentRepository(connectionString));
        services.AddScoped<ISparseRetriever, Fts5Search>(sp =>
            new Fts5Search(connectionString));

        // Embedding services
        var embeddingSection = configuration.GetSection("Rag:Embeddings");
        services.AddSingleton<IEmbeddingGenerator, LocalOnnxEmbeddingGenerator>(sp =>
        {
            var modelPath = embeddingSection["ModelPath"] ?? "models/paraphrase-multilingual-MiniLM-L12-v2";
            var modelName = embeddingSection["ModelName"] ?? "model.onnx";
            var maxLengthStr = embeddingSection["MaxLength"];
            var maxLength = int.TryParse(maxLengthStr, out var ml) ? ml : 128;
            return new LocalOnnxEmbeddingGenerator(new EmbeddingOptions
            {
                ModelPath = modelPath,
                ModelName = modelName,
                MaxLength = maxLength
            });
        });
        services.AddScoped<IEmbeddingRepository, EmbeddingRepository>(sp =>
            new EmbeddingRepository(connectionString));
        services.AddScoped<IVectorRetriever, SqliteVectorRetriever>(sp =>
            new SqliteVectorRetriever(connectionString, sp.GetRequiredService<IEmbeddingGenerator>()));

        // Hybrid retrieval components
        services.AddScoped<IResultFusion, ReciprocalRankFusion>();
        services.AddScoped<IReranker, BaselineReranker>();

        // Chunking — TokenAwareChunker uses the same tokenizer as the embedding pipeline
        var tokenizerModelPath = embeddingSection["TokenizerPath"]
            ?? Path.Combine(Path.GetDirectoryName(embeddingSection["ModelPath"]) ?? "models", "paraphrase-multilingual-MiniLM-L12-v2");

        services.AddScoped<IChunker, TokenAwareChunker>(sp =>
        {
            var tokenizerJsonPath = Path.Combine(tokenizerModelPath, "tokenizer.json");
            Tokenizer? tokenizer = null;
            if (File.Exists(tokenizerJsonPath))
            {
                tokenizer = new Tokenizer(vocabPath: tokenizerJsonPath);
            }

            if (tokenizer == null)
            {
                // Fallback: create a minimal tokenizer if tokenizer.json not found
                // This handles the case where only vocab.txt exists
                var vocabTxtPath = Path.Combine(tokenizerModelPath, "vocab.txt");
                if (File.Exists(vocabTxtPath))
                {
                    tokenizer = new Tokenizer(vocabPath: vocabTxtPath);
                }
            }

            if (tokenizer == null)
            {
                throw new InvalidOperationException(
                    $"Tokenizer not found. Searched in '{tokenizerModelPath}'. " +
                    "Required: tokenizer.json or vocab.txt. " +
                    "Configure Rag:Embeddings:TokenizerPath if the tokenizer is in a different location.");
            }

            return new TokenAwareChunker(tokenizer, targetChunkTokens: 256, maxChunkTokens: 384, overlapTokens: 48);
        });

        return services;
    }
}
