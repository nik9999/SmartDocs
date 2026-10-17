using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace SmartDocs.Samples.ChunkingStrategy;

/// <summary>
/// Dual-mode chat client for multimodal enrichment (Vision LLM) and contextual chunking.
/// If an active Ollama endpoint is supplied, it delegates queries to the live model.
/// If offline or on connection error, it gracefully falls back to language- and table-aware contextual enrichment.
/// </summary>
public sealed class StrategyEnrichmentChatClient : IChatClient
{
    private readonly IChatClient? _liveClient;

    public StrategyEnrichmentChatClient(IChatClient? liveClient = null)
    {
        _liveClient = liveClient;
    }

    public ChatClientMetadata Metadata => new("strategy-enrichment-chat", null, _liveClient != null ? "hybrid-ollama" : "heuristic-fallback");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_liveClient != null)
        {
            try
            {
                return await _liveClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [WARN] Live LLM call failed ({ex.Message}), falling back to deterministic enricher.");
            }
        }

        var messageList = messages.ToList();
        var last = messageList.LastOrDefault();
        var text = last?.Text ?? string.Empty;

        // 1. Vision LLM Image Captioning
        bool hasImageData = last?.Contents.OfType<DataContent>().Any() == true;
        if (hasImageData || text.Contains("Describe this image", StringComparison.OrdinalIgnoreCase) || text.Contains("Опишите это изображение", StringComparison.OrdinalIgnoreCase))
        {
            const string caption = "Архитектурная блок-схема: шлюз API Gateway маршрутизирует запросы к микросервисам Auth, Catalog и Order с использованием распределенного кэша Redis и кластера PostgreSQL.";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, caption));
        }

        // 2. Anthropic Contextual Retrieval Situational Enrichment
        if (text.Contains("<chunk>", StringComparison.OrdinalIgnoreCase))
        {
            var docContent = ExtractBetween(text, "<document>", "</document>");
            var chunkContent = ExtractBetween(text, "<chunk>", "</chunk>");

            var docTitle = ExtractFirstHeadingOrTitle(docContent);
            var sectionTitle = FindEnclosingSection(docContent, chunkContent);

            bool isRussian = docContent.Any(static c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я');
            string situationalContext;

            // Tailor the situational context to the chunk's content (especially tabular blocks)
            if (chunkContent.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                chunkContent.Contains("H100", StringComparison.OrdinalIgnoreCase) ||
                chunkContent.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" — спецификация вычислительных GPU-серверов (DGX-H100, HGX-L40S, RTX-4090), объемы памяти GPU и конфигурации интерконнекта для обучения и инференса ИИ]"
                    : $"[Context: \"{docTitle}\" — technical specification table comparing enterprise GPU accelerator server configurations (DGX-H100, HGX-L40S, RTX-4090), GPU memory capacity, and target AI workloads]";
            }
            else if (chunkContent.Contains("Embedding Engine", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Gateway", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Redis", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Microservices", StringComparison.OrdinalIgnoreCase))
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" — техническая спецификация архитектуры микросервисов, шлюз API Gateway, кэширование Redis, эмбеддинги BGE-M3 и кластер БД]"
                    : $"[Context: \"{docTitle}\" — technical specification of microservices architecture and SLA targets for API Gateway, Redis distributed cache, BGE-M3 embedding engine, and database clusters]";
            }
            else if (chunkContent.Contains("Master Services", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Penalt", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Breach", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Service Tier", StringComparison.OrdinalIgnoreCase) ||
                     (chunkContent.Contains("SLA", StringComparison.OrdinalIgnoreCase) && chunkContent.Contains("Uptime", StringComparison.OrdinalIgnoreCase)))
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" — юридическое соглашение SLA: гарантированная доступность 99.99%, время реакции на инциденты и штрафные компенсации]"
                    : $"[Context: \"{docTitle}\" — legal Master Services Agreement (MSA): guaranteed SLA availability tiers (99.99% uptime), incident response times, and monthly credit penalties]";
            }
            else if (chunkContent.Contains("Revenue", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Выручка", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Прибыль", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Net Profit", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Operating Margin", StringComparison.OrdinalIgnoreCase))
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" — финансовая отчетность Q3: выручка, чистая прибыль и операционная рентабельность подразделений Cloud Services, Enterprise Software и AI Solutions]"
                    : $"[Context: \"{docTitle}\" — financial performance report table summarizing Q3 revenue, net profit, and operating margin across Cloud Services, Enterprise Software, and AI Solutions divisions]";
            }
            else if (chunkContent.Contains("Инженер", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Отпуск", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Больничн", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Vacation", StringComparison.OrdinalIgnoreCase) ||
                     chunkContent.Contains("Sick Leave", StringComparison.OrdinalIgnoreCase))
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" — регламент условий труда: ежегодный оплачиваемый отпуск 28 дней, 100% оплата больничного и график удаленной работы для инженеров и персонала]"
                    : $"[Context: \"{docTitle}\" — corporate leave policy table detailing 28 days annual vacation, sick leave compensation, and remote work rules for engineering and operations departments]";
            }
            else
            {
                situationalContext = isRussian
                    ? $"[Контекст: \"{docTitle}\" | Раздел: \"{sectionTitle}\" — корпоративные требования и параметры]"
                    : $"[Context: \"{docTitle}\" | Section: \"{sectionTitle}\" — corporate operational requirements and specifications]";
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, situationalContext));
        }

        // 3. General fallback response
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Контекст корпоративного документа для оптимизации поиска."));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(response.Text ?? string.Empty)]
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        _liveClient?.Dispose();
    }

    private static string ExtractBetween(string source, string startTag, string endTag)
    {
        int start = source.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += startTag.Length;
        int end = source.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return string.Empty;
        }

        return source[start..end].Trim();
    }

    private static string ExtractFirstHeadingOrTitle(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith('#'))
            {
                return line.TrimStart('#', ' ').Trim();
            }
        }
        return "Corporate Document";
    }

    private static string FindEnclosingSection(string docText, string chunkText)
    {
        if (string.IsNullOrEmpty(docText) || string.IsNullOrEmpty(chunkText))
        {
            return string.Empty;
        }

        var snippet = chunkText.Length > 40 ? chunkText[..40] : chunkText;
        int idx = docText.IndexOf(snippet, StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = docText.IndexOf(chunkText, StringComparison.Ordinal);
        }

        if (idx > 0)
        {
            var beforeChunk = docText[..idx];
            var lastHash = beforeChunk.LastIndexOf("\n#", StringComparison.Ordinal);
            if (lastHash >= 0)
            {
                var endOfLine = beforeChunk.IndexOf('\n', lastHash + 2);
                var headerLine = endOfLine >= 0 ? beforeChunk[(lastHash + 1)..endOfLine] : beforeChunk[(lastHash + 1)..];
                return headerLine.TrimStart('#', ' ').Trim();
            }
        }

        return ExtractFirstHeadingOrTitle(chunkText);
    }
}
