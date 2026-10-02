namespace Rag.Infrastructure.Chunking;

using Rag.Core.Documents;
using Tokenizers.DotNet;

/// <summary>
/// Audits document chunks and produces statistics about token distribution.
/// Uses the real Tokenizers.DotNet tokenizer for accurate token counts.
/// </summary>
public sealed class ChunkAudit
{
    private readonly Tokenizer _tokenizer;

    public ChunkAudit(Tokenizer tokenizer)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    }

    /// <summary>
    /// Audits a collection of documents with their chunks.
    /// </summary>
    public AuditReport AuditDocuments(IEnumerable<(Document Document, IReadOnlyList<DocumentChunk> Chunks)> documents)
    {
        var allChunks = new List<AuditChunk>();

        foreach (var (doc, chunks) in documents)
        {
            foreach (var chunk in chunks)
            {
                var tokenCount = CountTokens(chunk.Text);
                var wordCount = CountWords(chunk.Text);
                var charCount = chunk.Text.Length;

                allChunks.Add(new AuditChunk(
                    chunk.ChunkId,
                    chunk.DocumentId,
                    chunk.Position,
                    doc.Title,
                    charCount,
                    wordCount,
                    tokenCount,
                    chunk.Metadata.PageNumber,
                    chunk.Metadata.Section,
                    chunk.Text));
            }
        }

        return new AuditReport(allChunks);
    }

    /// <summary>
    /// Audits a single document's chunks.
    /// </summary>
    public AuditReport AuditDocument(Document document, IReadOnlyList<DocumentChunk> chunks)
    {
        return AuditDocuments(new[] { (document, chunks) });
    }

    /// <summary>
    /// Counts tokens in text using the real tokenizer.
    /// No fallback — throws on tokenizer failure.
    /// </summary>
    public int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return _tokenizer.Encode(text).Length;
    }

    /// <summary>
    /// Counts words in text (whitespace-separated tokens).
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

/// <summary>
/// Represents audit data for a single chunk.
/// </summary>
public sealed record AuditChunk(
    Guid ChunkId,
    Guid DocumentId,
    int ChunkIndex,
    string DocumentTitle,
    int CharacterCount,
    int WordCount,
    int TokenCount,
    int? PageNumber,
    string? Section,
    string Text);

/// <summary>
/// Contains aggregate statistics from chunk auditing.
/// </summary>
public sealed class AuditReport
{
    public IReadOnlyList<AuditChunk> Chunks { get; }
    public int DocumentCount { get; }
    public int TotalChunks { get; }

    public int MinTokens { get; }
    public int MaxTokens { get; }
    public double AverageTokens { get; }
    public int MedianTokens { get; }
    public int P90Tokens { get; }
    public int P95Tokens { get; }
    public int P99Tokens { get; }

    public int ChunksUpTo128 { get; }
    public int ChunksUpTo256 { get; }
    public int ChunksUpTo384 { get; }
    public int ChunksUpTo512 { get; }
    public int ChunksOver512 { get; }

    public AuditReport(IReadOnlyList<AuditChunk> chunks)
    {
        Chunks = chunks;
        TotalChunks = chunks.Count;

        // Group by unique document IDs
        var uniqueDocIds = new HashSet<Guid>(chunks.Select(c => c.DocumentId));
        DocumentCount = uniqueDocIds.Count;

        if (chunks.Count == 0)
        {
            MinTokens = MaxTokens = MedianTokens = P90Tokens = P95Tokens = P99Tokens = 0;
            AverageTokens = 0;
            ChunksUpTo128 = ChunksUpTo256 = ChunksUpTo384 = ChunksUpTo512 = ChunksOver512 = 0;
            return;
        }

        var tokenCounts = chunks.Select(c => c.TokenCount).OrderBy(x => x).ToList();

        MinTokens = tokenCounts[0];
        MaxTokens = tokenCounts[^1];
        AverageTokens = tokenCounts.Average();
        MedianTokens = ComputePercentile(tokenCounts, 0.5);
        P90Tokens = ComputePercentile(tokenCounts, 0.9);
        P95Tokens = ComputePercentile(tokenCounts, 0.95);
        P99Tokens = ComputePercentile(tokenCounts, 0.99);

        ChunksUpTo128 = tokenCounts.Count(t => t <= 128);
        ChunksUpTo256 = tokenCounts.Count(t => t <= 256);
        ChunksUpTo384 = tokenCounts.Count(t => t <= 384);
        ChunksUpTo512 = tokenCounts.Count(t => t <= 512);
        ChunksOver512 = tokenCounts.Count(t => t > 512);
    }

    /// <summary>
    /// Computes percentile using linear interpolation method.
    /// This is the standard "Method 7" (R-7) used by R and many statistical packages.
    /// Deterministic and correct for any sample size.
    /// </summary>
    private static int ComputePercentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        var n = sortedValues.Count;
        if (n == 0)
            return 0;
        if (n == 1)
            return sortedValues[0];

        // Linear interpolation (R-7 method)
        // rank = percentile * (n - 1)  (0-based)
        var rank = percentile * (n - 1);
        var lower = (int)Math.Floor(rank);
        var upper = Math.Min(lower + 1, n - 1);
        var fraction = rank - lower;

        return (int)Math.Round(sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]));
    }

    /// <summary>
    /// Returns a formatted audit report suitable for console output.
    /// </summary>
    public string FormatReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Chunk Audit Report ===");
        sb.AppendLine();
        sb.AppendLine($"Documents: {DocumentCount}");
        sb.AppendLine($"Chunks: {TotalChunks}");
        sb.AppendLine();
        sb.AppendLine("--- Token Distribution ---");
        sb.AppendLine($"Min tokens: {MinTokens}");
        sb.AppendLine($"Average tokens: {AverageTokens:F1}");
        sb.AppendLine($"Median tokens: {MedianTokens}");
        sb.AppendLine($"P90 tokens: {P90Tokens}");
        sb.AppendLine($"P95 tokens: {P95Tokens}");
        sb.AppendLine($"P99 tokens: {P99Tokens}");
        sb.AppendLine($"Max tokens: {MaxTokens}");
        sb.AppendLine();
        sb.AppendLine("--- Chunk Size Buckets ---");
        sb.AppendLine($"Chunks <= 128 tokens: {ChunksUpTo128}");
        sb.AppendLine($"Chunks <= 256 tokens: {ChunksUpTo256}");
        sb.AppendLine($"Chunks <= 384 tokens: {ChunksUpTo384}");
        sb.AppendLine($"Chunks <= 512 tokens: {ChunksUpTo512}");
        sb.AppendLine($"Chunks > 512 tokens: {ChunksOver512}");
        sb.AppendLine();

        // Per-document summary
        sb.AppendLine("--- Per-Document Summary ---");
        var docGroups = Chunks.GroupBy(c => c.DocumentId)
            .OrderBy(g => g.Key);

        foreach (var docGroup in docGroups)
        {
            var title = docGroup.FirstOrDefault()?.DocumentTitle ?? "(unknown)";
            sb.AppendLine($"  Document: {title} ({docGroup.Count()} chunks)");

            foreach (var chunk in docGroup.OrderBy(c => c.ChunkIndex))
            {
                var preview = chunk.Text.Length > 80 ? chunk.Text[..77] + "..." : chunk.Text;
                sb.AppendLine($"    Chunk[{chunk.ChunkIndex}]: {chunk.TokenCount} tokens, " +
                              $"{chunk.CharacterCount} chars, {chunk.WordCount} words");
                sb.AppendLine($"      \"{preview}\"");
            }
        }

        return sb.ToString();
    }
}
