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
/// Native .NET OpenXML parser for .docx files that extracts text, headings,
/// structured tables (converted to Markdown), and embedded raster images.
/// </summary>
public static class WordDocumentParser
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static ParsedDocument Parse(string filePath, string outputImageDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var docId = Path.GetFileNameWithoutExtension(filePath);
        var images = new List<ExtractedImage>();
        var tables = new List<ExtractedTable>();
        var textBuilder = new StringBuilder();

        using var zip = ZipFile.OpenRead(filePath);

        // 1. Extract embedded raster images from word/media/
        int imgIndex = 0;
        foreach (var entry in zip.Entries.Where(static e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)))
        {
            Directory.CreateDirectory(outputImageDir);
            var ext = Path.GetExtension(entry.Name);
            var imgFileName = $"{docId}-img-{imgIndex:D3}{ext}";
            var imgFilePath = Path.Combine(outputImageDir, imgFileName);
            entry.ExtractToFile(imgFilePath, overwrite: true);

            images.Add(new ExtractedImage(
                ImageId: $"{docId}#img-{imgIndex}",
                PageNumber: 1,
                Caption: string.Empty,
                ImagePath: imgFilePath));
            imgIndex++;
        }

        // 2. Parse word/document.xml
        var docEntry = zip.GetEntry("word/document.xml");
        if (docEntry != null)
        {
            using var stream = docEntry.Open();
            var xdoc = XDocument.Load(stream);
            var body = xdoc.Root?.Element(W + "body");
            if (body != null)
            {
                int tableIndex = 0;
                int currentPage = 1;
                string currentHeading = "Введение";
                int currentDrawingIndex = 0;

                foreach (var element in body.Elements())
                {
                    if (element.Descendants().Any(static d => (d.Name.LocalName == "br" && d.Attribute(W + "type")?.Value == "page") || d.Name.LocalName == "lastRenderedPageBreak"))
                    {
                        currentPage++;
                    }

                    if (element.Name == W + "p")
                    {
                        var style = element.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
                        var pText = string.Concat(element.Descendants(W + "t").Select(static t => t.Value)).Trim();

                        if (style == "Heading1")
                        {
                            currentHeading = pText;
                            textBuilder.Append("# ").AppendLine(pText).AppendLine();
                        }
                        else if (style == "Heading2")
                        {
                            currentHeading = pText;
                            textBuilder.Append("## ").AppendLine(pText).AppendLine();
                        }
                        else if (style == "Heading3")
                        {
                            currentHeading = pText;
                            textBuilder.Append("### ").AppendLine(pText).AppendLine();
                        }
                        else if (!string.IsNullOrEmpty(pText))
                        {
                            textBuilder.AppendLine(pText).AppendLine();
                        }

                        // Check if paragraph contains an embedded drawing/image
                        if (element.Descendants().Any(static d => d.Name.LocalName == "drawing" || d.Name.LocalName == "pict"))
                        {
                            if (currentDrawingIndex < images.Count)
                            {
                                images[currentDrawingIndex] = images[currentDrawingIndex] with
                                {
                                    PageNumber = currentPage,
                                    SectionHeading = currentHeading,
                                    StartCharOffset = textBuilder.Length
                                };
                                currentDrawingIndex++;
                            }
                        }
                    }
                    else if (element.Name == W + "tbl")
                    {
                        var tableMd = ConvertWordTableToMarkdown(element);
                        if (!string.IsNullOrWhiteSpace(tableMd))
                        {
                            int tblStart = textBuilder.Length;
                            textBuilder.AppendLine(tableMd).AppendLine();

                            tables.Add(new ExtractedTable(
                                TableId: $"{docId}#tbl-{tableIndex++}",
                                PageNumber: currentPage,
                                Markdown: tableMd,
                                SectionHeading: currentHeading,
                                StartCharOffset: tblStart,
                                EndCharOffset: tblStart + tableMd.Length));
                        }
                    }
                }
            }
        }

        var meta = new DocumentMetadata(
            Id: docId,
            Silo: "word-documents",
            Department: "General",
            Office: "Main",
            ConfidentialityLevel: "Internal",
            DocumentType: "Docx",
            FiscalYear: 2026,
            Author: "Author",
            LastModified: DateOnly.FromDateTime(DateTime.UtcNow),
            Title: docId);

        var doc = new Document(meta, textBuilder.ToString(), filePath);
        return new ParsedDocument(doc, images, tables);
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

        return sb.ToString();
    }
}
