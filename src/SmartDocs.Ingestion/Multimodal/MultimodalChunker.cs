using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.Ingestion.Multimodal;

/// <summary>
/// Chunker decorator that augments text chunks with associated <see cref="ExtractedImage"/>s
/// and <see cref="ExtractedTable"/>s based on physical document coordinates (page numbers,
/// section headings, and character offset containment).
/// Avoids naive global duplication by linking each image or table strictly to its corresponding
/// chunk rather than scattering them across all chunks of the document.
/// Also supports enforcing an optional token budget ceiling (<see cref="MaxTokens"/>)
/// using an <see cref="ITokenCounter"/>.
/// </summary>
public sealed class MultimodalChunker : IChunker
{
    private readonly IChunker _innerTextChunker;
    private readonly IReadOnlyList<ExtractedImage> _images;
    private readonly IReadOnlyList<ExtractedTable> _tables;
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
        _images = images;
        _tables = tables;
        _tokenCounter = tokenCounter;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// Build a stream of <see cref="MultimodalChunk"/>s.
    /// Resolves page boundaries and sections from the document content, then associates
    /// each image and table with its corresponding chunk based on structural coordinates.
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

        // 1. Buffer all text chunks for this document
        var rawChunks = new List<DocumentChunk>();
        await foreach (var chunk in _innerTextChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rawChunks.Add(chunk);
        }

        if (rawChunks.Count == 0)
        {
            yield break;
        }

        // 2. Index document page spans and structural sections
        var pageSpans = DetectPageSpans(document.Content);
        var chunkPages = new List<HashSet<int>>(rawChunks.Count);
        var chunkSections = new List<string?>(rawChunks.Count);

        foreach (var chunk in rawChunks)
        {
            chunkPages.Add(ResolveChunkPages(chunk, pageSpans));
            chunkSections.Add(ResolveChunkSection(chunk, document.Content));
        }

        // 3. Match images to their corresponding chunk (at most one primary chunk per image)
        var chunkImagesMap = new Dictionary<int, List<ExtractedImage>>();
        var chunkTablesMap = new Dictionary<int, List<ExtractedTable>>();

        for (int i = 0; i < rawChunks.Count; i++)
        {
            chunkImagesMap[i] = new List<ExtractedImage>();
            chunkTablesMap[i] = new List<ExtractedTable>();
        }

        foreach (var img in _images)
        {
            int bestChunkIndex = FindBestChunkForElement(
                img.PageNumber,
                img.StartCharOffset,
                img.SectionHeading,
                img.ImageId,
                rawChunks,
                chunkPages,
                chunkSections);

            if (bestChunkIndex >= 0 && bestChunkIndex < rawChunks.Count)
            {
                chunkImagesMap[bestChunkIndex].Add(img);
            }
        }

        // 4. Match tables to their corresponding chunk
        foreach (var tbl in _tables)
        {
            int bestChunkIndex = FindBestChunkForElement(
                tbl.PageNumber,
                tbl.StartCharOffset,
                tbl.SectionHeading,
                tbl.TableId,
                rawChunks,
                chunkPages,
                chunkSections,
                tbl.Markdown);

            if (bestChunkIndex >= 0 && bestChunkIndex < rawChunks.Count)
            {
                chunkTablesMap[bestChunkIndex].Add(tbl);
            }
        }

