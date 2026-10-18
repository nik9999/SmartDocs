using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Loaders;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class DocxDocumentLoaderTests
{
    private static byte[] CreateSampleDocx(string bodyXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">\n" +
                "<w:body>\n" +
                bodyXml +
                "</w:body>\n" +
                "</w:document>");
        }

        return ms.ToArray();
    }

    [Fact]
    public void Load_FileNotFound_ThrowsFileNotFoundException()
    {
        var loader = new DocxDocumentLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.docx");

        Assert.Throws<FileNotFoundException>(() => loader.Load(missingPath));
    }

    [Fact]
    public void Load_CorruptedFile_ThrowsInvalidDataException()
    {
        var loader = new DocxDocumentLoader();
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.docx");

        try
        {
            File.WriteAllText(tempFile, "NOT_A_VALID_ZIP_PACKAGE");
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
    public void Load_MissingDocumentXml_ThrowsInvalidDataException()
    {
        var loader = new DocxDocumentLoader();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("unrelated.txt");
        }

        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => loader.Load(ms, "dummy.docx"));
        Assert.Contains("missing 'word/document.xml'", ex.Message);
    }

    [Fact]
    public void Load_EmptyDocumentWithoutText_ThrowsInvalidOperationException()
    {
        var loader = new DocxDocumentLoader();
        var docxBytes = CreateSampleDocx(string.Empty);

        using var ms = new MemoryStream(docxBytes);
        var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(ms, "empty.docx"));
        Assert.Contains("No text could be extracted", ex.Message);
    }

    [Fact]
    public void Load_EmptyDocument_WhenThrowDisabled_ReturnsEmptyDocument()
    {
        var loader = new DocxDocumentLoader(new DocxDocumentLoaderOptions
        {
            ThrowIfNoTextExtracted = false
        });

        var docxBytes = CreateSampleDocx(string.Empty);
        using var ms = new MemoryStream(docxBytes);
        var doc = loader.Load(ms, "empty.docx");

        Assert.NotNull(doc);
        Assert.Empty(doc.Content);
        Assert.Equal("empty", doc.Metadata.Id);
    }

    [Fact]
    public void Load_HeadingsAndTables_ExtractsMarkdownFormat()
    {
        var loader = new DocxDocumentLoader();
        var bodyXml =
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>System Architecture</w:t></w:r></w:p>\n" +
            "<w:p><w:r><w:t>The platform uses microservices with Redis caching.</w:t></w:r></w:p>\n" +
            "<w:tbl>\n" +
            "  <w:tr>\n" +
            "    <w:tc><w:p><w:r><w:t>Service</w:t></w:r></w:p></w:tc>\n" +
            "    <w:tc><w:p><w:r><w:t>Port</w:t></w:r></w:p></w:tc>\n" +
            "  </w:tr>\n" +
            "  <w:tr>\n" +
            "    <w:tc><w:p><w:r><w:t>Gateway</w:t></w:r></w:p></w:tc>\n" +
            "    <w:tc><w:p><w:r><w:t>8080</w:t></w:r></w:p></w:tc>\n" +
            "  </w:tr>\n" +
            "</w:tbl>";

        var docxBytes = CreateSampleDocx(bodyXml);
        using var ms = new MemoryStream(docxBytes);
        var doc = loader.Load(ms, "architecture_spec.docx");

        Assert.NotNull(doc);
        Assert.Contains("# System Architecture", doc.Content);
        Assert.Contains("The platform uses microservices", doc.Content);
        Assert.Contains("| Service | Port |", doc.Content);
        Assert.Contains("| Gateway | 8080 |", doc.Content);
        Assert.Equal("System Architecture", doc.Metadata.Title);
    }

    [Fact]
    public async Task LoadAsync_RealArchitectureSpecification_ExtractsSuccessfully()
    {
        var realDocx = @"E:\PRJ\SmartDocs\data\technical-docs\architecture_specification.docx";
        if (!File.Exists(realDocx))
        {
            return;
        }

        var loader = new DocxDocumentLoader();
        var doc = await loader.LoadAsync(realDocx);

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Content);
        Assert.Equal("technical-docs", doc.Metadata.Silo);
        Assert.Equal("architecture_specification", doc.Metadata.Id);
    }
}
