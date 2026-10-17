using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SmartDocs.Samples.ChunkingStrategy;

/// <summary>
/// Ensures all sample heterogeneous files exist with correct UTF-8 encoding:
/// PDF, DOCX (Word with tables and embedded image), XLSX (Excel spreadsheet with tables),
/// and Markdown documents in Russian and English.
/// </summary>
public static class SampleDataGenerator
{
    private static readonly byte[] SamplePngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    public static void EnsureSampleDataExists(string dataDir)
    {
        GeneratePdfDocument(Path.Combine(dataDir, "hr-policies", "vacation_policy.pdf"));
        GenerateWordDocument(Path.Combine(dataDir, "technical-docs", "architecture_specification.docx"));
        GenerateExcelDocument(Path.Combine(dataDir, "financial-reports", "q3_financial_report.xlsx"));
        GenerateMarkdownFiles(dataDir);
    }

    private static void GeneratePdfDocument(string hrPdf)
    {
        if (File.Exists(hrPdf))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(hrPdf)!);
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Annual Vacation and Leave Policy (HR-2026)", 14, new PdfPoint(50, 750), font);
        page.AddText("Employees receive 28 calendar days of paid vacation per year.", 11, new PdfPoint(50, 720), font);
        page.AddText("Department | Vacation Days | Sick Leave | Remote Days", 10, new PdfPoint(50, 680), font);
        page.AddText("Engineering | 28 days | Unlimited | 3 days/week", 10, new PdfPoint(50, 660), font);
        page.AddText("Finance | 28 days | 14 days | 2 days/week", 10, new PdfPoint(50, 640), font);
        page.AddText("Operations | 28 days | 14 days | On-site", 10, new PdfPoint(50, 620), font);