        // 5. Emit augmented MultimodalChunks with token budgeting
        for (int i = 0; i < rawChunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = rawChunks[i];
            var assignedImgs = chunkImagesMap[i];
            var assignedTbls = chunkTablesMap[i];

            var augmented = BuildAugmentedText(raw.Text, assignedImgs, assignedTbls);
            var augmentedChunk = raw with { Text = augmented };

            if (postSplitter != null && _tokenCounter!.CountTokens(augmented) > _maxTokens)
            {
                foreach (var piece in postSplitter.SplitIfNeeded(augmentedChunk))
                {
                    yield return new MultimodalChunk(piece, assignedImgs, assignedTbls);
                }
            }
            else
            {
                yield return new MultimodalChunk(augmentedChunk, assignedImgs, assignedTbls);
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

    private static int FindBestChunkForElement(
        int pageNumber,
        int startCharOffset,
        string? sectionHeading,
        string elementId,
        List<DocumentChunk> chunks,
        List<HashSet<int>> chunkPages,
        List<string?> chunkSections,
        string? elementContent = null)
    {
        // 1. Exact character offset containment
        if (startCharOffset > 0)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                if (startCharOffset >= chunks[i].StartCharOffset && startCharOffset <= chunks[i].EndCharOffset)
                {
                    return i;
                }
            }
        }

        // 2. In-text reference or element ID mention
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Text.Contains(elementId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
            if (!string.IsNullOrEmpty(elementContent) && chunks[i].Text.Contains(elementContent, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // 3. Section heading match
        if (!string.IsNullOrWhiteSpace(sectionHeading))
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                if (string.Equals(chunkSections[i], sectionHeading, StringComparison.OrdinalIgnoreCase) ||
                    chunks[i].Text.Contains(sectionHeading, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        // 4. Page number match (pick the chunk on that page closest to element offset, or first chunk of that page)
        int bestPageIndex = -1;
        int minDistance = int.MaxValue;

        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunkPages[i].Contains(pageNumber))
            {
                if (startCharOffset > 0)
                {
                    int dist = Math.Abs(chunks[i].StartCharOffset - startCharOffset);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestPageIndex = i;
                    }
                }
                else if (bestPageIndex < 0)
                {
                    bestPageIndex = i;
                }
            }
        }

        if (bestPageIndex >= 0)
        {
            return bestPageIndex;
        }

        // 5. Fallback: closest by offset or first chunk
        if (startCharOffset > 0)
        {
            int closest = 0;
            int dist = int.MaxValue;
            for (int i = 0; i < chunks.Count; i++)
            {
                int d = Math.Abs(chunks[i].StartCharOffset - startCharOffset);
                if (d < dist)
                {
                    dist = d;
                    closest = i;
                }
            }
            return closest;
        }

        return 0;
    }

    private sealed record PageSpan(int PageNumber, int StartOffset, int EndOffset);

    private static List<PageSpan> DetectPageSpans(string content)
    {
        var spans = new List<PageSpan>();
        if (string.IsNullOrEmpty(content))
        {
            spans.Add(new PageSpan(1, 0, 0));
            return spans;
        }

        // Matches patterns like "## Page 1", "[Page 1]", "--- Page 1 ---"
        var regex = new Regex(
            @"(?:^|\n)\s*(?:##\s*Page\s+|\[Page\s+|---\s*Page\s+)(\d+)",
            RegexOptions.IgnoreCase);

        var matches = regex.Matches(content);
        if (matches.Count == 0)
        {
            spans.Add(new PageSpan(1, 0, content.Length));
            return spans;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int pageNum = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            int startOffset = match.Index;
            int endOffset = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
            spans.Add(new PageSpan(pageNum, startOffset, endOffset));
        }

        return spans;
    }

    private static HashSet<int> ResolveChunkPages(DocumentChunk chunk, List<PageSpan> pageSpans)
    {
        var pages = new HashSet<int>();

        if (chunk.EndCharOffset > chunk.StartCharOffset && pageSpans.Count > 0)
        {
            foreach (var span in pageSpans)
            {
                if (chunk.StartCharOffset < span.EndOffset && chunk.EndCharOffset > span.StartOffset)
                {
                    pages.Add(span.PageNumber);
                }
            }
        }

        if (pages.Count == 0)
        {
            var match = Regex.Match(chunk.Text, @"(?:##\s*Page\s+|\[Page\s+)(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int p))
            {
                pages.Add(p);
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(1);
        }

        return pages;
    }

    private static string? ResolveChunkSection(DocumentChunk chunk, string docContent)
    {
        var matchInText = Regex.Match(chunk.Text, @"(?m)^#{1,3}\s+(.+)$");
        if (matchInText.Success)
        {
            return matchInText.Groups[1].Value.Trim();
        }

        if (chunk.StartCharOffset > 0 && chunk.StartCharOffset <= docContent.Length)
        {
            var prefix = docContent[..chunk.StartCharOffset];
            var matchInDoc = Regex.Matches(prefix, @"(?m)^#{1,3}\s+(.+)$");
            if (matchInDoc.Count > 0)
            {
                return matchInDoc[^1].Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static string BuildAugmentedText(
        string text,
        List<ExtractedImage> images,
        List<ExtractedTable> tables)
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

            // Do not duplicate if caption or figure ID is already in the chunk's text
            if (text.Contains($"[Figure {img.ImageId}]", StringComparison.OrdinalIgnoreCase) ||
                text.Contains(img.Caption, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append("\n\n[Figure ").Append(img.ImageId).Append("] ").Append(img.Caption);
        }

        foreach (var table in tables)
        {
            if (string.IsNullOrWhiteSpace(table.Markdown))
            {
                continue;
            }

            // Do not duplicate if table markdown or table ID is already in the chunk's text
            if (text.Contains($"[Table {table.TableId}]", StringComparison.OrdinalIgnoreCase) ||
                text.Contains(table.Markdown, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Check if the first row (header) of the table is already in the chunk's text
            var firstLine = table.Markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstLine) && text.Contains(firstLine, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append("\n\n[Table ").Append(table.TableId).Append("]\n").Append(table.Markdown);
        }

        return sb.ToString();
    }
}
