using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Loaders;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class XlsxDocumentLoaderTests
{
    private static byte[] CreateSampleXlsx(string sheetDataXml, string[]? sharedStrings = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // xl/workbook.xml
            var wbEntry = zip.CreateEntry("xl/workbook.xml");
            using (var wbStream = wbEntry.Open())
            using (var writer = new StreamWriter(wbStream, Encoding.UTF8))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n" +
                    "  <sheets>\n" +
                    "    <sheet name=\"Revenue_Report\" sheetId=\"1\" r:id=\"rId1\"/>\n" +
                    "  </sheets>\n" +
                    "</workbook>");
            }

            // xl/sharedStrings.xml if present
            if (sharedStrings != null && sharedStrings.Length > 0)
            {
                var sstEntry = zip.CreateEntry("xl/sharedStrings.xml");
                using var sstStream = sstEntry.Open();
                using var writer = new StreamWriter(sstStream, Encoding.UTF8);
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                foreach (var str in sharedStrings)
                {
                    sb.Append("<si><t>").Append(str).Append("</t></si>");
                }
                sb.Append("</sst>");
                writer.Write(sb.ToString());
            }

            // xl/worksheets/sheet1.xml
            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using (var sheetStream = sheetEntry.Open())
            using (var writer = new StreamWriter(sheetStream, Encoding.UTF8))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n" +
                    "  <sheetData>\n" +
                    sheetDataXml +
                    "  </sheetData>\n" +
                    "</worksheet>");
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void Load_FileNotFound_ThrowsFileNotFoundException()
    {
        var loader = new XlsxDocumentLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.xlsx");

        Assert.Throws<FileNotFoundException>(() => loader.Load(missingPath));
    }

    [Fact]
    public void Load_CorruptedFile_ThrowsInvalidDataException()
    {
        var loader = new XlsxDocumentLoader();
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.xlsx");

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
    public void Load_MissingWorkbookXml_ThrowsInvalidDataException()
    {
        var loader = new XlsxDocumentLoader();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("unrelated.txt");
        }

        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => loader.Load(ms, "dummy.xlsx"));
        Assert.Contains("missing 'xl/workbook.xml'", ex.Message);
    }

    [Fact]
    public void Load_EmptySpreadsheetWithoutData_ThrowsInvalidOperationException()
    {
        var loader = new XlsxDocumentLoader();
        var xlsxBytes = CreateSampleXlsx(string.Empty);

        using var ms = new MemoryStream(xlsxBytes);
        var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(ms, "empty.xlsx"));
        Assert.Contains("No table or cell data could be extracted", ex.Message);
    }

    [Fact]
    public void Load_SpreadsheetWithSharedStrings_ConvertsToMarkdownTable()
    {
        var loader = new XlsxDocumentLoader();
        var sheetDataXml =
            "    <row r=\"1\">\n" +
            "      <c r=\"A1\" t=\"s\"><v>0</v></c>\n" +
            "      <c r=\"B1\" t=\"s\"><v>1</v></c>\n" +
            "    </row>\n" +
            "    <row r=\"2\">\n" +
            "      <c r=\"A2\" t=\"s\"><v>2</v></c>\n" +
            "      <c r=\"B2\"><v>45.2</v></c>\n" +
            "    </row>\n";

        var sharedStrings = new[] { "Metric", "Value", "Cloud Revenue" };
        var xlsxBytes = CreateSampleXlsx(sheetDataXml, sharedStrings);

        using var ms = new MemoryStream(xlsxBytes);
        var doc = loader.Load(ms, "financial_summary.xlsx");

        Assert.NotNull(doc);
        Assert.Contains("## Sheet: Revenue_Report", doc.Content);
        Assert.Contains("| Metric | Value |", doc.Content);
        Assert.Contains("| Cloud Revenue | 45.2 |", doc.Content);
        Assert.Equal("financial_summary — Revenue_Report", doc.Metadata.Title);
    }

    [Fact]
    public async Task LoadAsync_RealFinancialReport_LoadsSuccessfully()
    {
        var realXlsx = @"E:\PRJ\SmartDocs\data\financial-reports\q3_financial_report.xlsx";
        if (!File.Exists(realXlsx))
        {
            return;
        }

        var loader = new XlsxDocumentLoader();
        var doc = await loader.LoadAsync(realXlsx);

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Content);
        Assert.Equal("financial-reports", doc.Metadata.Silo);
        Assert.Equal("q3_financial_report", doc.Metadata.Id);
    }
}
