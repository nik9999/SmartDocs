using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.Ingestion.DependencyInjection;

/// <summary>
/// DI registration extensions for the SmartDocs ingestion pipeline. Lives in
/// <c>SmartDocs.Ingestion</c> (rather than <c>SmartDocs.Core</c>) because the
/// concrete chunkers it wires up — <see cref="RecursiveCharacterChunker"/> and
/// <see cref="ContextualChunker"/> — are defined here, and <c>SmartDocs.Core</c>
/// must not take a dependency on <c>SmartDocs.Ingestion</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Bind <see cref="SmartDocsOptions"/> to the <c>SmartDocs</c> configuration
    /// section and register the default <see cref="IChunker"/>. The registration
    /// reads <see cref="IngestionOptions.MaxChunkSize"/> for the recursive budget
    /// and, when <see cref="IngestionOptions.UseContextualRetrieval"/> is set,
    /// wraps the recursive chunker in a <see cref="ContextualChunker"/> (which
    /// needs the registered <see cref="IChatClient"/> at index time).
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="configuration">
    /// Application configuration. The <c>SmartDocs</c> section is bound to
    /// <see cref="SmartDocsOptions"/>; its <c>Ingestion</c> sub-section drives the
    /// chunker selection.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsIngestion(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SmartDocsOptions>()
            .Bind(configuration.GetSection(SmartDocsOptions.SectionName));

        services.AddSingleton<Preprocessing.IDocumentPreprocessor, Preprocessing.DocumentPreprocessor>();
        services.AddSingleton<Loaders.PdfDocumentLoader>();
        services.AddSingleton<Loaders.DocxDocumentLoader>();
        services.AddSingleton<Loaders.XlsxDocumentLoader>();
        services.AddSingleton<Loaders.CompositeDocumentLoader>(static sp =>
            new Loaders.CompositeDocumentLoader([
                sp.GetRequiredService<Loaders.PdfDocumentLoader>(),
                sp.GetRequiredService<Loaders.DocxDocumentLoader>(),
                sp.GetRequiredService<Loaders.XlsxDocumentLoader>()
            ]));
        services.AddSingleton<IDocumentLoader>(static sp => sp.GetRequiredService<Loaders.CompositeDocumentLoader>());

        services.AddSingleton<IChunker>(static sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SmartDocsOptions>>().Value;
            IChunker chunker = new RecursiveCharacterChunker(maxChunkSize: opts.Ingestion.MaxChunkSize);

            if (opts.Ingestion.UseContextualRetrieval)
            {
                var chat = sp.GetRequiredService<IChatClient>();
                chunker = new ContextualChunker(chunker, chat);
            }

            if (opts.Ingestion.PreprocessDocuments)
            {
                var preprocessor = sp.GetRequiredService<Preprocessing.IDocumentPreprocessor>();
                chunker = new PreprocessingChunker(chunker, preprocessor);
            }

            return chunker;
        });

        return services;
    }
}
