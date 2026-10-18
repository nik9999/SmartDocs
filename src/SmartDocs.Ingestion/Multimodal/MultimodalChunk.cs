using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Multimodal;

/// <summary>
/// A chunk that, in addition to text, references one or more extracted
/// images (charts, diagrams, scanned figures) and structured tables. The
/// chunk's <see cref="DocumentChunk.Text"/> embeds image captions and
/// markdown-rendered tables so the existing dense-retrieval path picks
/// it up unmodified.
/// </summary>
public sealed record MultimodalChunk(
    DocumentChunk Chunk,
    IReadOnlyList<ExtractedImage> Images,
    IReadOnlyList<ExtractedTable> Tables);

/// <param name="ImageId">Stable id of the form <c>{DocumentId}#img-{n}</c>.</param>
/// <param name="PageNumber">1-based PDF or document page/sheet number.</param>
/// <param name="Caption">LLM- or vision-model-generated description of the image.</param>
/// <param name="ImagePath">Path on disk where the extracted bitmap was written, or null if extraction was caption-only.</param>
/// <param name="SectionHeading">Name or title of the section/heading containing this image.</param>
/// <param name="StartCharOffset">Character offset in the document text where this image appears or is anchored.</param>
/// <param name="EndCharOffset">End character offset in the document text.</param>
public sealed record ExtractedImage(
    string ImageId,
    int PageNumber,
    string Caption,
    string? ImagePath,
    string? SectionHeading = null,
    int StartCharOffset = 0,
    int EndCharOffset = 0);

/// <param name="TableId">Stable id of the form <c>{DocumentId}#tbl-{n}</c>.</param>
/// <param name="PageNumber">1-based PDF or document page/sheet number.</param>
/// <param name="Markdown">The table rendered as a Markdown table — the format LLMs handle most reliably.</param>
/// <param name="SectionHeading">Name or title of the section/heading containing this table.</param>
/// <param name="StartCharOffset">Character offset where this table starts in the document text.</param>
/// <param name="EndCharOffset">Character offset where this table ends in the document text.</param>
public sealed record ExtractedTable(
    string TableId,
    int PageNumber,
    string Markdown,
    string? SectionHeading = null,
    int StartCharOffset = 0,
    int EndCharOffset = 0);
