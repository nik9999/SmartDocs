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
        var (metadata, detectedDocCode) = ExtractMetadata(document, localPdfPath);

        foreach (Page page in document.GetPages())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Page {page.Number}]");
            var cleanedLines = ExtractLinesFromPage(page)
                .Where(line => (string.IsNullOrEmpty(detectedDocCode) || !line.Contains(detectedDocCode, StringComparison.OrdinalIgnoreCase)) &&
                               !Regex.IsMatch(line, @"^(Стр\.|Страница|Page)\s*\d+$", RegexOptions.IgnoreCase));

            foreach (var line in cleanedLines)
            {
                if (Regex.IsMatch(line, @"^\d+(\.\d+)*\s+[А-ЯЁA-Z][а-яёa-zА-ЯЁA-Z\s\-]{3,60}$"))
                {
                    int depth = line.Split(' ')[0].Count(c => c == '.') + 1;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"\n{new string('#', Math.Clamp(depth, 1, 6))} {line}\n");
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
        }

        return new Document(metadata, sb.ToString(), localPdfPath);
    }

    private static (DocumentMetadata Metadata, string DetectedDocCode) ExtractMetadata(PdfDocument document, string localPdfPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(localPdfPath);

        // 1. Извлечение строк первой страницы
        var page1Lines = document.NumberOfPages > 0
            ? ExtractLinesFromPage(document.GetPage(1)).ToList()
            : new List<string>();

        // 2. Поиск шифра документа (ГОСТ, ТУ, ПИМФ и т.д.)
        string detectedDocCode = string.Empty;
        var codeRegex = new Regex(@"\b([А-ЯA-Z]{2,6}\.\d{3,6}\.\d{3,6}(\s*[А-ЯA-Z]{2,4})?(\s*v?\d+(\.\d+)?)?)\b", RegexOptions.IgnoreCase);
        foreach (var line in page1Lines)
        {
            var match = codeRegex.Match(line);
            if (match.Success)
            {
                detectedDocCode = match.Value.Trim();
                break;
            }
        }
        if (string.IsNullOrEmpty(detectedDocCode))
        {
            var match = codeRegex.Match(fileName);
            if (match.Success)
            {
                detectedDocCode = match.Value.Trim();
            }
        }

        // 3. Формирование стабильного ID документа
        string rawId = !string.IsNullOrWhiteSpace(detectedDocCode) ? detectedDocCode : fileName;
        string id = Regex.Replace(rawId, @"[^\p{L}\p{Nd}\.\-]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "doc-" + Guid.NewGuid().ToString("N")[..8];
        }

        // 4. Определение заголовка (Title)
        string title = string.Empty;
        if (!string.IsNullOrWhiteSpace(document.Information.Title) &&
            !Regex.IsMatch(document.Information.Title, @"^(untitled|содержание|оглавление|введение|документ|document|table of contents|microsoft word)", RegexOptions.IgnoreCase) &&
            !document.Information.Title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            title = document.Information.Title.Trim();
        }

        if (string.IsNullOrEmpty(title))
        {
            var candidateTitleLines = page1Lines
                .Where(l => l.Length > 6 &&
                            !Regex.IsMatch(l, @"^(ООО|ЗАО|ОАО|ПАО|АО|НПФ|НПО|тел|факс|www\.|http|e-mail|содержание|оглавление|введение|стр\.)", RegexOptions.IgnoreCase) &&
                            !codeRegex.IsMatch(l))
                .Take(2)
                .ToList();

            if (candidateTitleLines.Count > 0)
            {
                string combined = string.Join(" — ", candidateTitleLines);
                title = !string.IsNullOrEmpty(detectedDocCode) && !combined.Contains(detectedDocCode, StringComparison.OrdinalIgnoreCase)
                    ? $"{detectedDocCode} — {combined}"
                    : combined;
            }
        }

        if (string.IsNullOrEmpty(title))
        {
            title = fileName;
        }

        // 5. Определение автора (Author)
        string author = string.Empty;
        string company = string.Empty;

        var orgLine = page1Lines.FirstOrDefault(l =>
            Regex.IsMatch(l, @"(ООО|ЗАО|ОАО|ПАО|АО|НПФ|НПО|Предприятие|Завод|Компания|«[^»]+»|""[^""]+"")", RegexOptions.IgnoreCase));
        if (!string.IsNullOrEmpty(orgLine))
        {
            var match = Regex.Match(orgLine, @"(«[^»]+»|""[^""]+""|[А-ЯA-Z][а-яa-zА-ЯA-Z\s\-]{3,30})");
            company = match.Success ? match.Value.Trim(' ', '«', '»', '"') : orgLine.Trim();
        }

        if (!string.IsNullOrWhiteSpace(document.Information.Author) &&
            !document.Information.Author.Contains("User", StringComparison.OrdinalIgnoreCase) &&
            !document.Information.Author.Contains("Admin", StringComparison.OrdinalIgnoreCase))
        {
            author = document.Information.Author.Trim();
            if (!string.IsNullOrEmpty(company) && !author.Contains(company, StringComparison.OrdinalIgnoreCase))
            {
                author = $"{author} ({company})";
            }
        }
        else if (!string.IsNullOrEmpty(company))
        {
            author = company;
        }
        else
        {
            author = "Unknown";
        }

        // 6. Определение года (FiscalYear: год издания/редакции, не превышающий текущий)
        int fiscalYear = 0;
        int currentYear = DateTime.UtcNow.Year;
        var yearRegex = new Regex(@"\b(19\d{2}|20\d{2})\b");
        foreach (var line in page1Lines.AsEnumerable().Reverse())
        {
            var match = yearRegex.Match(line);
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yr)
                && yr >= 1990 && yr <= currentYear)
            {
                fiscalYear = yr;
                break;
            }
        }

        if (fiscalYear == 0 && File.Exists(localPdfPath))
        {
            var fileYear = File.GetLastWriteTime(localPdfPath).Year;
            fiscalYear = fileYear <= currentYear ? fileYear : currentYear;
        }
        if (fiscalYear == 0)
        {
            fiscalYear = currentYear;
        }

        // 7. Определение даты последнего изменения (LastModified)
        DateOnly lastModified = File.Exists(localPdfPath)
            ? DateOnly.FromDateTime(File.GetLastWriteTime(localPdfPath))
            : DateOnly.FromDateTime(DateTime.UtcNow);

        // 8. Город / Офис (Office)
        string office = "Central";
        var cityLine = page1Lines.FirstOrDefault(l =>
            Regex.IsMatch(l, @"(Москва|Санкт-Петербург|Нижний Новгород|Екатеринбург|Новосибирск|Казань|Самара|Челябинск|Уфа)", RegexOptions.IgnoreCase));
        if (!string.IsNullOrEmpty(cityLine))
        {
            var cityMatch = Regex.Match(cityLine, @"(Москва|Санкт-Петербург|Нижний Новгород|Екатеринбург|Новосибирск|Казань|Самара|Челябинск|Уфа)", RegexOptions.IgnoreCase);
            if (cityMatch.Success)
            {
                office = cityMatch.Value;
            }
        }

        // 9. Тип документа (DocumentType)
        string docType = "Specification";
        string lowerFull = (title + " " + string.Join(" ", page1Lines)).ToLowerInvariant();
        if (lowerFull.Contains("руководство по эксплуатации") || lowerFull.Contains("инструкция") || lowerFull.Contains("руководство пользователя"))
        {
            docType = "Runbook";
        }
        else if (lowerFull.Contains("паспорт") || lowerFull.Contains("технические условия") || lowerFull.Contains("спецификация"))
        {
            docType = "Specification";
        }
        else if (lowerFull.Contains("политика") || lowerFull.Contains("регламент"))
        {
            docType = "Policy";
        }

        var meta = new DocumentMetadata(
            Id: id,
            Silo: "technical-docs",
            Department: "Engineering",
            Office: office,
            ConfidentialityLevel: "Public",
            DocumentType: docType,
            FiscalYear: fiscalYear,
            Author: StripNoiseSymbols(author),
            LastModified: lastModified,
            Title: StripNoiseSymbols(title));

        return (meta, detectedDocCode);
    }

    private static IEnumerable<string> ExtractLinesFromPage(Page page)
    {
        // Группируем слова в строки с допуском 3 пункта по вертикали (сверху вниз)
        return page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
            .OrderByDescending(g => g.Key)
            .Select(g => StripNoiseSymbols(string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)).Trim()))
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string StripNoiseSymbols(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("®", "", StringComparison.Ordinal)
            .Replace("™", "", StringComparison.Ordinal)
            .Replace("©", "", StringComparison.Ordinal);
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
        const int MaxHeaderLevel = 6;
        var breadcrumbs = new string[MaxHeaderLevel];
        var blocks = Regex.Split(document.Content, @"(?m)(?=^#{1,6}\s)")
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
                if (level >= 1 && level <= breadcrumbs.Length)
                {
                    breadcrumbs[level - 1] = headerLine.TrimStart('#', ' ');
                    for (int i = level; i < breadcrumbs.Length; i++)
                    {
                        breadcrumbs[i] = string.Empty;
                    }
                }
                else if (level > breadcrumbs.Length)
                {
                    breadcrumbs[^1] = headerLine.TrimStart('#', ' ');
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


