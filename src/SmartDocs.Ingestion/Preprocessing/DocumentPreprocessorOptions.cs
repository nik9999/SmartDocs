using System;
using System.Collections.Generic;

namespace SmartDocs.Ingestion.Preprocessing;

/// <summary>
/// Knobs controlling text sanitization and page filtering before chunking.
/// </summary>
public sealed class DocumentPreprocessorOptions
{
    /// <summary>
    /// When <see langword="true"/>, glues hyphenated words split across line breaks
    /// (e.g., "преобразова-\nтель" -> "преобразователь").
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool Dehyphenate { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, removes invisible Unicode characters (soft hyphens \u00AD, zero-width spaces \u200B)
    /// and normalizes whitespace (\u00A0 -> regular space, multiple spaces -> single space, \r\n -> \n).
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool NormalizeWhitespace { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, collapses dot leaders in tables of contents (e.g., "Введение ........... 3" -> "Введение 3").
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool RemoveDotLeaders { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, filters out empty pages or pages containing only boilerplate / page numbers.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool FilterEmptyPages { get; set; } = true;

    /// <summary>
    /// Minimum number of informative characters (excluding page markers, whitespace, and standalone numbers)
    /// required for a page to be kept. Default is 40.
    /// </summary>
    public int MinInformativeCharactersPerPage { get; set; } = 40;

    /// <summary>
    /// When <see langword="true"/>, detects running headers and footers that repeat across >= 50% of the document pages
    /// and strips them from the page body. Default is <see langword="true"/>.
    /// </summary>
    public bool StripRepeatingHeadersAndFooters { get; set; } = true;

    /// <summary>
    /// Set of case-insensitive boilerplate phrases that indicate a blank/filler page (e.g. "для заметок").
    /// </summary>
    public HashSet<string> BoilerplatePhrases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "для заметок",
        "для записей",
        "страница намеренно оставлена пустой",
        "намеренно оставлено пустым",
        "this page intentionally left blank",
        "notes"
    };
}
