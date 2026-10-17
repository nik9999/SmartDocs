using System.Text;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.Ingestion.Multimodal;

/// <summary>
/// Chunker decorator that augments each text chunk with the captions of
/// any <see cref="ExtractedImage"/>s and the markdown of any
/// <see cref="ExtractedTable"/>s assigned to that chunk's page range.
/// Image captions and table markdown are inlined into the chunk text so
/// downstream embedding + retrieval pick them up unchanged.
/// Also supports enforcing an optional token budget ceiling (<see cref="MaxTokens"/>)
/// using an <see cref="ITokenCounter"/>.
/// </summary>
public sealed class MultimodalChunker : IChunker
{
    private readonly IChunker _innerTextChunker;
    private readonly Dictionary<int, IReadOnlyList<ExtractedImage>> _imagesByPage;
    private readonly Dictionary<int, IReadOnlyList<ExtractedTable>> _tablesByPage;
    private readonly ITokenCounter? _tokenCounter;
    private readonly int _maxTokens;

    /// <summary>The token budget ceiling for each chunk (0 if unconstrained).</summary>
    public int MaxTokens => _maxTokens;

    /// <summary>The chunking strategy name.</summary>
    public string Strategy => $"multimodal({_innerTextChunker.Strategy})";

    public MultimodalChunker(
        IChunker innerTextChunker,
        IReadOnlyList<ExtractedImage> images,
        IReadOnlyList<ExtractedTable> tables,
        ITokenCounter? tokenCounter = null,
        int maxTokens = 0)
    {
        ArgumentNullException.ThrowIfNull(innerTextChunker);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(tables);
        if (maxTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "maxTokens must be non-negative.");
        }

        _innerTextChunker = innerTextChunker;
        _tokenCounter = tokenCounter;
        _maxTokens = maxTokens;
        _imagesByPage = images.GroupBy(i => i.PageNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ExtractedImage>)g.ToList());
        _tablesByPage = tables.GroupBy(t => t.PageNumber)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ExtractedTable>)g.ToList());
    }

    /// <summary>
    /// Build a stream of <see cref="MultimodalChunk"/>s. The simple page-based
    /// assignment used here treats every text chunk as "page 1" unless the
    /// caller supplies a page-aware text chunker.
    /// If <see cref="_tokenCounter"/> and <see cref="_maxTokens"/> are provided,
    /// any augmented chunk exceeding the budget is re-split into conforming sub-chunks.
    /// </summary>
    public async IAsyncEnumerable<MultimodalChunk> ChunkAsync(
        Document document,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        TokenAwareChunker? postSplitter = (_tokenCounter != null && _maxTokens > 0)
            ? new TokenAwareChunker(_tokenCounter, _maxTokens)
            : null;

        await foreach (var raw in _innerTextChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            const int pageNumber = 1;
            var imgs = _imagesByPage.TryGetValue(pageNumber, out var imageList)
                ? imageList : (IReadOnlyList<ExtractedImage>)Array.Empty<ExtractedImage>();
            var tbls = _tablesByPage.TryGetValue(pageNumber, out var tableList)
                ? tableList : (IReadOnlyList<ExtractedTable>)Array.Empty<ExtractedTable>();

            var augmented = BuildAugmentedText(raw.Text, imgs, tbls);
            var augmentedChunk = raw with { Text = augmented };

            if (postSplitter != null && _tokenCounter!.CountTokens(augmented) > _maxTokens)
            {
                foreach (var piece in postSplitter.SplitIfNeeded(augmentedChunk))
                {
                    yield return new MultimodalChunk(piece, imgs, tbls);
                }
            }
            else
            {
                yield return new MultimodalChunk(augmentedChunk, imgs, tbls);
            }
        }
    }

    async IAsyncEnumerable<DocumentChunk> IChunker.ChunkAsync(
        Document document,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var mmChunk in ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            yield return mmChunk.Chunk;
        }
    }

    private static string BuildAugmentedText(
        string text,
        IReadOnlyList<ExtractedImage> images,
        IReadOnlyList<ExtractedTable> tables)
    {
        if (images.Count == 0 && tables.Count == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text);
        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img.Caption))
            {
                continue;
            }
            sb.Append("\n\n[Figure ").Append(img.ImageId).Append("] ").Append(img.Caption);
        }
        foreach (var table in tables)
        {
            sb.Append("\n\n[Table ").Append(table.TableId).Append("]\n").Append(table.Markdown);
        }
        return sb.ToString();
    }
}
