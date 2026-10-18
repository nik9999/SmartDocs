using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Loaders;

/// <summary>
/// Production Microsoft Word (.docx) document loader backed by native OpenXML
/// zip decompression and XML parsing. Extracts raw text, headings, tables (as Markdown),
/// preserves page/section breaks, extracts metadata, and provides clear error handling.
/// </summary>
public sealed class DocxDocumentLoader : IDocumentLoader
{
    private static readonly HashSet<string> DefaultSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx"
    };

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Dcterms = "http://purl.org/dc/terms/";

    private static readonly Regex NonIdCharactersRegex = new(
        @"[^\p{L}\p{Nd}\.\-_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly DocxDocumentLoaderOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="DocxDocumentLoader"/>.
    /// </summary>
    /// <param name="options">Optional loader configuration options.</param>
    public DocxDocumentLoader(DocxDocumentLoaderOptions? options = null)
    {
        _options = options ?? new DocxDocumentLoaderOptions();
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
    /// Synchronously loads a Word (.docx) document from disk.
    /// </summary>
    /// <param name="path">Path to the .docx file.</param>
    /// <returns>A loaded <see cref="Document"/> containing raw text and metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the .docx file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the .docx is corrupted or cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no text could be extracted and throwing is enabled.</exception>
    public Document Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Word document file not found: '{0}'.", path),
                path);
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Load(stream, path);
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Word document '{0}' is corrupted or could not be parsed: {1}", path, ex.Message),
                ex);
        }
    }

    /// <summary>
    /// Synchronously loads a Word (.docx) document from a stream.
    /// </summary>
    /// <param name="stream">Readable stream containing the .docx package.</param>
    /// <param name="sourcePath">Logical path or identifier for metadata and error reporting.</param>
    /// <returns>A loaded <see cref="Document"/> containing raw text and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourcePath"/> is null or whitespace.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream is corrupted or not a valid Word package.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no text could be extracted and throwing is enabled.</exception>
    public Document Load(Stream stream, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Word package stream for '{0}' is corrupted or not a valid ZIP archive: {1}", sourcePath, ex.Message),
                ex);
        }

        using (zip)
        {
            return ParseDocxArchive(zip, sourcePath);
        }
    }

    /// <summary>
    /// Asynchronously loads a Word (.docx) document from a stream.
    /// </summary>
    public Task<Document> LoadAsync(Stream stream, string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(stream, sourcePath));
    }

    private Document ParseDocxArchive(ZipArchive zip, string sourcePath)
    {
        var docEntry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Word document '{0}' is missing 'word/document.xml'.", sourcePath));

        XDocument xdoc;
        try
        {
            using var entryStream = docEntry.Open();
            xdoc = XDocument.Load(entryStream);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Failed to parse 'word/document.xml' in '{0}': {1}", sourcePath, ex.Message),
                ex);
        }

        var body = xdoc.Root?.Element(W + "body");
        if (body == null)
        {
            if (_options.ThrowIfNoTextExtracted)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Word document '{0}' contains an empty body.", sourcePath));
            }

            var emptyMeta = ExtractDocxMetadata(zip, sourcePath, string.Empty);
            return new Document(emptyMeta, string.Empty, sourcePath);
        }

        var sb = new StringBuilder();
        int currentPage = 1;
        bool hasAnyText = false;
        string firstHeading = string.Empty;

        if (!string.IsNullOrWhiteSpace(_options.PageSeparatorTemplate))
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, _options.PageSeparatorTemplate, currentPage);
            sb.AppendLine().AppendLine();
        }

        foreach (var element in body.Elements())
        {
            // Check for explicit page breaks
            if (element.Descendants().Any(static d => (d.Name.LocalName == "br" && d.Attribute(W + "type")?.Value == "page") || d.Name.LocalName == "lastRenderedPageBreak"))
            {
                currentPage++;
                if (!string.IsNullOrWhiteSpace(_options.PageSeparatorTemplate))
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine().AppendLine();
                    }

                    sb.AppendFormat(CultureInfo.InvariantCulture, _options.PageSeparatorTemplate, currentPage);
                    sb.AppendLine().AppendLine();
                }
            }

            if (element.Name == W + "p")
            {
                var style = element.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
                var pText = string.Concat(element.Descendants(W + "t").Select(static t => t.Value)).Trim();

                if (string.IsNullOrEmpty(pText))
                {
                    continue;
                }

                hasAnyText = true;

                if (IsHeadingStyle(style, 1))
                {
                    if (string.IsNullOrEmpty(firstHeading))
                    {
                        firstHeading = pText;
                    }

                    sb.Append("# ").AppendLine(pText).AppendLine();
                }
                else if (IsHeadingStyle(style, 2))
                {
                    sb.Append("## ").AppendLine(pText).AppendLine();
                }
                else if (IsHeadingStyle(style, 3))
                {
                    sb.Append("### ").AppendLine(pText).AppendLine();
                }
                else if (IsHeadingStyle(style, 4))
                {
                    sb.Append("#### ").AppendLine(pText).AppendLine();
                }
                else
                {
                    sb.AppendLine(pText).AppendLine();
                }
            }
            else if (element.Name == W + "tbl")
            {
                var tableMd = ConvertWordTableToMarkdown(element);
                if (!string.IsNullOrWhiteSpace(tableMd))
                {
                    hasAnyText = true;
                    sb.AppendLine(tableMd).AppendLine();
                }
            }
        }

        string fullContent = hasAnyText ? sb.ToString().Trim() : string.Empty;

        if (!hasAnyText || string.IsNullOrWhiteSpace(fullContent))
        {
            if (_options.ThrowIfNoTextExtracted)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "No text could be extracted from Word document '{0}'.", sourcePath));
            }
        }

        var metadata = _options.MetadataExtractor != null
            ? _options.MetadataExtractor(sourcePath)
            : ExtractDocxMetadata(zip, sourcePath, firstHeading);

        return new Document(metadata, fullContent, sourcePath);
    }

    private static bool IsHeadingStyle(string? style, int level)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return false;
        }

        string s = style.Replace(" ", string.Empty, StringComparison.Ordinal);
        string levelStr = level.ToString(CultureInfo.InvariantCulture);

        return s.Equals("Heading" + levelStr, StringComparison.OrdinalIgnoreCase) ||
               s.Equals("Заголовок" + levelStr, StringComparison.OrdinalIgnoreCase) ||
               s.Equals("Titre" + levelStr, StringComparison.OrdinalIgnoreCase) ||
               s.Equals("h" + levelStr, StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertWordTableToMarkdown(XElement tbl)
    {
        var rows = tbl.Elements(W + "tr").ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var headerCells = rows[0].Elements(W + "tc")
            .Select(static tc => string.Concat(tc.Descendants(W + "t").Select(static t => t.Value)).Trim())
            .ToList();

        if (headerCells.Count == 0)
        {
            return string.Empty;
        }

        sb.Append("| ").Append(string.Join(" | ", headerCells)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", headerCells.Select(static _ => "---"))).AppendLine(" |");

        for (int i = 1; i < rows.Count; i++)
        {
            var cells = rows[i].Elements(W + "tc")
                .Select(static tc => string.Concat(tc.Descendants(W + "t").Select(static t => t.Value)).Trim())
                .ToList();

            while (cells.Count < headerCells.Count)
            {
                cells.Add(string.Empty);
            }

            sb.Append("| ").Append(string.Join(" | ", cells.Take(headerCells.Count))).AppendLine(" |");
        }

        return sb.ToString().TrimEnd();
    }

    private DocumentMetadata ExtractDocxMetadata(ZipArchive zip, string sourcePath, string firstHeading)
    {
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        string id = NonIdCharactersRegex.Replace(fileName, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(id))
        {
            id = "doc-" + Guid.NewGuid().ToString("N")[..8];
        }

        string title = string.Empty;
        string author = string.Empty;
        DateTime? modifiedDate = null;

        var coreEntry = zip.GetEntry("docProps/core.xml");
        if (coreEntry != null)
        {
            try
            {
                using var stream = coreEntry.Open();
                var coreDoc = XDocument.Load(stream);
                title = coreDoc.Descendants(Dc + "title").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                author = coreDoc.Descendants(Dc + "creator").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                var modStr = coreDoc.Descendants(Dcterms + "modified").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(modStr) && DateTime.TryParse(modStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    modifiedDate = dt;
                }
            }
            catch
            {
                // Core properties optional
            }
        }

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(firstHeading))
        {
            title = firstHeading;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = fileName;
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            author = _options.DefaultAuthor;
        }

        int currentYear = DateTime.UtcNow.Year;
        int fiscalYear = currentYear;
        if (modifiedDate.HasValue && modifiedDate.Value.Year <= currentYear)
        {
            fiscalYear = modifiedDate.Value.Year;
        }
        else if (File.Exists(sourcePath))
        {
            int fileYear = File.GetLastWriteTime(sourcePath).Year;
            if (fileYear <= currentYear)
            {
                fiscalYear = fileYear;
            }
        }

        DateOnly lastModified = modifiedDate.HasValue
            ? DateOnly.FromDateTime(modifiedDate.Value)
            : (File.Exists(sourcePath) ? DateOnly.FromDateTime(File.GetLastWriteTime(sourcePath)) : DateOnly.FromDateTime(DateTime.UtcNow));

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

        string docType = _options.DefaultDocumentType;
        string lowerTitle = title.ToLowerInvariant();
        if (lowerTitle.Contains("политика") || lowerTitle.Contains("policy") || lowerTitle.Contains("регламент"))
        {
            docType = "Policy";
        }
        else if (lowerTitle.Contains("руководство") || lowerTitle.Contains("инструкция") || lowerTitle.Contains("manual"))
        {
            docType = "Runbook";
        }
        else if (lowerTitle.Contains("отчет") || lowerTitle.Contains("report"))
        {
            docType = "Report";
        }
        else if (lowerTitle.Contains("договор") || lowerTitle.Contains("соглашение") || lowerTitle.Contains("contract"))
        {
            docType = "Contract";
        }

        return new DocumentMetadata(
            Id: id,
            Silo: silo,
            Department: _options.DefaultDepartment,
            Office: _options.DefaultOffice,
            ConfidentialityLevel: _options.DefaultConfidentialityLevel,
            DocumentType: docType,
            FiscalYear: fiscalYear,
            Author: author,
            LastModified: lastModified,
            Title: title);
    }
}
