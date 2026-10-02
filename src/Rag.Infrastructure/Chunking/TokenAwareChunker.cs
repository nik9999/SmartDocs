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

        var encoded = _tokenizer.Encode(text);
        return encoded.Length;
    }

    /// <summary>
    /// Splits text into paragraphs. Handles soft line-break hyphenation:
    /// "word-\nword" at line boundaries is merged into "wordword" when it's
    /// clearly a hyphenation break (not a real hyphenated identifier).
    ///
    /// Technical identifiers (RS-485, TCP/IP, T_sensor, cos(φ), etc.) are preserved.
    /// </summary>
    private List<string> SplitIntoParagraphs(string text)
    {
        // Step 1: Handle soft line-break hyphenation
        var normalized = NormalizeHyphenatedLineBreaks(text);

        return SplitIntoParagraphsRaw(normalized);
    }

    /// <summary>
    /// Normalizes soft line-break hyphenation in text.
    ///
    /// Detects patterns like:
    ///   "технологи-\nческий" → "технологический"
    ///   "темпера-\nтура" → "температура"
    ///
    /// While preserving real hyphenated identifiers:
    ///   "RS-485" → "RS-485" (unchanged)
    ///   "TCP/IP" → "TCP/IP" (unchanged)
    ///   "T_sensor" → "T_sensor" (unchanged)
    ///   "cos(φ)" → "cos(φ)" (unchanged)
    ///
    /// Heuristic for distinguishing hyphenation from real hyphens:
    /// - Hyphenation: short word part before hyphen (1-4 chars), word part after,
    ///   line break immediately after hyphen
    /// - Real hyphen: longer parts, or hyphen in the middle of a line (not at EOL)
    /// </summary>
    private static string NormalizeHyphenatedLineBreaks(string text)
    {
        // Pattern: word-hyphen + line-break + word
        // We need to distinguish:
        // 1. Hyphenation: "word-\nword" where both parts are short (likely a split word)
        // 2. Real hyphen: "RS-485" (longer parts, or hyphen not at EOL)
        //
        // Strategy: match "-\n" or "-\r\n" and check context.
        // If the part before the hyphen is short (1-6 chars) and consists of
        // word characters, and the part after starts with word characters,
        // treat it as hyphenation and merge.

        var result = new System.Text.StringBuilder(text.Length);
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            // Check if this line ends with a hyphen that looks like hyphenation
            if (i + 1 < lines.Length && line.Length > 0 && line[^1] == '-')
            {
                var beforeHyphen = line.Length > 1 ? line[..^1] : string.Empty;

                // Heuristic: if before-hyphen is short word chars, treat as hyphenation
                if (IsLikelyHyphenationPart(beforeHyphen))
                {
                    var nextLine = lines[i + 1];
                    // Remove trailing whitespace from next line start (already handled by Split)
                    // Merge: remove hyphen from current line, concatenate with next line
                    result.Append(beforeHyphen);

                    // Skip the hyphenated line and the next line, merge them
                    i += 2;
                    // Continue processing the merged result with next lines
                    // We need to re-append the next line content
                    // Since we're using StringBuilder, append next line now
                    // but we need to handle paragraph boundaries
                    // For now, append next line directly (it will be joined later)
                    // Use a special marker or handle at paragraph level

                    // Actually, let's handle this differently - just append
                    // the next line content right after beforeHyphen
                    // and skip the line break
                    // We'll use a placeholder for "no line break"
                    result.Append(nextLine);
                    continue;
                }
            }

            result.Append(line);
            i++;

            // Add paragraph boundary marker for non-merged lines
            if (i < lines.Length)
            {
                result.AppendLine();
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks if a string looks like the first part of a hyphenated word
    /// (short, word characters only).
    /// </summary>
    private static bool IsLikelyHyphenationPart(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 6)
            return false;

        // Should be word characters (letters, digits, underscores for identifiers like T_)
        // But NOT contain slashes, digits after letters in patterns like "RS-485"
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        // Additional check: if it contains a digit followed by more chars,
        // it might be part of "RS-485" pattern — but "RS" alone is fine
        // The key distinction: "RS" (2 letters) before hyphen at EOL is ambiguous,
        // but "RS-485" has the hyphen in the middle of a line, not at EOL.
        // Since we only match "-\n" patterns, "RS-485" won't match (485 is on same line).

        return true;
    }

    /// <summary>
    /// Splits normalized text into paragraphs (no hyphenation handling needed here).
    /// </summary>
    private static List<string> SplitIntoParagraphsRaw(string text)
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
    /// Force-splits text at real tokenizer boundaries when a single unit exceeds max.
    /// Uses token-aware splitting to stay as close to max as possible.
    /// Guarantees no chunk exceeds maxTokens.
    /// Terminates even for pathological input (single-char tokens, etc.).
    /// </summary>
    private List<string> ForceSplit(string text, int maxTokens)
    {
        var chunks = new List<string>();

        var remaining = text;
        while (remaining.Length > 0)
        {
            var tokenCount = CountTokens(remaining);

            if (tokenCount <= maxTokens)
            {
                chunks.Add(remaining.Trim());
                break;
            }

            // Binary search for the split point that gives ~maxTokens
            var splitPos = FindExactSplitByTokens(remaining, maxTokens);

            if (splitPos <= 0)
            {
                // Edge case: even a single character produces tokens >= maxTokens
                // This shouldn't happen with a proper tokenizer, but guard against it
                // Split at every character to guarantee termination
                splitPos = 1;
            }

            if (splitPos >= remaining.Length)
            {
                // Should not happen, but guard against infinite loop
                chunks.Add(remaining.Trim());
                break;
            }

            var chunk = remaining[..splitPos].Trim();
            if (string.IsNullOrEmpty(chunk))
            {
                // Avoid infinite loop on empty chunks
                splitPos++;
                chunk = remaining[..Math.Min(splitPos, remaining.Length)].Trim();
                if (string.IsNullOrEmpty(chunk))
                    break; // Cannot make progress, stop
            }

            chunks.Add(chunk);
            remaining = remaining[splitPos..];
        }

        return chunks;
    }

    /// <summary>
    /// Applies overlap between consecutive chunks by copying trailing tokens
    /// from each chunk to the beginning of the next one.
    /// Overlap is measured in real tokenizer tokens.
    /// The resulting chunk is trimmed to never exceed MaxChunkTokens.
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

                // Enforce maxTokens after overlap — trim overlap portion precisely
                var currentTokens = CountTokens(current);
                if (currentTokens >= _maxChunkTokens)
                {
                    // Current chunk alone fills the budget — no overlap
                    result.Add(current);
                }
                else
                {
                    var budgetForOverlap = _maxChunkTokens - currentTokens - 1; // -1 for space
                    if (budgetForOverlap <= 0)
                    {
                        result.Add(current);
                    }
                    else
                    {
                        // Binary search for the longest prefix of overlapText that fits
                        var overlapTokensBefore = CountTokens(overlapText);
                        var targetOverlap = Math.Min(budgetForOverlap, overlapTokensBefore);

                        // Binary search for the char position where prefix has exactly targetOverlap tokens
                        var splitPos = FindExactSplitByTokens(overlapText, targetOverlap);
                        var trimmedOverlap = overlapText[..splitPos].Trim();

                        if (string.IsNullOrWhiteSpace(trimmedOverlap))
                        {
                            result.Add(current);
                        }
                        else
                        {
                            var finalText = trimmedOverlap + " " + current;
                            var finalTokens = CountTokens(finalText);
                            // Safety: if still over (shouldn't happen), truncate
                            if (finalTokens > _maxChunkTokens)
                            {
                                // Aggressive trim: remove words from overlap end until it fits
                                var ov = trimmedOverlap;
                                while (CountTokens(ov + " " + current) > _maxChunkTokens && ov.Length > 0)
                                {
                                    var lastSpace = ov.LastIndexOf(' ');
                                    if (lastSpace > 0)
                                        ov = ov[..lastSpace].Trim();
                                    else
                                        ov = ov[..Math.Max(0, ov.Length - 1)];
                                }
                                finalText = string.IsNullOrWhiteSpace(ov) ? current : (ov + " " + current);
                            }
                            result.Add(finalText);
                        }
                    }
                }
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the trailing portion of text that corresponds to the given token count.
    /// Uses binary search over character positions with real tokenizer verification.
    /// </summary>
    private string GetTrailingTextByTokens(string text, int targetTokens)
    {
        if (string.IsNullOrWhiteSpace(text) || targetTokens <= 0)
            return string.Empty;

        var totalTokens = CountTokens(text);
        if (targetTokens >= totalTokens)
            return text.Trim();

        // We need the last targetTokens tokens.
        // Binary search for the split point: keep first (totalTokens - targetTokens) tokens.
        var keepTokens = totalTokens - targetTokens;
        var prefixTokens = keepTokens;

        // Binary search for the character position where the prefix has exactly prefixTokens
        var lo = 0;
        var hi = text.Length;
        var bestSplit = text.Length;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var prefix = text[..mid];
            var tokenCount = CountTokens(prefix);

            if (tokenCount <= prefixTokens)
            {
                bestSplit = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // bestSplit is the furthest position where prefix has <= prefixTokens
        // Verify we're close enough
        var actualTokens = CountTokens(text[..bestSplit]);
        if (actualTokens > prefixTokens)
        {
            // Adjust: find the exact position
            bestSplit = FindExactSplitByTokens(text, prefixTokens);
        }

        if (bestSplit >= text.Length)
            return text.Trim();

        // Try to find a safe boundary (space or punctuation) near bestSplit
        var safePos = FindSafeBoundary(text, bestSplit);
        return text[safePos..].Trim();
    }

    /// <summary>
    /// Finds the character position where the prefix contains at most targetTokens.
    /// Returns the furthest position where CountTokens(text[..pos]) <= targetTokens.
    /// Guarantees: the resulting prefix will never exceed targetTokens.
    /// </summary>
    private int FindExactSplitByTokens(string text, int targetTokens)
    {
        if (targetTokens <= 0)
            return 0;

        var totalTokens = CountTokens(text);
        if (targetTokens >= totalTokens)
            return text.Length;

        // Binary search: find the largest pos where CountTokens(text[..pos]) <= targetTokens
        var lo = 1;
        var hi = text.Length - 1;
        var best = 1;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var prefix = text[..mid];
            var tokenCount = CountTokens(prefix);

            if (tokenCount <= targetTokens)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds a safe boundary near a character position.
    /// Prefers whitespace, punctuation, or tokenizer token boundaries.
    /// Preserves technical identifiers (RS-485, TCP/IP, T_sensor, etc.).
    /// </summary>
    private int FindSafeBoundary(string text, int pos)
    {
        if (pos >= text.Length)
            return text.Length;

        // First, try the exact position
        if (char.IsWhiteSpace(text[pos]) || char.IsPunctuation(text[pos]))
            return pos;

        // Look forward for a safe boundary (up to 30 chars)
        for (var i = pos + 1; i <= Math.Min(pos + 30, text.Length); i++)
        {
            if (i >= text.Length)
                return text.Length;

            var c = text[i];
            if (char.IsWhiteSpace(c))
                return i + 1;
            if (c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':')
                return i + 1;
        }

        // Look backward for a safe boundary (up to 20 chars)
        for (var i = pos - 1; i >= Math.Max(0, pos - 20); i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i + 1;
        }

        // No safe boundary found, return original position
        return pos;
    }
}
