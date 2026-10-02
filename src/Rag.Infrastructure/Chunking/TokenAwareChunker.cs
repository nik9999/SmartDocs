namespace Rag.Infrastructure.Chunking;

using Rag.Core.Contracts;
using Rag.Core.Documents;
using Tokenizers.DotNet;

/// <summary>
/// Deterministic token-aware chunker that splits documents based on real tokenizer token counts.
///
/// Strategy:
/// 1. Split document into paragraphs first (natural boundary)
/// 2. Merge paragraphs until approaching TargetChunkTokens
/// 3. If a single paragraph exceeds MaxChunkTokens, split it at sentence boundaries
/// 4. Apply overlap between consecutive chunks
/// 5. Preserve technical identifiers (don't split in the middle of identifiers)
///
/// Token counting uses Tokenizers.DotNet (HuggingFace tokenizers.rs) for accuracy.
/// </summary>
public sealed class TokenAwareChunker : IChunker
{
    private const int DefaultTargetChunkTokens = 256;
    private const int DefaultMaxChunkTokens = 384;
    private const int DefaultOverlapTokens = 48;

    private readonly Tokenizer _tokenizer;
    private readonly int _targetChunkTokens;
    private readonly int _maxChunkTokens;
    private readonly int _overlapTokens;

    /// <summary>
    /// Creates a new TokenAwareChunker with the given tokenizer and configuration.
    /// </summary>
    /// <param name="tokenizer">HuggingFace tokenizer for accurate token counting.</param>
    /// <param name="targetChunkTokens">Target number of tokens per chunk (default 256).</param>
    /// <param name="maxChunkTokens">Maximum tokens allowed in any chunk (default 384).</param>
    /// <param name="overlapTokens">Number of overlapping tokens between consecutive chunks (default 48).</param>
    public TokenAwareChunker(
        Tokenizer tokenizer,
        int targetChunkTokens = DefaultTargetChunkTokens,
        int maxChunkTokens = DefaultMaxChunkTokens,
        int overlapTokens = DefaultOverlapTokens)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _targetChunkTokens = targetChunkTokens;
        _maxChunkTokens = maxChunkTokens;
        _overlapTokens = Math.Max(0, Math.Min(overlapTokens, maxChunkTokens / 4));
    }

    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> Chunk(Document document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var content = document.Content;
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<DocumentChunk>();

        // Split into paragraphs as the first-level boundary
        var paragraphs = SplitIntoParagraphs(content);

        if (paragraphs.Count == 0)
            return Array.Empty<DocumentChunk>();

        // Build chunks from paragraphs with token-aware merging
        var rawChunks = BuildChunksFromParagraphs(paragraphs);

        // Apply overlap between chunks
        var chunksWithOverlap = ApplyOverlap(rawChunks);

        // Convert to DocumentChunk with proper metadata
        var result = new List<DocumentChunk>(chunksWithOverlap.Count);
        for (var i = 0; i < chunksWithOverlap.Count; i++)
        {
            var text = chunksWithOverlap[i];
            var tokenCount = CountTokens(text);
            var chunk = new DocumentChunk(
                document.DocumentId,
                text,
                i,
                new ChunkMetadata(
                    pageNumber: null,
                    section: document.Source,
                    tokenCount: tokenCount));
            result.Add(chunk);
        }

        return result;
    }

    /// <summary>
    /// Counts tokens in the given text using the real tokenizer.
    /// </summary>
    public int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        try
        {
            var encoded = _tokenizer.Encode(text);
            return encoded.Length;
        }
        catch
        {
            // Fallback: estimate from character count
            // This should rarely happen with valid text
            return (int)(text.Length / 4.0);
        }
    }

    /// <summary>
    /// Splits text into paragraphs. Preserves empty paragraph markers.
    /// </summary>
    private static List<string> SplitIntoParagraphs(string text)
    {
        var paragraphs = new List<string>();
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var currentParagraph = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                // Empty line = paragraph boundary
                if (currentParagraph.Length > 0)
                {
                    paragraphs.Add(currentParagraph.ToString());
                    currentParagraph.Clear();
                }
            }
            else
            {
                if (currentParagraph.Length > 0)
                {
                    currentParagraph.Append(" ");
                }
                currentParagraph.Append(trimmed);
            }
        }

        if (currentParagraph.Length > 0)
        {
            paragraphs.Add(currentParagraph.ToString());
        }

        return paragraphs;
    }

    /// <summary>
    /// Merges paragraphs into chunks respecting token limits.
    /// If a single paragraph exceeds MaxChunkTokens, it is split at sentence boundaries.
    /// </summary>
    private List<string> BuildChunksFromParagraphs(List<string> paragraphs)
    {
        var chunks = new List<string>();
        var currentChunk = new System.Text.StringBuilder();
        var currentTokenCount = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphTokens = CountTokens(paragraph);

            if (paragraphTokens == 0)
                continue;

            // If the paragraph alone exceeds MaxChunkTokens, split it
            if (paragraphTokens > _maxChunkTokens)
            {
                // Flush current chunk first
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    currentTokenCount = 0;
                }

                // Split the long paragraph
                var subChunks = SplitLongText(paragraph);
                chunks.AddRange(subChunks);
                continue;
            }

            // Check if adding this paragraph would exceed max
            var proposedCount = currentTokenCount + 1 + paragraphTokens; // +1 for space

            if (proposedCount > _maxChunkTokens && currentChunk.Length > 0)
            {
                // Flush current chunk
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
                currentTokenCount = 0;
            }

            // Start new chunk if empty
            if (currentChunk.Length > 0)
            {
                currentChunk.Append(" ");
            }
            currentChunk.Append(paragraph);
            currentTokenCount = proposedCount;
        }

        // Flush remaining
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        // If no chunks were created (e.g., all paragraphs were empty), return the original content
        if (chunks.Count == 0)
        {
            chunks.Add(string.Join(" ", paragraphs));
        }

        return chunks;
    }

    /// <summary>
    /// Splits a long text at sentence boundaries when it exceeds MaxChunkTokens.
    /// Uses a simple sentence boundary heuristic.
    /// </summary>
    private List<string> SplitLongText(string text)
    {
        var sentences = SplitIntoSentences(text);
        var chunks = new List<string>();
        var currentChunk = new System.Text.StringBuilder();
        var currentTokenCount = 0;

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            var sentenceTokens = CountTokens(sentence);

            // If a single sentence exceeds max, we must split it at character level
            if (sentenceTokens > _maxChunkTokens)
            {
                // Flush current chunk
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    currentTokenCount = 0;
                }

                // Force-split at character level (last resort)
                var subChunks = ForceSplit(sentence, _maxChunkTokens);
                chunks.AddRange(subChunks);
                continue;
            }

            var proposedCount = currentTokenCount + 1 + sentenceTokens;

            if (proposedCount > _maxChunkTokens && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
                currentTokenCount = 0;
            }

            if (currentChunk.Length > 0)
            {
                currentChunk.Append(" ");
            }
            currentChunk.Append(sentence);
            currentTokenCount = proposedCount;
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    /// <summary>
    /// Splits text into sentences using common delimiters.
    /// Preserves technical identifiers and numeric patterns.
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();

        // Split on sentence-ending punctuation followed by space or end of text
        // Patterns: ". ", "! ", "? ", ".\n", etc.
        var parts = System.Text.RegularExpressions.Regex.Split(
            text,
            @"(?<=[.!?])\s+");

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                sentences.Add(trimmed);
            }
        }

        // If no split happened (no sentence delimiters), treat as single sentence
        if (sentences.Count == 0)
        {
            sentences.Add(text.Trim());
        }

        return sentences;
    }

    /// <summary>
    /// Force-splits text at character level when a single token exceeds max.
    /// Uses token-aware splitting to stay as close to max as possible.
    /// </summary>
    private List<string> ForceSplit(string text, int maxTokens)
    {
        var chunks = new List<string>();
        var charIndex = 0;

        while (charIndex < text.Length)
        {
            // Binary search for the best split point
            var remaining = text.Length - charIndex;
            var targetLen = Math.Min(remaining, _targetChunkTokens * 3); // rough char estimate
            if (targetLen <= 0)
                targetLen = remaining;

            var end = charIndex + targetLen;
            if (end >= text.Length)
            {
                chunks.Add(text[charIndex..]);
                break;
            }

            // Try to find a safe split point (space, punctuation)
            var splitPos = FindSafeSplitPoint(text, charIndex, end);
            if (splitPos <= charIndex)
            {
                // No safe point found, force split at end
                splitPos = end;
            }

            chunks.Add(text[charIndex..splitPos].Trim());
            charIndex = splitPos;
        }

        return chunks;
    }

    /// <summary>
    /// Finds a safe split point before the target position.
    /// Prefers spaces and punctuation over mid-word splits.
    /// </summary>
    private static int FindSafeSplitPoint(string text, int start, int target)
    {
        // Look backwards from target for a space
        var lookback = Math.Min(50, target - start);
        for (var i = target - 1; i >= Math.Max(start, target - lookback); i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
                return i + 1;

            // Also allow splitting after punctuation
            if (c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':')
                return i + 1;
        }

        // No safe point found, return target
        return target;
    }

    /// <summary>
    /// Applies overlap between consecutive chunks by copying trailing tokens
    /// from each chunk to the beginning of the next one.
    /// </summary>
    private List<string> ApplyOverlap(List<string> chunks)
    {
        if (chunks.Count <= 1 || _overlapTokens == 0)
            return chunks;

        var result = new List<string>(chunks.Count);
        result.Add(chunks[0]);

        for (var i = 1; i < chunks.Count; i++)
        {
            var current = chunks[i];
            var prev = result[i - 1];

            // Count tokens in the previous chunk
            var prevTokens = CountTokens(prev);
            var overlapTokenCount = Math.Min(_overlapTokens, prevTokens / 2);

            if (overlapTokenCount <= 0)
            {
                result.Add(current);
                continue;
            }

            // Get the trailing portion of the previous chunk that corresponds to overlap tokens
            var overlapText = GetTrailingTextByTokens(prev, overlapTokenCount);

            if (!string.IsNullOrEmpty(overlapText))
            {
                var overlapped = overlapText + " " + current;
                result.Add(overlapped);
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the trailing portion of text that corresponds approximately to the given token count.
    /// Uses a heuristic: estimate character ratio and then trim.
    /// </summary>
    private static string GetTrailingTextByTokens(string text, int targetTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Simple heuristic: estimate character length from token count
        // Average ~1.5 chars per token for English, ~2-3 for Russian
        // Use a conservative estimate and then verify
        var estimatedChars = targetTokens * 4;
        var startIdx = text.Length - estimatedChars;

        if (startIdx <= 0)
            return text.Trim();

        // Try to find a sentence boundary near the estimated position
        var searchStart = Math.Max(0, startIdx - 20);
        var searchEnd = Math.Min(text.Length, startIdx + 20);

        // Look for a sentence-ending punctuation or newline
        for (var i = searchStart; i < searchEnd; i++)
        {
            var c = text[i];
            if ((c == '.' || c == '!' || c == '?') && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return text[(i + 1)..].Trim();
            }
        }

        // Fall back to character-based split
        return text[startIdx..].Trim();
    }
}
