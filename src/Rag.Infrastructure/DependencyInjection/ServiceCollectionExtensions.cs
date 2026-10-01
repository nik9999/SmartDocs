using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rag.Application.Documents;
using Rag.Core.Contracts;
using Rag.Core.Embeddings;
using Rag.Infrastructure.Configuration;
using Rag.Infrastructure.Embeddings;
using Rag.Infrastructure.Persistence;
using Rag.Infrastructure.Persistence.Repositories;
using Rag.Infrastructure.Search;


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

        return services;
    }
}
