using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Loaders;
using SmartDocs.Ingestion.Preprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class PdfDocumentLoaderTests
{
    private static byte[] CreateSamplePdf(params (string Title, string Text)[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var (title, text) in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(title, 14, new PdfPoint(50, 750), font);
            double y = 700;
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                page.AddText(line, 10, new PdfPoint(50, y), font);
                y -= 20;
            }
        }

        return builder.Build();
    }

    [Fact]
    public void Load_FileNotFound_ThrowsFileNotFoundException()
    {
        var loader = new PdfDocumentLoader();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.pdf");

        var ex = Assert.Throws<FileNotFoundException>(() => loader.Load(nonExistentPath));
        Assert.Contains(nonExistentPath, ex.Message);
    }

    [Fact]
    public void Load_CorruptedPdf_ThrowsInvalidDataException()
    {
        var loader = new PdfDocumentLoader();
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.pdf");

        try
        {
            File.WriteAllText(tempFile, "NOT_A_VALID_PDF_HEADER_OR_DATA");
            Assert.Throws<InvalidDataException>(() => loader.Load(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Load_EmptyPdfWithoutText_ThrowsInvalidOperationException()
    {
        var loader = new PdfDocumentLoader();
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4); // Blank page without any text
        var pdfBytes = builder.Build();

        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(tempFile, pdfBytes);
            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(tempFile));
            Assert.Contains("No text could be extracted", ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Load_EmptyPdfWithoutText_WhenThrowDisabled_ReturnsEmptyDocument()
    {
        var loader = new PdfDocumentLoader(new PdfDocumentLoaderOptions
        {
            ThrowIfNoTextExtracted = false
        });

        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var pdfBytes = builder.Build();

        using var ms = new MemoryStream(pdfBytes);
        var doc = loader.Load(ms, "empty.pdf");

        Assert.NotNull(doc);
        Assert.Empty(doc.Content);
        Assert.Equal("empty", doc.Metadata.Id);
    }

    [Fact]
    public void Load_MultiPagePdf_PreservesPageSeparatorsAndExtractsContent()
    {
        var loader = new PdfDocumentLoader();
        var pdfBytes = CreateSamplePdf(
            ("Page One Title", "First page body line description."),
            ("Page Two Title", "Second page body line description.")
        );

        using var ms = new MemoryStream(pdfBytes);
        var doc = loader.Load(ms, "sample_document.pdf");

        Assert.NotNull(doc);
        Assert.Contains("## Page 1", doc.Content);
        Assert.Contains("## Page 2", doc.Content);
        Assert.Contains("Page One Title", doc.Content);
        Assert.Contains("Page Two Title", doc.Content);
        Assert.Equal("sample_document", doc.Metadata.Id);
    }

    [Fact]
    public void Load_CustomPageSeparatorTemplate_UsesProvidedFormat()
    {
        var loader = new PdfDocumentLoader(new PdfDocumentLoaderOptions
        {
            PageSeparatorTemplate = "=== SECTION {0} ==="
        });

        var pdfBytes = CreateSamplePdf(
            ("Title 1", "Content 1"),
            ("Title 2", "Content 2")
        );

        using var ms = new MemoryStream(pdfBytes);
        var doc = loader.Load(ms, "custom_template.pdf");

        Assert.Contains("=== SECTION 1 ===", doc.Content);
        Assert.Contains("=== SECTION 2 ===", doc.Content);
        Assert.DoesNotContain("## Page 1", doc.Content);
    }

    [Fact]
    public void Load_CustomMetadataExtractor_OverridesMetadataGeneration()
    {
        var loader = new PdfDocumentLoader(new PdfDocumentLoaderOptions
        {
            MetadataExtractor = (path, pdf) => new DocumentMetadata(
                Id: "custom-id-99",
                Silo: "custom-silo",
                Department: "QA",
                Office: "Berlin",
                ConfidentialityLevel: "Restricted",
                DocumentType: "Runbook",
                FiscalYear: 2026,
                Author: "Custom Author",
                LastModified: new DateOnly(2026, 10, 18),
                Title: "Custom Title Overridden"
            )
        });

        var pdfBytes = CreateSamplePdf(("Title", "Content"));
        using var ms = new MemoryStream(pdfBytes);
        var doc = loader.Load(ms, "test.pdf");

        Assert.Equal("custom-id-99", doc.Metadata.Id);
        Assert.Equal("custom-silo", doc.Metadata.Silo);
        Assert.Equal("Berlin", doc.Metadata.Office);
        Assert.Equal("Custom Title Overridden", doc.Metadata.Title);
    }

    [Fact]
    public async Task LoadAsync_WithRealTechnicalPassport_LoadsCorrectMetadataAndExtractsText()
    {
        var pdfPath = @"E:\PRJ\SmartDocs\data\technical-docs\ПИМФ.426439.004 ПС Сv13.0.pdf";
        if (!File.Exists(pdfPath))
        {
            return;
        }

        var loader = new PdfDocumentLoader();
        var doc = await loader.LoadAsync(pdfPath);

        Assert.NotNull(doc);
        Assert.Contains("## Page 1", doc.Content);
        Assert.Contains("## Page 2", doc.Content);
        Assert.Contains("MDS AI-8UI", doc.Content);
        Assert.Contains("ПИМФ.426439.004", doc.Metadata.Id);
        Assert.Equal("technical-docs", doc.Metadata.Silo);
        Assert.Equal("Нижний Новгород", doc.Metadata.Office);
        Assert.Equal("Specification", doc.Metadata.DocumentType);
        Assert.Contains("КонтрАвт", doc.Metadata.Author);
    }

    [Fact]
    public async Task UserWorkflow_PdfDocumentLoader_To_Preprocessor_To_Chunkers()
    {
        // Demonstrates the exact flow requested by the user:
        // PDF -> PdfDocumentLoader -> Document -> IDocumentPreprocessor -> Various Chunkers

        var pdfBytes = CreateSamplePdf(
            ("PIMF-426439.004 Passport Specification", "MDS series input modules are designed for industrial telemetry acquisition.\nTechnical specifications: RS-485 Modbus RTU interface."),
            ("Safety Instructions and Operation Rules", "Electrical safety requirements: always disconnect main power line before wiring terminal blocks.")
        );

        var loader = new PdfDocumentLoader();
        using var ms = new MemoryStream(pdfBytes);
        var rawDoc = loader.Load(ms, "PIMF-426439.004-Passport.pdf");

        // 1. Raw document loaded with structural page separators
        Assert.Contains("## Page 1", rawDoc.Content);
        Assert.Contains("## Page 2", rawDoc.Content);

        // 2. Preprocessing
        var preprocessor = new DocumentPreprocessor();
        var preparedDoc = preprocessor.Preprocess(rawDoc);
        Assert.NotNull(preparedDoc);

        // 3. Feed the exact same prepared Document into multiple chunkers:
        // a) RecursiveCharacterChunker
        var recursiveChunker = new RecursiveCharacterChunker(maxChunkSize: 200);
        var recursiveChunks = new List<DocumentChunk>();
        await foreach (var chunk in recursiveChunker.ChunkAsync(preparedDoc))
        {
            recursiveChunks.Add(chunk);
        }
        Assert.NotEmpty(recursiveChunks);

        // b) FixedSizeChunker
        var fixedChunker = new FixedSizeChunker(chunkSize: 150, overlap: 20);
        var fixedChunks = new List<DocumentChunk>();
        await foreach (var chunk in fixedChunker.ChunkAsync(preparedDoc))
        {
            fixedChunks.Add(chunk);
        }
        Assert.NotEmpty(fixedChunks);

        // c) SentenceChunker
        var sentenceChunker = new SentenceChunker(maxSentencesPerChunk: 2);
        var sentenceChunks = new List<DocumentChunk>();
        await foreach (var chunk in sentenceChunker.ChunkAsync(preparedDoc))
        {
            sentenceChunks.Add(chunk);
        }
        Assert.NotEmpty(sentenceChunks);

        // d) TokenAwareChunker
        var tokenCounter = new SmartDocs.Core.Tokens.TokenCounter();
        var tokenChunker = new TokenAwareChunker(recursiveChunker, tokenCounter, maxTokens: 50);
        var tokenChunks = new List<DocumentChunk>();
        await foreach (var chunk in tokenChunker.ChunkAsync(preparedDoc))
        {
            tokenChunks.Add(chunk);
        }
        Assert.NotEmpty(tokenChunks);
    }
}
