using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.Samples.ChunkingStrategy.Parsers;

/// <summary>
/// Native .NET OpenXML parser for .xlsx files that extracts worksheets and converts
/// tabular data into structured Markdown tables with preserved column headers.
/// </summary>
public static class ExcelDocumentParser
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static ParsedDocument Parse(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var docId = Path.GetFileNameWithoutExtension(filePath);
        var tables = new List<ExtractedTable>();
        var textBuilder = new StringBuilder();

        using var zip = ZipFile.OpenRead(filePath);

        // 1. Read shared strings
        var sharedStrings = new List<string>();
        var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sstEntry != null)
        {
            using var sstStream = sstEntry.Open();
            var sstDoc = XDocument.Load(sstStream);
            foreach (var si in sstDoc.Descendants(S + "si"))
            {
                var val = string.Concat(si.Descendants(S + "t").Select(static t => t.Value));
                sharedStrings.Add(val);
            }
        }

        // 2. Read sheet names
        var sheetNames = new Dictionary<string, string>();
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry != null)
        {
            using var wbStream = wbEntry.Open();
            var wbDoc = XDocument.Load(wbStream);
            foreach (var sheet in wbDoc.Descendants(S + "sheet"))
            {
                var name = sheet.Attribute("name")?.Value ?? "Лист";
                var rId = sheet.Attribute(R + "id")?.Value;
                if (rId != null)
                {
                    sheetNames[rId] = name;
                }
            }
        }

        // 3. Read worksheets
        int tableIndex = 0;
        foreach (var entry in zip.Entries.Where(static e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var sheetStream = entry.Open();
            var sheetDoc = XDocument.Load(sheetStream);
            var sheetData = sheetDoc.Descendants(S + "sheetData").FirstOrDefault();
            if (sheetData == null)
            {
                continue;
            }

            var rows = sheetData.Elements(S + "row").ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            var rowDataList = new List<List<string>>();
            foreach (var row in rows)
            {
                var rowCells = new List<string>();
                foreach (var c in row.Elements(S + "c"))
                {
                    var type = c.Attribute("t")?.Value;
                    var val = c.Element(S + "v")?.Value ?? string.Empty;
                    if (type == "s" && int.TryParse(val, out var strIdx) && strIdx >= 0 && strIdx < sharedStrings.Count)
                    {
                        val = sharedStrings[strIdx];
                    }
                    rowCells.Add(val.Trim());
                }

                if (rowCells.Any(static x => !string.IsNullOrEmpty(x)))
                {
                    rowDataList.Add(rowCells);
                }
            }

            if (rowDataList.Count == 0)
            {
                continue;
            }

            // Convert to Markdown Table with headers
            var header = rowDataList[0];
            var colCount = header.Count;
            var sheetTitle = sheetNames.Values.ElementAtOrDefault(tableIndex) ?? entry.Name;
            var sb = new StringBuilder();
            sb.Append("### Таблица: ").AppendLine(sheetTitle).AppendLine();
            sb.Append("| ").Append(string.Join(" | ", header)).AppendLine(" |");
            sb.Append("| ").Append(string.Join(" | ", header.Select(static _ => "---"))).AppendLine(" |");

            for (int i = 1; i < rowDataList.Count; i++)
            {
                var r = rowDataList[i];
                while (r.Count < colCount)
                {
                    r.Add(string.Empty);
                }
                sb.Append("| ").Append(string.Join(" | ", r.Take(colCount))).AppendLine(" |");
            }

            var tableMd = sb.ToString();
            tables.Add(new ExtractedTable(
                TableId: $"{docId}#tbl-{tableIndex++}",
                PageNumber: 1,
                Markdown: tableMd));

            textBuilder.AppendLine(tableMd).AppendLine();
        }

        var meta = new DocumentMetadata(
            Id: docId,
            Silo: "spreadsheets",
            Department: "Finance",
            Office: "Main",
            ConfidentialityLevel: "Internal",
            DocumentType: "Xlsx",
            FiscalYear: 2026,
            Author: "Finance Dept",
            LastModified: DateOnly.FromDateTime(DateTime.UtcNow),
            Title: docId);

        var doc = new Document(meta, textBuilder.ToString(), filePath);
        return new ParsedDocument(doc, Array.Empty<ExtractedImage>(), tables);
    }
}
