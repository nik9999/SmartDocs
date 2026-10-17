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

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            {
                currentTable.AppendLine(trimmed);
            }
            else
            {
                if (currentTable.Length > 0)
                {
                    tables.Add(new ExtractedTable(
                        TableId: $"{docId}#tbl-{tableIndex++}",
                        PageNumber: 1,
                        Markdown: currentTable.ToString().Trim()));
                    currentTable.Clear();
                }
            }
        }

        if (currentTable.Length > 0)
        {
            tables.Add(new ExtractedTable(
                TableId: $"{docId}#tbl-{tableIndex++}",
                PageNumber: 1,
                Markdown: currentTable.ToString().Trim()));
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
