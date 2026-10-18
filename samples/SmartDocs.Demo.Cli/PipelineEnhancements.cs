using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.Demo.Cli;

/// <summary>
/// 1. Загрузчик PDF ПИМФ.426439.004 РЭ с очисткой колонтитулов и разметкой заголовков ГОСТ в Markdown.
/// </summary>
public sealed class ContravtPdfDocumentLoader(HttpClient? httpClient = null) : IDocumentLoader
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    public async Task<Document> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] pdfBytes;
        string localPdfPath = path;

        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (httpClient is null)
            {
                throw new InvalidOperationException("HttpClient не сконфигурирован для загрузки по сети.");
            }

            pdfBytes = await httpClient.GetByteArrayAsync(path, cancellationToken).ConfigureAwait(false);
            var tempFile = Path.Combine(Path.GetTempPath(), "smartdocs_cache", "contravt.pdf");
            var dir = Path.GetDirectoryName(tempFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllBytesAsync(tempFile, pdfBytes, cancellationToken).ConfigureAwait(false);
            localPdfPath = tempFile;
        }
        else
        {
            pdfBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var sb = new StringBuilder();
        using var document = PdfDocument.Open(pdfBytes);

        foreach (Page page in document.GetPages())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Page {page.Number}]");
            var cleanedLines = ExtractLinesFromPage(page)
                .Where(line => !line.Contains("ПИМФ.426439.004 РЭ", StringComparison.Ordinal) && !Regex.IsMatch(line, @"^Стр\.\s*\d+$"));

            foreach (var line in cleanedLines)
            {
                if (Regex.IsMatch(line, @"^\d+(\.\d+)*\s+[А-ЯЁA-Z][а-яёa-zА-ЯЁA-Z\s\-]{3,60}$"))
                {
                    int depth = line.Split(' ')[0].Count(c => c == '.') + 1;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"\n{new string('#', Math.Min(depth, 4))} {line}\n");
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
        }

        var metadata = new DocumentMetadata(
            Id: "PIMF-426439-004-RE-v13",
            Silo: "technical-docs",
            Department: "Engineering",
            Office: "Montreal",
            ConfidentialityLevel: "Public",
            DocumentType: "Specification",
            FiscalYear: 2026,
            Author: "КонтрАвт",
            LastModified: new DateOnly(2026, 1, 1),
            Title: "ПИМФ.426439.004 РЭ v13.0 — Модули ввода-вывода серии MDS");

        return new Document(metadata, sb.ToString(), localPdfPath);
    }

    private static IEnumerable<string> ExtractLinesFromPage(Page page)
    {
        // Группируем слова в строки с допуском 3 пункта по вертикали (сверху вниз)
        return page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }
}

/// <summary>
/// 2. Фильтр картинок для PdfPigImageExtractor: отсекает мелкие иконки/полоски и дубликаты логотипов по SHA256.
/// </summary>
public sealed class FilteredPdfImageExtractor(
    IPdfImageExtractor innerExtractor,
    int minWidth = 120,
    int minHeight = 120) : IPdfImageExtractor
{
    public string Implementation => $"filtered({innerExtractor.Implementation})";

    public async Task<IReadOnlyList<ExtractedImage>> ExtractAsync(
        string pdfPath,
        string outputDirectory,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var rawImages = await innerExtractor.ExtractAsync(pdfPath, outputDirectory, documentId, cancellationToken).ConfigureAwait(false);
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExtractedImage>();

        foreach (var img in rawImages)
        {
            if (string.IsNullOrEmpty(img.ImagePath) || !File.Exists(img.ImagePath))
            {
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(img.ImagePath, cancellationToken).ConfigureAwait(false);
            if (bytes.Length < 24)
            {
                continue;
            }

            bool isPng = bytes[0] == 0x89 && bytes[1] == 0x50;
            if (isPng)
            {
                int width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
                int height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));

                if (width < minWidth || height < minHeight)
                {
                    TryDeleteFile(img.ImagePath);
                    continue;
                }

                if (Math.Min(width, height) > 0)
                {
                    double aspect = (double)Math.Max(width, height) / Math.Min(width, height);
                    if (aspect > 6.0)
                    {
                        TryDeleteFile(img.ImagePath);
                        continue;
                    }
                }
            }
            else
            {
                // Для не-PNG форматов (JPEG и др.) фильтруем мелкие иконки по размеру файла
                if (bytes.Length < 4096)
                {
                    TryDeleteFile(img.ImagePath);
                    continue;
                }
            }

            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!seenHashes.Add(hash))
            {
                TryDeleteFile(img.ImagePath);
                continue;
            }

            result.Add(img);
        }

        return result;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// 3. Декоратор Markdown-aware Chunker: сохраняет шапку таблиц при разрезании и инжектит путь заголовков.
