using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Loaders;

/// <summary>
/// Composite document loader that routes document loading requests to the appropriate
/// format loader (<see cref="PdfDocumentLoader"/>, <see cref="DocxDocumentLoader"/>,
/// <see cref="XlsxDocumentLoader"/>) based on the file extension.
/// </summary>
public sealed class CompositeDocumentLoader : IDocumentLoader
{
    private readonly Dictionary<string, IDocumentLoader> _loaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="CompositeDocumentLoader"/> with the specified loaders.
    /// </summary>
    /// <param name="loaders">Collection of format-specific loaders to register.</param>
    public CompositeDocumentLoader(IEnumerable<IDocumentLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(loaders);

        foreach (var loader in loaders)
        {
            // Avoid registering CompositeDocumentLoader inside itself
            if (loader is CompositeDocumentLoader)
            {
                continue;
            }

            foreach (var ext in loader.SupportedExtensions)
            {
                _loaders[ext] = loader;
            }
        }
    }

    /// <summary>
    /// Creates a <see cref="CompositeDocumentLoader"/> with default implementations for PDF, Word, and Excel.
    /// </summary>
    public static CompositeDocumentLoader CreateDefault()
    {
        return new CompositeDocumentLoader(
        [
            new PdfDocumentLoader(),
            new DocxDocumentLoader(),
            new XlsxDocumentLoader()
        ]);
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> SupportedExtensions => _loaders.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<Document> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string ext = Path.GetExtension(path);
        if (_loaders.TryGetValue(ext, out var loader))
        {
            return loader.LoadAsync(path, cancellationToken);
        }

        throw new NotSupportedException(
            string.Format(
                CultureInfo.InvariantCulture,
                "No loader registered for file extension '{0}'. Supported extensions: {1}",
                ext,
                string.Join(", ", SupportedExtensions)));
    }
}

