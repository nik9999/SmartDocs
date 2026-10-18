using System;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Loaders;

/// <summary>
/// Configuration options for <see cref="DocxDocumentLoader"/>.
/// </summary>
public sealed class DocxDocumentLoaderOptions
{
    /// <summary>
    /// Gets or sets the template for page separator lines when page breaks are encountered.
    /// Format parameter {0} is replaced by the 1-based page number.
    /// Defaults to <c>"## Page {0}"</c>.
    /// If <see langword="null"/> or whitespace, page separator headings are omitted.
    /// </summary>
    public string PageSeparatorTemplate { get; set; } = "## Page {0}";

    /// <summary>
    /// Gets or sets whether to throw an <see cref="InvalidOperationException"/> if no text
    /// could be extracted from the Word document.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ThrowIfNoTextExtracted { get; set; } = true;

    /// <summary>
    /// Default silo to assign when it cannot be inferred from the directory structure.
    /// Defaults to <c>"technical-docs"</c>.
    /// </summary>
    public string DefaultSilo { get; set; } = "technical-docs";

    /// <summary>
    /// Default department to assign in metadata.
    /// Defaults to <c>"Engineering"</c>.
    /// </summary>
    public string DefaultDepartment { get; set; } = "Engineering";

    /// <summary>
    /// Default office/city to assign in metadata when not detected.
    /// Defaults to <c>"Central"</c>.
    /// </summary>
    public string DefaultOffice { get; set; } = "Central";

    /// <summary>
    /// Default confidentiality level to assign in metadata when not detected.
    /// Defaults to <c>"Public"</c>.
    /// </summary>
    public string DefaultConfidentialityLevel { get; set; } = "Public";

    /// <summary>
    /// Default document type to assign in metadata when not detected.
    /// Defaults to <c>"Specification"</c>.
    /// </summary>
    public string DefaultDocumentType { get; set; } = "Specification";

    /// <summary>
    /// Default author to assign in metadata when not detected from document properties or text.
    /// Defaults to <c>"Unknown"</c>.
    /// </summary>
    public string DefaultAuthor { get; set; } = "Unknown";

    /// <summary>
    /// Optional custom metadata extractor to override or augment metadata extraction.
    /// </summary>
    public Func<string, DocumentMetadata>? MetadataExtractor { get; set; }
}