/// Использует штатный ITokenCounter из SmartDocs.Core.
/// </summary>
public sealed class AtomicMarkdownTableChunker(
    IChunker innerTokenAwareChunker,
    ITokenCounter tokenCounter,
    int maxChunkTokens = 550) : IChunker
{
    public string Strategy => $"atomic-table({innerTokenAwareChunker.Strategy})";

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var breadcrumbs = new string[4];
        var blocks = Regex.Split(document.Content, @"(?m)(?=^#{1,4}\s)")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s));

        int chunkIndex = 0;

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string blockToProcess = block;

            if (block.StartsWith('#'))
            {
                int newlineIdx = block.IndexOf('\n');
                string headerLine = newlineIdx >= 0 ? block[..newlineIdx].Trim() : block.Trim();
                string body = newlineIdx >= 0 ? block[(newlineIdx + 1)..].Trim() : string.Empty;

                int level = headerLine.TakeWhile(c => c == '#').Count();
                if (level is >= 1 and <= 4)
                {
                    breadcrumbs[level - 1] = headerLine.TrimStart('#', ' ');
                    for (int i = level; i < 4; i++)
                    {
                        breadcrumbs[i] = string.Empty;
                    }
                }

                if (string.IsNullOrEmpty(body))
                {
                    continue;
                }

                blockToProcess = body;
            }

            string path = string.Join(" > ", breadcrumbs.Where(s => !string.IsNullOrEmpty(s)));
            string headerContext = string.IsNullOrEmpty(path)
                ? $"[Документ: {document.Metadata.Title}]\n"
                : $"[Раздел: {path}]\n";

            if (blockToProcess.Contains('|', StringComparison.Ordinal) && Regex.IsMatch(blockToProcess, @"\|\s*:?-{3,}:?\s*\|"))
            {
                foreach (var tablePart in SplitTableWithHeader(blockToProcess, headerContext, maxChunkTokens))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new DocumentChunk(
                        ChunkId: $"{document.Metadata.Id}#{chunkIndex}",
                        DocumentId: document.Metadata.Id,
                        ChunkIndex: chunkIndex++,
                        Text: tablePart,
                        StartCharOffset: 0,
                        EndCharOffset: tablePart.Length,
                        Metadata: document.Metadata);
                }
            }
            else
            {
                var subDoc = document with { Content = blockToProcess };
                await foreach (var subChunk in innerTokenAwareChunker.ChunkAsync(subDoc, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return subChunk with
                    {
                        ChunkId = $"{document.Metadata.Id}#{chunkIndex}",
                        ChunkIndex = chunkIndex++,
                        Text = headerContext + subChunk.Text
                    };
                }
            }
        }
    }

    private IEnumerable<string> SplitTableWithHeader(string tableText, string prefix, int maxTokens)
    {
        var lines = tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 2)
        {
            yield return prefix + tableText;
            yield break;
        }

        string tableHeader = lines[0] + "\n" + lines[1] + "\n";
        var batch = new StringBuilder(prefix + tableHeader);

        for (int i = 2; i < lines.Length; i++)
        {
            string candidate = batch + lines[i] + "\n";
            if (tokenCounter.CountTokens(candidate) > maxTokens && batch.Length > prefix.Length + tableHeader.Length)
            {
                yield return batch.ToString();
                batch.Clear().Append(prefix).Append(tableHeader).AppendLine(lines[i]);
            }
            else
            {
                batch.AppendLine(lines[i]);
            }
        }

        if (batch.Length > prefix.Length + tableHeader.Length)
        {
            yield return batch.ToString();
        }
    }
}


