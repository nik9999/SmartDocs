using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.GigaChat;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.Demo.Cli;

/// <summary>
/// 4. Мультимодальный чанкер документов: извлекает изображения, находит подписи в тексте,
/// формирует расширенные описания через ImageCaptioner и связывает их с текстовыми чанками.
/// </summary>
public sealed class DocumentMultimodalChunker(
    IChunker innerTextChunker,
    IPdfImageExtractor extractor,
    ImageCaptioner captioner,
    ITokenCounter? tokenCounter = null,
    int maxTokens = 450,
    GigaChatClient? gigaChatClient = null) : IChunker
{
    public string Strategy => $"doc-multimodal({innerTextChunker.Strategy})";

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ExtractedImage> captionedImages = [];

        string pdfPath = document.SourcePath;
        if (File.Exists(pdfPath) && pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "smartdocs_extracted_images", document.Metadata.Id);
            Console.WriteLine("      [Мультимодальность] Извлечение и фильтрация изображений из PDF...");
            
            var extracted = await extractor.ExtractAsync(pdfPath, tempDir, document.Metadata.Id, cancellationToken).ConfigureAwait(false);
            if (extracted.Count > 0)
            {
                Console.WriteLine($"      [Мультимодальность] Отобрано изображений: {extracted.Count}. Запуск Vision LLM для подписей...");

                if (gigaChatClient != null)
                {
                    try
                    {
                        string targetModel = gigaChatClient.Options.DefaultModel;
                        var allModels = await gigaChatClient.GetModelsAsync(cancellationToken).ConfigureAwait(false);
                        var modelDto = allModels.FirstOrDefault(m => string.Equals(m.Id, targetModel, StringComparison.OrdinalIgnoreCase));
                        string avail = allModels.Count > 0 ? string.Join(", ", allModels.Select(m => m.Id)) : "нет данных";

                        if (modelDto != null && !string.IsNullOrEmpty(modelDto.Id))
                        {
                            Console.WriteLine($"      [GigaChat API] Используемая модель: {modelDto.Id} (владелец: {modelDto.OwnedBy ?? "salute"}, тип: {modelDto.Type ?? "chat"})");
                            Console.WriteLine($"      [GigaChat API] Доступные модели по API: {avail}");
                        }
                        else
                        {
                            Console.WriteLine($"      [GigaChat API] Модель: {targetModel} (доступные в API: {avail})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      [GigaChat API] Модель: {gigaChatClient.Options.DefaultModel} (ошибка запроса /models: {ex.Message})");
                    }
                }

                var list = new List<ExtractedImage>(extracted.Count);
                
                for (int i = 0; i < extracted.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Console.WriteLine($"      [Vision LLM] Генерация описания изображения {i + 1}/{extracted.Count} (стр. {extracted[i].PageNumber})...");
                    
                    ExtractedImage captioned;
                    try
                    {
                        captioned = await captioner.CaptionAsync(extracted[i], cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"        -> [WARN] Ошибка вызова Vision LLM: {ex.Message}");
                        captioned = extracted[i] with { Caption = string.Empty };
                    }
                    
                    // Гибридный подход: ищем подпись к рисунку именно на странице изображения
                    string figureCaption = ExtractFigureCaption(document.Content, extracted[i].PageNumber);
                    
                    string combinedCaption;
                    if (!string.IsNullOrEmpty(figureCaption) && !string.IsNullOrWhiteSpace(captioned.Caption))
                    {
                        combinedCaption = $"[Подпись к рисунку: {figureCaption}]\n[Описание схемы/чертежа: {captioned.Caption}]";
                    }
                    else if (!string.IsNullOrEmpty(figureCaption))
                    {
                        combinedCaption = $"[Подпись к рисунку: {figureCaption}]";
                    }
                    else if (!string.IsNullOrWhiteSpace(captioned.Caption))
                    {
                        combinedCaption = $"[Описание схемы/чертежа: {captioned.Caption}]";
                    }
                    else
                    {
                        combinedCaption = string.Empty;
                    }

                    list.Add(captioned with { Caption = combinedCaption });

                    if (!string.IsNullOrEmpty(figureCaption))
                    {
                        Console.WriteLine($"        -> Подпись: {figureCaption}");
                    }
                    if (!string.IsNullOrWhiteSpace(captioned.Caption))
                    {
                        var cleanCaption = captioned.Caption.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
                        if (cleanCaption.Length > 120)
                        {
                            cleanCaption = string.Concat(cleanCaption.AsSpan(0, 120), "...");
                        }
                        Console.WriteLine($"        -> Описание LLM: {cleanCaption}");
                    }
                }
                captionedImages = list;
            }
            else
            {
                Console.WriteLine("      [Мультимодальность] Значимых изображений в документе не обнаружено.");
            }
        }

        var mmChunker = new MultimodalChunker(
            innerTextChunker,
            captionedImages,
            [],
            tokenCounter,
            maxTokens);

        await foreach (var mmChunk in mmChunker.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
        {
            var chunk = mmChunk.Chunk;

            // Инжекция скомбинированных подписей и описаний изображений в текст чанка
            if (mmChunk.Images.Count > 0)
            {
                var sb = new StringBuilder(chunk.Text);
                bool modified = false;

                foreach (var img in mmChunk.Images)
                {
                    if (!string.IsNullOrWhiteSpace(img.Caption) &&
                        !chunk.Text.Contains(img.Caption, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append("\n\n").Append(img.Caption);
                        modified = true;
                    }
                }

                if (modified)
                {
                    chunk = chunk with { Text = sb.ToString() };
                }
            }

            yield return chunk;
        }
    }

    private static string ExtractFigureCaption(string fullDocumentText, int pageNumber)
    {
        if (pageNumber <= 0 || string.IsNullOrWhiteSpace(fullDocumentText))
        {
            return string.Empty;
        }

        string pageMarker = $"[Page {pageNumber}]";
        int pageStart = fullDocumentText.IndexOf(pageMarker, StringComparison.OrdinalIgnoreCase);
        if (pageStart < 0)
        {
            return string.Empty;
        }

        pageStart += pageMarker.Length;
        string nextPageMarker = $"[Page {pageNumber + 1}]";
        int pageEnd = fullDocumentText.IndexOf(nextPageMarker, pageStart, StringComparison.OrdinalIgnoreCase);
        string pageText = pageEnd >= 0 ? fullDocumentText[pageStart..pageEnd] : fullDocumentText[pageStart..];

        var match = Regex.Match(pageText, @"(Рис\.|Рисунок)\s*\d+[\s\.\—\-]+[^\n]+", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Value.Trim();
        }

        return string.Empty;
    }
}