        var bytes = builder.Build();
        File.WriteAllBytes(hrPdf, bytes);
    }

    private static void GenerateWordDocument(string docxPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        if (File.Exists(docxPath))
        {
            return;
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);

            AddZipEntry(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            AddZipEntry(archive, "word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImg1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/architecture_diagram.png"/>
                </Relationships>
                """);

            AddZipEntry(archive, "word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                      <w:r><w:t>Microservices System Architecture Specification</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:r><w:t>SmartDocs platform indexes complex corporate multi-format documents. The ingestion subsystem handles Word documents, Excel spreadsheets, PDF files, and Markdown catalogs. The architecture employs a two-stage retrieval pipeline: local multilingual Bi-Encoder (BGE-M3 ONNX) and Cross-Encoder reranker.</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
                      <w:r><w:t>Core Platform Components and SLA Guarantees</w:t></w:r>
                    </w:p>
                    <w:tbl>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Component</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Purpose</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Technology Stack</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>SLA Uptime</w:t></w:r></w:p></w:tc>
                      </w:tr>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Embedding Engine</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Dense multilingual vector search</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>BGE-M3 (ONNX Local)</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>99.99%</w:t></w:r></w:p></w:tc>
                      </w:tr>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Reranker</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Candidate reranking</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>bge-reranker-v2-m3 (ONNX)</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>99.95%</w:t></w:r></w:p></w:tc>
                      </w:tr>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Vector Database</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Dense embedding index</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Qdrant Client</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>99.90%</w:t></w:r></w:p></w:tc>
                      </w:tr>
                      <w:tr>
                        <w:tc><w:p><w:r><w:t>Distributed Cache</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Query and session cache</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>Redis Cluster</w:t></w:r></w:p></w:tc>
                        <w:tc><w:p><w:r><w:t>99.99%</w:t></w:r></w:p></w:tc>
                      </w:tr>
                    </w:tbl>
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
                      <w:r><w:t>Architecture Diagram and Data Flow</w:t></w:r>
                    </w:p>
                    <w:p>
                      <w:r><w:t>The diagram below depicts client traffic entering through the API Gateway, routed to Auth and Catalog services, with Redis distributed caching and PostgreSQL cluster persistence:</w:t></w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);

            var imgEntry = archive.CreateEntry("word/media/architecture_diagram.png");
            using var imgStream = imgEntry.Open();
            imgStream.Write(SamplePngBytes, 0, SamplePngBytes.Length);
        }

        File.WriteAllBytes(docxPath, zipStream.ToArray());
    }

    private static void GenerateExcelDocument(string xlsxPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath)!);
        if (File.Exists(xlsxPath))
        {
            return;
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddZipEntry(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
                </Types>
                """);

            AddZipEntry(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            AddZipEntry(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
                </Relationships>
                """);

            AddZipEntry(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Q3 Financial Performance" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);

            AddZipEntry(archive, "xl/sharedStrings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="16" uniqueCount="16">
                  <si><t>Department / Division</t></si>
                  <si><t>Q3 Revenue ($M)</t></si>
                  <si><t>Net Profit ($M)</t></si>
                  <si><t>Operating Margin %</t></si>
                  <si><t>Cloud Services (Enterprise Cloud)</t></si>
                  <si><t>450.5</t></si>
                  <si><t>112.3</t></si>
                  <si><t>24.9%</t></si>
                  <si><t>Enterprise Software &amp; Solutions</t></si>
                  <si><t>320.0</t></si>
                  <si><t>95.4</t></si>
                  <si><t>29.8%</t></si>
                  <si><t>AI &amp; Machine Learning Platform</t></si>
                  <si><t>180.2</t></si>
                  <si><t>42.1</t></si>
                  <si><t>23.3%</t></si>
                </sst>
                """);

            AddZipEntry(archive, "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" t="s"><v>0</v></c>
                      <c r="B1" t="s"><v>1</v></c>
                      <c r="C1" t="s"><v>2</v></c>
                      <c r="D1" t="s"><v>3</v></c>
                    </row>
                    <row r="2">
                      <c r="A2" t="s"><v>4</v></c>
                      <c r="B2" t="s"><v>5</v></c>
                      <c r="C2" t="s"><v>6</v></c>
                      <c r="D2" t="s"><v>7</v></c>
                    </row>
                    <row r="3">
                      <c r="A3" t="s"><v>8</v></c>
                      <c r="B3" t="s"><v>9</v></c>
                      <c r="C3" t="s"><v>10</v></c>
                      <c r="D3" t="s"><v>11</v></c>
                    </row>
                    <row r="4">
                      <c r="A4" t="s"><v>12</v></c>
                      <c r="B4" t="s"><v>13</v></c>
                      <c r="C4" t="s"><v>14</v></c>
                      <c r="D4" t="s"><v>15</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        File.WriteAllBytes(xlsxPath, zipStream.ToArray());
    }

    private static void GenerateMarkdownFiles(string dataDir)
    {
        var serverMd = Path.Combine(dataDir, "product-catalog", "server_specifications.md");
        if (!File.Exists(serverMd))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(serverMd)!);
            File.WriteAllText(serverMd,
                """
                # GPU Server Infrastructure Catalog (2026)

                ## 1. High-Density Compute Overview
                Enterprise compute configurations optimized for large-scale multilingual embedding generation, RAG reranking, and LLM fine-tuning.

                ## 2. Technical Specifications of GPU Accelerator Nodes

                | Server Model | Accelerators | GPU Memory | Interconnect | Target Workload |
                | --- | --- | --- | --- | --- |
                | DGX-H100 | 8x NVIDIA H100 SXM5 | 640 GB HBM3 | NVLink 900 GB/s | LLM Pre-training & BGE-M3 Dense Indexing |
                | HGX-L40S | 8x NVIDIA L40S PCIe | 384 GB GDDR6 | PCIe Gen 5 | Multimodal Vision Captioning & Serving |
                | RT-4090-Node | 4x RTX 4090 24GB | 96 GB GDDR6X | PCIe Gen 4 | Edge Inference & Embedding Extraction |

                ## 3. Storage and Ingestion Throughput
                All nodes feature NVMe-oF low latency storage with up to 25 GB/s sequential read for rapid multi-gigabyte document parsing.
                """, Encoding.UTF8);
        }
    }

    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
