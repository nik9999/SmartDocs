using UglyToad.PdfPig;

namespace SmartDocs.Ingestion.Multimodal;

/// <summary>
/// Local PDF image extractor backed by PdfPig (no Azure subscription
/// required). Extracts every embedded raster image, writes each to a
/// PNG/JPG file under the output directory, and returns
/// <see cref="ExtractedImage"/> records with empty captions —
/// <see cref="ImageCaptioner"/> fills the captions in a second pass.
///
/// For higher-quality extraction (layout-aware, table reconstruction,
/// scanned-PDF OCR) use Azure Document Intelligence's Layout API as an
/// alternative implementation of <see cref="IPdfImageExtractor"/>.
/// </summary>
public sealed class PdfPigImageExtractor : IPdfImageExtractor
{
    public string Implementation => "pdfpig";

    public async Task<IReadOnlyList<ExtractedImage>> ExtractAsync(
        string pdfPath,
        string outputDirectory,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        Directory.CreateDirectory(outputDirectory);
        var results = new List<ExtractedImage>();

        await Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfPath);
            int imgIndex = 0;
            var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var image in page.GetImages())
                {
                    if (!image.TryGetPng(out var bytes) || bytes is null)
                    {
                        continue;
                    }

                    // Skip tiny decorative icons, bullets, dividers (< 1KB or < 48x48)
                    if (bytes.Length < 1024 || image.WidthInSamples < 48 || image.HeightInSamples < 48)
                    {
                        continue;
                    }

                    // Deduplicate identical images across pages (e.g. repeated company logos in headers)
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                    if (!seenHashes.Add(hash))
                    {
                        continue;
                    }

                    var name = $"{documentId}-img-{imgIndex:D3}.png";
                    var path = Path.Combine(outputDirectory, name);
                    File.WriteAllBytes(path, bytes);
                    results.Add(new ExtractedImage(
                        ImageId: $"{documentId}#img-{imgIndex}",
                        PageNumber: page.Number,
                        Caption: string.Empty,
                        ImagePath: path));
                    imgIndex++;
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        return results;
    }
}
