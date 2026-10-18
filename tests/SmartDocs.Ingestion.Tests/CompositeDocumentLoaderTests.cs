using System;
using System.IO;
using System.Threading.Tasks;
using SmartDocs.Ingestion.Loaders;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class CompositeDocumentLoaderTests
{
    [Fact]
    public void CreateDefault_RegistersExpectedExtensions()
    {
        var composite = CompositeDocumentLoader.CreateDefault();

        Assert.Contains(".pdf", composite.SupportedExtensions);
        Assert.Contains(".docx", composite.SupportedExtensions);
        Assert.Contains(".xlsx", composite.SupportedExtensions);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedExtension_ThrowsNotSupportedException()
    {
        var composite = CompositeDocumentLoader.CreateDefault();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => composite.LoadAsync("file.xyz"));
        Assert.Contains(".xyz", ex.Message);
    }

    [Fact]
    public async Task LoadAsync_RoutesByExtension_ToAppropriateLoader()
    {
        var composite = CompositeDocumentLoader.CreateDefault();

        var realDocx = @"E:\PRJ\SmartDocs\data\technical-docs\architecture_specification.docx";
        if (File.Exists(realDocx))
        {
            var docxDoc = await composite.LoadAsync(realDocx);
            Assert.NotNull(docxDoc);
            Assert.NotEmpty(docxDoc.Content);
        }

        var realXlsx = @"E:\PRJ\SmartDocs\data\financial-reports\q3_financial_report.xlsx";
        if (File.Exists(realXlsx))
        {
            var xlsxDoc = await composite.LoadAsync(realXlsx);
            Assert.NotNull(xlsxDoc);
            Assert.NotEmpty(xlsxDoc.Content);
        }

        var realPdf = @"E:\PRJ\SmartDocs\data\technical-docs\ПИМФ.426439.004 ПС Сv13.0.pdf";
        if (File.Exists(realPdf))
        {
            var pdfDoc = await composite.LoadAsync(realPdf);
            Assert.NotNull(pdfDoc);
            Assert.NotEmpty(pdfDoc.Content);
        }
    }
}
