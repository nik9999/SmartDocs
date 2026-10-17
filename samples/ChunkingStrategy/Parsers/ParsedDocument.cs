using System.Collections.Generic;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.Samples.ChunkingStrategy.Parsers;

/// <summary>
/// Represents the result of parsing a raw document file into text, structured tables, and extracted images.
/// </summary>
public sealed record ParsedDocument(
    Document Document,
    IReadOnlyList<ExtractedImage> Images,
    IReadOnlyList<ExtractedTable> Tables);
