using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartDocs.Ingestion.Loaders;

/// <summary>
/// Production PDF document loader backed by PdfPig that opens PDF files,
/// extracts raw text page-by-page preserving structural page markers,
/// extracts document metadata, and provides clear error handling for
/// missing files, corrupted PDFs, and image-only/empty documents.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader
{
    private static readonly HashSet<string> DefaultSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly Regex CodeRegex = new(
        @"\b([А-ЯA-Z]{2,6}\.\d{3,6}\.\d{3,6}(\s*[А-ЯA-Z]{2,4})?(\s*v?\d+(\.\d+)?)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NonIdCharactersRegex = new(
        @"[^\p{L}\p{Nd}\.\-_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearRegex = new(
        @"\b(19\d{2}|20\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CityRegex = new(
        @"(Москва|Санкт-Петербург|Нижний Новгород|Екатеринбург|Новосибирск|Казань|Самара|Челябинск|Уфа|Ростов-на-Дону|Montreal|Paris|Casablanca|London|Berlin|New York)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OrgRegex = new(
        @"(ООО|ЗАО|ОАО|ПАО|АО|НПФ|НПО|Предприятие|Завод|Компания|«[^»]+»|""[^""]+"")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OrgExtractorRegex = new(
        @"(«[^»]+»|""[^""]+""|[А-ЯA-Z][а-яa-zА-ЯA-Z\s\-]{3,30})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InvalidTitleRegex = new(
        @"^(untitled|содержание|оглавление|введение|документ|document|table of contents|microsoft word)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BoilerplateTitleLineRegex = new(
        @"^(ООО|ЗАО|ОАО|ПАО|АО|НПФ|НПО|тел|факс|www\.|http|e-mail|содержание|оглавление|введение|стр\.)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly PdfDocumentLoaderOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfDocumentLoader"/>.
    /// </summary>
    /// <param name="options">Optional loader configuration options.</param>
    public PdfDocumentLoader(PdfDocumentLoaderOptions? options = null)
    {
        _options = options ?? new PdfDocumentLoaderOptions();
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> SupportedExtensions => DefaultSupportedExtensions;

    /// <inheritdoc/>
    public Task<Document> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(path));
    }

    /// <summary>
    /// Synchronously loads a PDF document from disk.
    /// </summary>
    /// <param name="path">Path to the PDF file.</param>
    /// <returns>A loaded <see cref="Document"/> containing raw text and metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the PDF file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the PDF is corrupted or cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no text could be extracted and throwing is enabled.</exception>
    public Document Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "PDF file not found: '{0}'.", path),
                path);
        }

        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(path);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "PDF file '{0}' is corrupted or could not be parsed: {1}", path, ex.Message),
                ex);
        }

        using (pdf)
        {
            return ParsePdfDocument(pdf, path);
        }
    }

    /// <summary>
    /// Synchronously loads a PDF document from a stream.
    /// </summary>
    /// <param name="stream">Readable stream containing the PDF data.</param>
    /// <param name="sourcePath">Logical path or identifier for metadata and error reporting.</param>
    /// <returns>A loaded <see cref="Document"/> containing raw text and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourcePath"/> is null or whitespace.</exception>
    /// <exception cref="InvalidDataException">Thrown when the PDF stream is corrupted or cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no text could be extracted and throwing is enabled.</exception>
    public Document Load(Stream stream, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(stream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "PDF stream for '{0}' is corrupted or could not be parsed: {1}", sourcePath, ex.Message),
                ex);
        }

        using (pdf)
        {
            return ParsePdfDocument(pdf, sourcePath);
        }
    }

    /// <summary>
    /// Asynchronously loads a PDF document from a stream.
    /// </summary>
    /// <param name="stream">Readable stream containing the PDF data.</param>
    /// <param name="sourcePath">Logical path or identifier for metadata and error reporting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A loaded <see cref="Document"/> containing raw text and metadata.</returns>
    public Task<Document> LoadAsync(Stream stream, string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(stream, sourcePath));
    }

    private Document ParsePdfDocument(PdfDocument pdf, string sourcePath)
    {
        int pageCount = pdf.NumberOfPages;
        if (pageCount == 0)
        {
            if (_options.ThrowIfNoTextExtracted)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "PDF file '{0}' contains no pages.", sourcePath));
            }

            var emptyMeta = _options.MetadataExtractor != null
                ? _options.MetadataExtractor(sourcePath, pdf)
                : ExtractMetadata(pdf, sourcePath, []);

            return new Document(emptyMeta, string.Empty, sourcePath);
        }

        var sb = new StringBuilder();
        var page1Lines = new List<string>();
        bool hasAnyText = false;

        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
        {
            Page page;
            try
            {
                page = pdf.GetPage(pageNum);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException(
                    string.Format(CultureInfo.InvariantCulture, "Failed to read page {0} of PDF '{1}': {2}", pageNum, sourcePath, ex.Message),
                    ex);
            }

            var lines = ExtractLinesFromPage(page, _options.LineClusteringTolerance).ToList();
            if (pageNum == 1)
            {
                page1Lines.AddRange(lines);
            }

            if (lines.Count > 0)
            {
                hasAnyText = true;

                if (!string.IsNullOrWhiteSpace(_options.PageSeparatorTemplate))
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine().AppendLine();
                    }

                    sb.AppendFormat(CultureInfo.InvariantCulture, _options.PageSeparatorTemplate, pageNum);
                    sb.AppendLine().AppendLine();
                }
                else if (sb.Length > 0)
                {
                    sb.AppendLine().AppendLine();
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    sb.AppendLine(lines[i]);
                }
            }
        }

        string fullContent = hasAnyText ? sb.ToString().TrimEnd() : string.Empty;

        if (!hasAnyText || string.IsNullOrWhiteSpace(fullContent))
        {
            if (_options.ThrowIfNoTextExtracted)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "No text could be extracted from PDF '{0}' ({1} page(s)). The document might be scanned (image-only without an OCR layer), empty, or using an unsupported font encoding.",
                        sourcePath,
                        pageCount));
            }
        }

        var metadata = _options.MetadataExtractor != null
            ? _options.MetadataExtractor(sourcePath, pdf)
            : ExtractMetadata(pdf, sourcePath, page1Lines);

        return new Document(metadata, fullContent, sourcePath);
    }

    private static IEnumerable<string> ExtractLinesFromPage(Page page, double tolerance)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            var rawText = page.Text;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                yield break;
            }

            using var reader = new StringReader(rawText);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
            yield break;
        }

        var clustered = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / tolerance) * tolerance)
            .OrderByDescending(static g => g.Key)
            .Select(static g => string.Join(" ", g.OrderBy(static w => w.BoundingBox.Left).Select(static w => w.Text)).Trim())
            .Where(static l => l.Length > 0);

        foreach (var line in clustered)
        {
            yield return line;
        }
    }

    private DocumentMetadata ExtractMetadata(PdfDocument document, string sourcePath, IReadOnlyList<string> page1Lines)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);

        // 1. Поиск шифра документа (ГОСТ, ТУ, ПИМФ и т.д.)
        string detectedDocCode = string.Empty;
        foreach (var line in page1Lines)
        {
            var match = CodeRegex.Match(line);
            if (match.Success)
            {
                detectedDocCode = match.Value.Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(detectedDocCode))
        {
            var match = CodeRegex.Match(fileName);
            if (match.Success)
            {
                detectedDocCode = match.Value.Trim();
            }
        }

        // 2. Стабильный ID документа
        string rawId = !string.IsNullOrWhiteSpace(detectedDocCode) ? detectedDocCode : fileName;
        string id = NonIdCharactersRegex.Replace(rawId, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "doc-" + Guid.NewGuid().ToString("N")[..8];
        }

        // 3. Заголовок (Title)
        string title = string.Empty;
        if (!string.IsNullOrWhiteSpace(document.Information.Title) &&
            !InvalidTitleRegex.IsMatch(document.Information.Title) &&
            !document.Information.Title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            title = document.Information.Title.Trim();
        }

        if (string.IsNullOrEmpty(title))
        {
            var candidateTitleLines = page1Lines
                .Where(static l => l.Length > 6 &&
                            !BoilerplateTitleLineRegex.IsMatch(l) &&
                            !CodeRegex.IsMatch(l))
                .Take(2)
                .ToList();

            if (candidateTitleLines.Count > 0)
            {
                string combined = string.Join(" — ", candidateTitleLines);
                title = !string.IsNullOrEmpty(detectedDocCode) && !combined.Contains(detectedDocCode, StringComparison.OrdinalIgnoreCase)
                    ? string.Format(CultureInfo.InvariantCulture, "{0} — {1}", detectedDocCode, combined)
                    : combined;
            }
        }

        if (string.IsNullOrEmpty(title))
        {
            title = fileName;
        }

        // 4. Автор (Author)
        string author = string.Empty;
        string company = string.Empty;

        var orgLine = page1Lines.FirstOrDefault(static l => OrgRegex.IsMatch(l));
        if (!string.IsNullOrEmpty(orgLine))
        {
            var match = OrgExtractorRegex.Match(orgLine);
            company = match.Success ? match.Value.Trim(' ', '«', '»', '"') : orgLine.Trim();
        }

        if (!string.IsNullOrWhiteSpace(document.Information.Author) &&
            !document.Information.Author.Contains("User", StringComparison.OrdinalIgnoreCase) &&
            !document.Information.Author.Contains("Admin", StringComparison.OrdinalIgnoreCase))
        {
            author = document.Information.Author.Trim();
            if (!string.IsNullOrEmpty(company) && !author.Contains(company, StringComparison.OrdinalIgnoreCase))
            {
                author = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", author, company);
            }
        }
        else if (!string.IsNullOrEmpty(company))
        {
            author = company;
        }
        else
        {
            author = _options.DefaultAuthor;
        }

        // 5. FiscalYear (год)
        int fiscalYear = 0;
        int currentYear = DateTime.UtcNow.Year;
        foreach (var line in page1Lines.Reverse())
        {
            var match = YearRegex.Match(line);
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yr)
                && yr >= 1990 && yr <= currentYear)
            {
                fiscalYear = yr;
                break;
            }
        }

        if (fiscalYear == 0 && File.Exists(sourcePath))
        {
            var fileYear = File.GetLastWriteTime(sourcePath).Year;
            fiscalYear = fileYear <= currentYear ? fileYear : currentYear;
        }

        if (fiscalYear == 0)
        {
            fiscalYear = currentYear;
        }

        // 6. LastModified
        DateOnly lastModified;
        if (File.Exists(sourcePath))
        {
            lastModified = DateOnly.FromDateTime(File.GetLastWriteTime(sourcePath));
        }
        else if (!string.IsNullOrWhiteSpace(document.Information.ModifiedDate) &&
                 DateTime.TryParse(document.Information.ModifiedDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var modDate))
        {
            lastModified = DateOnly.FromDateTime(modDate);
        }
        else
        {
            lastModified = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        // 7. Город / Офис (Office)
        string office = _options.DefaultOffice;
        var cityLine = page1Lines.FirstOrDefault(static l => CityRegex.IsMatch(l));
        if (!string.IsNullOrEmpty(cityLine))
        {
            var cityMatch = CityRegex.Match(cityLine);
            if (cityMatch.Success)
            {
                office = cityMatch.Value;
            }
        }

        // 8. Silo
        string silo = _options.DefaultSilo;
        try
        {
            var parentDir = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(sourcePath)));
            if (!string.IsNullOrWhiteSpace(parentDir) &&
                parentDir is "technical-docs" or "hr-policies" or "financial-reports" or "legal-contracts" or "product-catalog" or "release-notes-tickets")
            {
                silo = parentDir;
            }
        }
        catch
        {
            // Ignore path inspection errors
        }

        // 9. DocumentType
        string docType = _options.DefaultDocumentType;
        string lowerFull = (title + " " + string.Join(" ", page1Lines)).ToLowerInvariant();
        if (lowerFull.Contains("руководство по эксплуатации") || lowerFull.Contains("инструкция") || lowerFull.Contains("руководство пользователя") || lowerFull.Contains("manual"))
        {
            docType = "Runbook";
        }
        else if (lowerFull.Contains("паспорт") || lowerFull.Contains("технические условия") || lowerFull.Contains("спецификация") || lowerFull.Contains("specification"))
        {
            docType = "Specification";
        }
        else if (lowerFull.Contains("политика") || lowerFull.Contains("регламент") || lowerFull.Contains("policy"))
        {
            docType = "Policy";
        }
        else if (lowerFull.Contains("отчет") || lowerFull.Contains("report"))
        {
            docType = "Report";
        }

        // 10. ConfidentialityLevel
        string confidentiality = _options.DefaultConfidentialityLevel;
        if (lowerFull.Contains("конфиденциально") || lowerFull.Contains("confidential"))
        {
            confidentiality = "Confidential";
        }
        else if (lowerFull.Contains("для служебного пользования") || lowerFull.Contains("дсп") || lowerFull.Contains("restricted"))
        {
            confidentiality = "Restricted";
        }

        return new DocumentMetadata(
            Id: id,
            Silo: silo,
            Department: _options.DefaultDepartment,
            Office: office,
            ConfidentialityLevel: confidentiality,
            DocumentType: docType,
            FiscalYear: fiscalYear,
            Author: author,
            LastModified: lastModified,
            Title: title);
    }
}
