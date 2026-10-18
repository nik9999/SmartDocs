using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Preprocessing;

/// <summary>
/// Default preprocessor and text sanitizer for corporate and technical documents.
/// Cleans extraction artifacts before chunking: glues broken word-wraps, removes invisible
/// Unicode characters, collapses dot leaders, detects and eliminates repeating headers/footers,
/// and filters out blank or boilerplate pages ("Для заметок").
/// </summary>
public sealed partial class DocumentPreprocessor : IDocumentPreprocessor, ITextCleaner
{
    private readonly DocumentPreprocessorOptions _options;

    // Word wrap dehyphenation: letters followed by hyphen + newline + lowercase letters
    private static readonly Regex SyllableDehyphenRegex = new(
        @"(?<=\p{L}{2,})[-‐‑]\n\s*(?=\p{Ll}{2,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Alphanumeric compound code joins: e.g. "RS-\n485" -> "RS-485"
    private static readonly Regex CompoundHyphenJoinRegex = new(
        @"(?<=[A-Za-zА-Яа-я0-9])[-‐‑]\n\s*(?=[A-Za-zА-Яа-я0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Dot leaders in TOC / indexes: 3 or more dots with optional spaces
    private static readonly Regex DotLeadersRegex = new(
        @"(?:\.\s*){3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Horizontal whitespace collapse (spaces and tabs on the same line)
    private static readonly Regex HorizontalSpacesRegex = new(
        @"[^\S\n]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Multiple consecutive newlines (3 or more)
    private static readonly Regex ConsecutiveNewlinesRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Page marker pattern: e.g. "## Page 1", "## Page 12"
    private static readonly Regex PageMarkerRegex = new(
        @"^##\s+Page\s+\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public DocumentPreprocessor(DocumentPreprocessorOptions? options = null)
    {
        _options = options ?? new DocumentPreprocessorOptions();
    }

    /// <inheritdoc/>
    public Document Preprocess(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Content))
        {
            return document;
        }

        var cleanedText = Clean(document.Content);

        // If page filtering or repeating header/footer stripping is enabled, process per-page blocks
        if (_options.FilterEmptyPages || _options.StripRepeatingHeadersAndFooters)
        {
            cleanedText = ProcessPages(cleanedText);
        }

        var cleanedMetadata = document.Metadata with
        {
            Title = Clean(document.Metadata.Title),
            Author = Clean(document.Metadata.Author)
        };

        return document with { Content = cleanedText, Metadata = cleanedMetadata };
    }

    /// <inheritdoc/>
    public string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // 1. Remove invisible Unicode characters, trademark/copyright symbols, and normalize non-breaking spaces
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                // Soft hyphen, zero-width space, zero-width non-joiner, zero-width joiner, BOM
                case '\u00AD':
                case '\u200B':
                case '\u200C':
                case '\u200D':
                case '\uFEFF':
                // Registered trademark, trademark, copyright symbols
                case '®': // \u00AE
                case '™': // \u2122
                case '©': // \u00A9
                    break;

                // Non-breaking spaces -> standard space
                case '\u00A0':
                case '\u202F':
                    sb.Append(' ');
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        var result = sb.ToString();

        // 2. Normalize line endings to \n
        result = result.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        // 3. Word-wrap dehyphenation
        if (_options.Dehyphenate)
        {
            // First: lowercase syllable wrap -> remove hyphen completely
            result = SyllableDehyphenRegex.Replace(result, string.Empty);
            // Second: compound/code hyphen wrap -> preserve hyphen without newline
            result = CompoundHyphenJoinRegex.Replace(result, "-");
        }

        // 4. Dot leaders in tables of contents
        if (_options.RemoveDotLeaders)
        {
            result = DotLeadersRegex.Replace(result, " ");
        }

        // 5. Whitespace normalization
        if (_options.NormalizeWhitespace)
        {
            result = HorizontalSpacesRegex.Replace(result, " ");
            result = ConsecutiveNewlinesRegex.Replace(result, "\n\n");
        }

        return result.Trim();
    }

    private string ProcessPages(string content)
    {
        var rawPages = SplitIntoPages(content);
        if (rawPages.Count <= 1)
        {
            return content;
        }

        // 1. Strip repeating running headers and footers across pages if enabled
        if (_options.StripRepeatingHeadersAndFooters && rawPages.Count >= 3)
        {
            StripRepeatingHeadersAndFooters(rawPages);
        }

        // 2. Filter empty and boilerplate pages if enabled
        var keptPages = new List<PageBlock>();
        foreach (var page in rawPages)
        {
            if (_options.FilterEmptyPages && IsEmptyOrBoilerplatePage(page))
            {
                continue;
            }

            keptPages.Add(page);
        }

        if (keptPages.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(content.Length);
        for (int i = 0; i < keptPages.Count; i++)
        {
            var p = keptPages[i];
            if (!string.IsNullOrEmpty(p.Header))
            {
                sb.AppendLine(p.Header);
            }
            sb.AppendLine(p.Body.Trim());
            if (i < keptPages.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static List<PageBlock> SplitIntoPages(string content)
    {
        var pages = new List<PageBlock>();
        using var reader = new StringReader(content);
        string? line;
        string? currentHeader = null;
        var currentBody = new StringBuilder();

        while ((line = reader.ReadLine()) != null)
        {
            if (PageMarkerRegex.IsMatch(line))
            {
                if (currentHeader != null || currentBody.Length > 0)
                {
                    pages.Add(new PageBlock(currentHeader ?? string.Empty, currentBody.ToString().Trim()));
                    currentBody.Clear();
                }
                currentHeader = line.Trim();
            }
            else
            {
                currentBody.AppendLine(line);
            }
        }

        if (currentHeader != null || currentBody.Length > 0)
        {
            pages.Add(new PageBlock(currentHeader ?? string.Empty, currentBody.ToString().Trim()));
        }

        return pages;
    }

    private static readonly Regex PageNumberFooterRegex = new(
        @"^(?:(?:Стр\.?|Page)\s*)?\d+(?:\s*(?:из|of|/)\s*\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DigitsRegex = new(
        @"\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void StripRepeatingHeadersAndFooters(List<PageBlock> pages)
    {
        var headerCandidateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerTemplateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var footerCandidateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var footerTemplateCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pages)
        {
            var lines = page.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            // Top line candidate
            var top = lines[0];
            if (IsEligibleHeaderFooter(top))
            {
                headerCandidateCounts[top] = headerCandidateCounts.GetValueOrDefault(top) + 1;
                var t = DigitsRegex.Replace(top, "#");
                headerTemplateCounts[t] = headerTemplateCounts.GetValueOrDefault(t) + 1;
            }

            // Bottom line candidate
            var bottom = lines[^1];
            if (IsEligibleHeaderFooter(bottom))
            {
                footerCandidateCounts[bottom] = footerCandidateCounts.GetValueOrDefault(bottom) + 1;
                var t = DigitsRegex.Replace(bottom, "#");
                footerTemplateCounts[t] = footerTemplateCounts.GetValueOrDefault(t) + 1;
            }
        }

        int threshold = (int)Math.Ceiling(pages.Count * 0.5);

        var repeatingHeaders = headerCandidateCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repeatingHeaderTemplates = headerTemplateCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repeatingFooters = footerCandidateCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repeatingFooterTemplates = footerTemplateCounts
            .Where(kvp => kvp.Value >= threshold)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var lines = p.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var top = lines[0];
            if (repeatingHeaders.Contains(top) || repeatingHeaderTemplates.Contains(DigitsRegex.Replace(top, "#")))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count > 0)
            {
                var bottom = lines[^1];
                if (PageNumberFooterRegex.IsMatch(bottom) ||
                    repeatingFooters.Contains(bottom) ||
                    repeatingFooterTemplates.Contains(DigitsRegex.Replace(bottom, "#")))
                {
                    lines.RemoveAt(lines.Count - 1);
                }
            }

            pages[i] = p with { Body = string.Join("\n", lines).Trim() };
        }
    }

    private bool IsEmptyOrBoilerplatePage(PageBlock page)
    {
        var body = page.Body.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return true;
        }

        // Tables, figures, or code blocks must never be discarded
        if (body.Contains('|') || body.Contains("![") || body.Contains("```"))
        {
            return false;
        }

        // Check if page contains only boilerplate phrases (e.g. "Для заметок")
        var normalizedForCheck = body.TrimEnd('.', '!', '?', ' ');
        if (_options.BoilerplatePhrases.Contains(normalizedForCheck))
        {
            return true;
        }

        // Count informative letters (Cyrillic or Latin)
        int letterCount = 0;
        foreach (char c in body)
        {
            if (char.IsLetter(c))
            {
                letterCount++;
            }
        }

        // If the page has almost no letters (e.g. just page number "15"), drop it
        if (letterCount < _options.MinInformativeCharactersPerPage)
        {
            return true;
        }

        return false;
    }

    private static bool IsEligibleHeaderFooter(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith('|'))
        {
            return false;
        }

        // Standalone numbers are handled as page numbers
        return line.Length is >= 3 and <= 120;
    }

    private sealed record PageBlock(string Header, string Body);
}
