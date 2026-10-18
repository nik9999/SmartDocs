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
/// Production Microsoft Excel (.xlsx) spreadsheet loader backed by native OpenXML
/// zip decompression and XML parsing. Converts worksheets into structured Markdown
/// tables, preserves sheet boundaries, extracts workbook metadata, and provides clear error handling.
/// </summary>
public sealed class XlsxDocumentLoader : IDocumentLoader
{
    private static readonly HashSet<string> DefaultSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx"
    };

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Dcterms = "http://purl.org/dc/terms/";

    private static readonly Regex NonIdCharactersRegex = new(
        @"[^\p{L}\p{Nd}\.\-_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly XlsxDocumentLoaderOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="XlsxDocumentLoader"/>.
    /// </summary>
    /// <param name="options">Optional loader configuration options.</param>
    public XlsxDocumentLoader(XlsxDocumentLoaderOptions? options = null)
    {
        _options = options ?? new XlsxDocumentLoaderOptions();
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
    /// Synchronously loads an Excel (.xlsx) spreadsheet from disk.
    /// </summary>
    /// <param name="path">Path to the .xlsx file.</param>
    /// <returns>A loaded <see cref="Document"/> containing Markdown tables and metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the .xlsx file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the .xlsx is corrupted or cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no data could be extracted and throwing is enabled.</exception>
    public Document Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                string.Format(CultureInfo.InvariantCulture, "Excel spreadsheet file not found: '{0}'.", path),
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
                string.Format(CultureInfo.InvariantCulture, "Excel spreadsheet '{0}' is corrupted or could not be parsed: {1}", path, ex.Message),
                ex);
        }
    }

    /// <summary>
    /// Synchronously loads an Excel (.xlsx) spreadsheet from a stream.
    /// </summary>
    /// <param name="stream">Readable stream containing the .xlsx package.</param>
    /// <param name="sourcePath">Logical path or identifier for metadata and error reporting.</param>
    /// <returns>A loaded <see cref="Document"/> containing Markdown tables and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourcePath"/> is null or whitespace.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream is corrupted or not a valid Excel package.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no data could be extracted and throwing is enabled.</exception>
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
                string.Format(CultureInfo.InvariantCulture, "Excel package stream for '{0}' is corrupted or not a valid ZIP archive: {1}", sourcePath, ex.Message),
                ex);
        }

        using (zip)
        {
            return ParseXlsxArchive(zip, sourcePath);
        }
    }

    /// <summary>
    /// Asynchronously loads an Excel (.xlsx) spreadsheet from a stream.
    /// </summary>
    public Task<Document> LoadAsync(Stream stream, string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load(stream, sourcePath));
    }

    private Document ParseXlsxArchive(ZipArchive zip, string sourcePath)
    {
        var wbEntry = zip.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Excel spreadsheet '{0}' is missing 'xl/workbook.xml'.", sourcePath));

        // 1. Read shared strings
        var sharedStrings = LoadSharedStrings(zip);

        // 2. Read sheet mapping (relationship ID -> Sheet Name)
        var sheetMap = LoadSheetMap(zip, wbEntry);

        // 3. Process each worksheet
        var sb = new StringBuilder();
        bool hasAnyData = false;
        string firstSheetName = string.Empty;

        foreach (var (sheetTarget, sheetName) in sheetMap)
        {
            var sheetEntry = zip.GetEntry(sheetTarget) ?? zip.GetEntry("xl/" + sheetTarget);
            if (sheetEntry == null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(firstSheetName))
            {
                firstSheetName = sheetName;
            }

            var tableMd = ConvertWorksheetToMarkdownTable(sheetEntry, sharedStrings);
            if (string.IsNullOrWhiteSpace(tableMd))
            {
                continue;
            }

            hasAnyData = true;

            if (!string.IsNullOrWhiteSpace(_options.SheetSeparatorTemplate))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine().AppendLine();
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, _options.SheetSeparatorTemplate, sheetName);
                sb.AppendLine().AppendLine();
            }
            else if (sb.Length > 0)
            {
                sb.AppendLine().AppendLine();
            }

            sb.AppendLine(tableMd);
        }

        string fullContent = sb.ToString().Trim();

        if (!hasAnyData || string.IsNullOrWhiteSpace(fullContent))
        {
            if (_options.ThrowIfNoTextExtracted)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "No table or cell data could be extracted from Excel spreadsheet '{0}'.", sourcePath));
            }
        }

        var metadata = _options.MetadataExtractor != null
            ? _options.MetadataExtractor(sourcePath)
            : ExtractXlsxMetadata(zip, sourcePath, firstSheetName);

        return new Document(metadata, fullContent, sourcePath);
    }

    private static List<string> LoadSharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sstEntry == null)
        {
            return strings;
        }

        try
        {
            using var stream = sstEntry.Open();
            var doc = XDocument.Load(stream);
            foreach (var si in doc.Descendants(S + "si"))
            {
                var val = string.Concat(si.Descendants(S + "t").Select(static t => t.Value));
                strings.Add(val);
            }
        }
        catch
        {
            // Fallback to empty if shared strings damaged
        }

        return strings;
    }

    private static List<(string Target, string Name)> LoadSheetMap(ZipArchive zip, ZipArchiveEntry wbEntry)
    {
        var result = new List<(string Target, string Name)>();

        // Read relationships from xl/_rels/workbook.xml.rels if available
        var relsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            try
            {
                using var relStream = relsEntry.Open();
                var relDoc = XDocument.Load(relStream);
                foreach (var rel in relDoc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    var id = rel.Attribute("Id")?.Value;
                    var target = rel.Attribute("Target")?.Value;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                    {
                        if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) && !target.StartsWith('/'))
                        {
                            target = "xl/" + target;
                        }
                        target = target.TrimStart('/');
                        relsMap[id] = target;
                    }
                }
            }
            catch
            {
                // Relationships optional fallback
            }
        }

        try
        {
            using var stream = wbEntry.Open();
            var wbDoc = XDocument.Load(stream);
            int sheetNum = 1;

            foreach (var sheet in wbDoc.Descendants(S + "sheet"))
            {
                var name = sheet.Attribute("name")?.Value ?? string.Format(CultureInfo.InvariantCulture, "Sheet{0}", sheetNum);
                var rId = sheet.Attribute(R + "id")?.Value;

                string target = string.Empty;
                if (!string.IsNullOrEmpty(rId) && relsMap.TryGetValue(rId, out var relTarget))
                {
                    target = relTarget;
                }
                else
                {
                    target = string.Format(CultureInfo.InvariantCulture, "xl/worksheets/sheet{0}.xml", sheetNum);
                }

                result.Add((target, name));
                sheetNum++;
            }
        }
        catch
        {
            // If workbook.xml fails, find sheet entries directly
            int sNum = 1;
            foreach (var e in zip.Entries.Where(static x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(static x => x.FullName))
            {
                result.Add((e.FullName, string.Format(CultureInfo.InvariantCulture, "Sheet{0}", sNum++)));
            }
        }

        return result;
    }

    private static string ConvertWorksheetToMarkdownTable(ZipArchiveEntry sheetEntry, List<string> sharedStrings)
    {
        XDocument doc;
        try
        {
            using var stream = sheetEntry.Open();
            doc = XDocument.Load(stream);
        }
        catch
        {
            return string.Empty;
        }

        var sheetData = doc.Descendants(S + "sheetData").FirstOrDefault();
        if (sheetData == null)
        {
            return string.Empty;
        }

        var rows = sheetData.Elements(S + "row").ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var grid = new List<Dictionary<int, string>>();
        int maxColIndex = 0;

        foreach (var row in rows)
        {
            var rowDict = new Dictionary<int, string>();
            int currentSyntheticCol = 0;

            foreach (var c in row.Elements(S + "c"))
            {
                var cellRef = c.Attribute("r")?.Value;
                int colIndex = !string.IsNullOrEmpty(cellRef)
                    ? ColumnNameToIndex(cellRef)
                    : currentSyntheticCol;

                currentSyntheticCol = colIndex + 1;

                var type = c.Attribute("t")?.Value;
                string val = string.Empty;

                if (type == "s")
                {
                    var rawVal = c.Element(S + "v")?.Value;
                    if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var strIdx) && strIdx >= 0 && strIdx < sharedStrings.Count)
                    {
                        val = sharedStrings[strIdx];
                    }
                }
                else if (type == "inlineStr")
                {
                    val = string.Concat(c.Descendants(S + "t").Select(static t => t.Value));
                }
                else if (type == "b")
                {
                    val = c.Element(S + "v")?.Value == "1" ? "true" : "false";
                }
                else
                {
                    val = c.Element(S + "v")?.Value ?? string.Empty;
                }

                val = val.Trim().Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(val))
                {
                    rowDict[colIndex] = val;
                    if (colIndex > maxColIndex)
                    {
                        maxColIndex = colIndex;
                    }
                }
            }

            if (rowDict.Count > 0)
            {
                grid.Add(rowDict);
            }
        }

        if (grid.Count == 0)
        {
            return string.Empty;
        }

        int colCount = maxColIndex + 1;
        var sb = new StringBuilder();

        // 1. Header row
        var headerRow = grid[0];
        var headerCells = new List<string>(colCount);
        for (int c = 0; c < colCount; c++)
        {
            headerCells.Add(headerRow.TryGetValue(c, out var h) ? h : string.Format(CultureInfo.InvariantCulture, "Col_{0}", c + 1));
        }

        sb.Append("| ").Append(string.Join(" | ", headerCells)).AppendLine(" |");
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", colCount))).AppendLine(" |");

        // 2. Data rows
        for (int r = 1; r < grid.Count; r++)
        {
            var rowDict = grid[r];
            var cells = new List<string>(colCount);
            for (int c = 0; c < colCount; c++)
            {
                cells.Add(rowDict.TryGetValue(c, out var val) ? val : string.Empty);
            }

            sb.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
        }

        return sb.ToString().TrimEnd();
    }

    private static int ColumnNameToIndex(string cellRef)
    {
        int index = 0;
        foreach (char c in cellRef)
        {
            if (c is >= 'A' and <= 'Z')
            {
                index = (index * 26) + (c - 'A' + 1);
            }
            else if (c is >= 'a' and <= 'z')
            {
                index = (index * 26) + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }

        return Math.Max(0, index - 1);
    }

    private DocumentMetadata ExtractXlsxMetadata(ZipArchive zip, string sourcePath, string firstSheetName)
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
                // Optional
            }
        }

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(firstSheetName))
        {
            title = string.Format(CultureInfo.InvariantCulture, "{0} — {1}", fileName, firstSheetName);
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

        return new DocumentMetadata(
            Id: id,
            Silo: silo,
            Department: _options.DefaultDepartment,
            Office: _options.DefaultOffice,
            ConfidentialityLevel: _options.DefaultConfidentialityLevel,
            DocumentType: _options.DefaultDocumentType,
            FiscalYear: fiscalYear,
            Author: author,
            LastModified: lastModified,
            Title: title);
    }
}
