using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.Samples.ChunkingStrategy.Parsers;

/// <summary>
/// Parser for Markdown (.md) and text (.txt) files with table extraction.
/// </summary>
public static class MarkdownDocumentParser
{
    public static ParsedDocument Parse(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var docId = Path.GetFileNameWithoutExtension(filePath);
        var text = File.ReadAllText(filePath, Encoding.UTF8);
        var tables = new List<ExtractedTable>();

        // Detect Markdown tables (| header | header |)
        var lines = text.Split('\n');
        var currentTable = new StringBuilder();
        int tableIndex = 0;

        string currentSection = "Общие сведения";
        int currentPage = 1;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                currentSection = trimmed.TrimStart('#').Trim();
            }
            if (trimmed.StartsWith("## Page", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("[Page", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(?:Page\s+|\[Page\s+)(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int p))
                {
                    currentPage = p;
                }
            }

            if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            {
                currentTable.AppendLine(trimmed);
            }
            else
            {
                if (currentTable.Length > 0)
                {
                    var tableMd = currentTable.ToString().Trim();
                    int tblStart = text.IndexOf(tableMd, StringComparison.Ordinal);
                    if (tblStart < 0)
                    {
                        tblStart = 0;
                    }

                    tables.Add(new ExtractedTable(
                        TableId: $"{docId}#tbl-{tableIndex++}",
                        PageNumber: currentPage,
                        Markdown: tableMd,
                        SectionHeading: currentSection,
                        StartCharOffset: tblStart,
                        EndCharOffset: tblStart + tableMd.Length));
                    currentTable.Clear();
                }
            }
        }

        if (currentTable.Length > 0)
        {
            var tableMd = currentTable.ToString().Trim();
            int tblStart = text.IndexOf(tableMd, StringComparison.Ordinal);
            if (tblStart < 0)
            {
                tblStart = 0;
            }

            tables.Add(new ExtractedTable(
                TableId: $"{docId}#tbl-{tableIndex++}",
                PageNumber: currentPage,
                Markdown: tableMd,
                SectionHeading: currentSection,
                StartCharOffset: tblStart,
                EndCharOffset: tblStart + tableMd.Length));
        }

        var meta = new DocumentMetadata(
            Id: docId,
            Silo: "markdown-docs",
            Department: "General",
            Office: "Main",
            ConfidentialityLevel: "Internal",
            DocumentType: "Markdown",
            FiscalYear: 2026,
            Author: "Author",
            LastModified: DateOnly.FromDateTime(DateTime.UtcNow),
            Title: docId);

        var doc = new Document(meta, text, filePath);
        return new ParsedDocument(doc, Array.Empty<ExtractedImage>(), tables);
    }
}
