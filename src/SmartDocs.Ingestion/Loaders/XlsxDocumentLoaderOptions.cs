using System;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Loaders;

/// <summary>
/// Configuration options for <see cref="XlsxDocumentLoader"/>.
/// </summary>
public sealed class XlsxDocumentLoaderOptions
{
    /// <summary>
    /// Gets or sets the template for sheet separator headings.
    /// Format parameter {0} is replaced by the sheet name.
    /// Defaults to <c>"## Sheet: {0}"</c>.
    /// If <see langword="null"/> or whitespace, sheet separator headings are omitted.
    /// </summary>
    public string SheetSeparatorTemplate { get; set; } = "## Sheet: {0}";

    /// <summary>
    /// Gets or sets whether to throw an <see cref="InvalidOperationException"/> if no text or cell data
    /// could be extracted from the Excel spreadsheet.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ThrowIfNoTextExtracted { get; set; } = true;

    /// <summary>
    /// Default silo to assign when it cannot be inferred from the directory structure.
    /// Defaults to <c>"financial-reports"</c>.
    /// </summary>
    public string DefaultSilo { get; set; } = "financial-reports";

    /// <summary>
    /// Default department to assign in metadata.
    /// Defaults to <c>"Finance"</c>.
    /// </summary>
    public string DefaultDepartment { get; set; } = "Finance";

    /// <summary>
    /// Default office/city to assign in metadata when not detected.
    /// Defaults to <c>"Central"</c>.
    /// </summary>
    public string DefaultOffice { get; set; } = "Central";

    /// <summary>
    /// Default confidentiality level to assign in metadata when not detected.
    /// Defaults to <c>"Internal"</c>.
    /// </summary>
    public string DefaultConfidentialityLevel { get; set; } = "Internal";

    /// <summary>
    /// Default document type to assign in metadata when not detected.
    /// Defaults to <c>"Report"</c>.
    /// </summary>
    public string DefaultDocumentType { get; set; } = "Report";

    /// <summary>
    /// Default author to assign in metadata when not detected from workbook properties.
    /// Defaults to <c>"Finance Dept"</c>.
    /// </summary>
    public string DefaultAuthor { get; set; } = "Finance Dept";

    /// <summary>
    /// Optional custom metadata extractor to override or augment metadata extraction.
    /// </summary>
    public Func<string, DocumentMetadata>? MetadataExtractor { get; set; }
}
