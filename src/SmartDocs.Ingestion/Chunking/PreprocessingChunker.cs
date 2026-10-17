using System;
using System.Collections.Generic;
using System.Threading;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Preprocessing;

namespace SmartDocs.Ingestion.Chunking;

/// <summary>
/// Decorator chunker that cleans and normalizes documents (dehyphenation, blank-page filtering,
/// dot-leader cleanup, and running header/footer stripping) before delegating to the wrapped <see cref="IChunker"/>.
/// </summary>
public sealed class PreprocessingChunker : IChunker
{
    private readonly IChunker _inner;
    private readonly IDocumentPreprocessor _preprocessor;

    public PreprocessingChunker(IChunker inner, IDocumentPreprocessor? preprocessor = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _preprocessor = preprocessor ?? new DocumentPreprocessor();
    }

    /// <inheritdoc/>
    public string Strategy => $"preprocessed({_inner.Strategy})";

    /// <inheritdoc/>
    public IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var cleaned = _preprocessor.Preprocess(document);
        return _inner.ChunkAsync(cleaned, cancellationToken);
    }
}
