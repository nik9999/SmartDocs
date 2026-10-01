namespace Rag.Application.DependencyInjection;

using Documents;
using Generation;
using Microsoft.Extensions.DependencyInjection;
using Retrieval;

/// <summary>
/// Dependency injection extensions for the Application layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Application layer services.
    /// </summary>
    public static IServiceCollection AddRagApplication(this IServiceCollection services)
    {
        services.AddScoped<IIngestionService, DocumentIngestionService>();
        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddScoped<IRagService, RagService>();

        return services;
    }
}
