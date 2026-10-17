using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Multimodal;
using UglyToad.PdfPig;

namespace SmartDocs.Samples.ChunkingStrategy.Parsers;

/// <summary>
/// Parser for PDF documents using PdfPig that extracts per-page text,
/// raster images, and structured tables.
/// </summary>
public static class PdfDocumentParser
{
    public static async Task<ParsedDocument> ParseAsync(
        string filePath,
        string outputImageDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var docId = Path.GetFileNameWithoutExtension(filePath);
        var images = new List<ExtractedImage>();
        var tables = new List<ExtractedTable>();
        var textBuilder = new StringBuilder();

        // 1. Extract raster images via PdfPigImageExtractor
        var imageExtractor = new PdfPigImageExtractor();
        var extractedImgs = await imageExtractor.ExtractAsync(filePath, outputImageDir, docId, cancellationToken);
        images.AddRange(extractedImgs);

        // 2. Extract text and detect tables per page
        using (var pdf = PdfDocument.Open(filePath))
        {
            int tblIndex = 0;
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Reconstruct lines from page words by vertical position
                var words = page.GetWords()
                    .OrderByDescending(static w => w.BoundingBox.Bottom)
                    .ThenBy(static w => w.BoundingBox.Left)
                    .ToList();

                var lines = new List<string>();
                var currentLineWords = new List<string>();
                double currentY = double.NaN;

                foreach (var word in words)
                {
                    if (double.IsNaN(currentY) || Math.Abs(word.BoundingBox.Bottom - currentY) > 5)
                    {
                        if (currentLineWords.Count > 0)
                        {
                            lines.Add(string.Join(" ", currentLineWords));
                            currentLineWords.Clear();
                        }
                        currentY = word.BoundingBox.Bottom;
                    }
                    currentLineWords.Add(word.Text);
                }

                if (currentLineWords.Count > 0)
                {
                    lines.Add(string.Join(" ", currentLineWords));
                }

                if (lines.Count > 0)
                {
                    textBuilder.Append("## Page ").Append(page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine().AppendLine();
                    var pageText = new StringBuilder();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        if (pageText.Length == 0)
                        {
                            pageText.Append(line);
                        }
                        else
                        {
                            // Start new paragraph on list items, headings, figure/table captions, or numbered sections
                            bool isNewBlock = line.StartsWith('•') ||
                                              line.StartsWith('-') ||
                                              line.StartsWith('') ||
                                              line.StartsWith('|') ||
                                              line.StartsWith("Таблица", StringComparison.OrdinalIgnoreCase) ||
                                              line.StartsWith("Рис", StringComparison.OrdinalIgnoreCase) ||
                                              line.StartsWith("Приложение", StringComparison.OrdinalIgnoreCase) ||
                                              (char.IsDigit(line[0]) && line.Contains('.') && line.IndexOf('.') <= 4);

                            if (isNewBlock)
                            {
                                pageText.AppendLine().AppendLine().Append(line);
                            }
                            else
                            {
                                pageText.Append(' ').Append(line);
                            }
                        }
                    }
                    textBuilder.AppendLine(pageText.ToString()).AppendLine();

                    // Detect tabular blocks (lines containing pipe '|')
                    var tableLines = lines.Where(static l => l.Contains('|')).ToList();
                    if (tableLines.Count >= 2)
                    {
                        var formattedTable = new StringBuilder();
                        var headerLine = tableLines[0].Trim();
                        formattedTable.AppendLine(headerLine);
                        if (!tableLines.Any(static l => l.Contains("---")))
                        {
                            var colCount = headerLine.Split('|', StringSplitOptions.RemoveEmptyEntries).Length;
                            if (colCount > 0)
                            {
                                formattedTable.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", colCount))).AppendLine(" |");
                            }
                        }
                        for (int i = 1; i < tableLines.Count; i++)
                        {
                            formattedTable.AppendLine(tableLines[i].Trim());
                        }

                        var tableMd = formattedTable.ToString().Trim();
                        tables.Add(new ExtractedTable(
                            TableId: $"{docId}#tbl-{tblIndex++}",
                            PageNumber: page.Number,
                            Markdown: tableMd));
                    }
                }
            }
        }

        var meta = new DocumentMetadata(
            Id: docId,
            Silo: "pdf-documents",
            Department: "General",
            Office: "Main",
            ConfidentialityLevel: "Internal",
            DocumentType: "Pdf",
            FiscalYear: 2026,
            Author: "Author",
            LastModified: DateOnly.FromDateTime(DateTime.UtcNow),
            Title: docId);

        var rawDoc = new Document(meta, textBuilder.ToString(), filePath);
        var preprocessor = new SmartDocs.Ingestion.Preprocessing.DocumentPreprocessor();
        var cleanDoc = preprocessor.Preprocess(rawDoc);
        return new ParsedDocument(cleanDoc, images, tables);
    }
}
