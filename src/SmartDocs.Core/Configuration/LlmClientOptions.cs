using System.ComponentModel.DataAnnotations;

namespace SmartDocs.Core.Configuration;

/// <summary>The set of LLM providers SmartDocs is wired to talk to.</summary>
public enum LlmProvider
{
    /// <summary>
    /// Local Ollama via <c>OllamaSharp.OllamaApiClient</c>. Default for
    /// development; works offline and incurs no API cost.
    /// </summary>
    Ollama,

    /// <summary>
    /// Sber GigaChat via <c>SmartDocs.GigaChat</c>.
    /// </summary>
    GigaChat,
}

/// <summary>
/// Configuration for the chat and embedding clients SmartDocs registers
/// in DI. Bound from the <c>SmartDocs:Llm</c> section of
/// <c>appsettings.json</c> by
/// <see cref="DependencyInjection.ServiceCollectionExtensions.AddSmartDocsCore"/>.
/// </summary>
public sealed class LlmClientOptions
{
    /// <summary>Configuration section path bound to this class.</summary>
    public const string SectionName = "SmartDocs:Llm";

    /// <summary>Which provider to wire up.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    /// <summary>
    /// Provider endpoint. For Ollama: <c>http://localhost:11434</c>. For
    /// GigaChat: the API base URL (e.g. <c>https://gigachat.devices.sberbank.ru/api/v1</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Endpoint { get; set; } = "http://localhost:11434";


    /// <summary>
    /// Base64-encoded Authorization Key (Client ID:Client Secret) for Sber GigaChat.
    /// Used when <see cref="Provider"/> is <see cref="LlmProvider.GigaChat"/>.
    /// </summary>
    public string? AuthorizationKey { get; set; }

    /// <summary>
    /// Client ID from Sber Developers Studio (alternative to <see cref="AuthorizationKey"/>).
    /// Used when <see cref="Provider"/> is <see cref="LlmProvider.GigaChat"/>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client Secret from Sber Developers Studio (alternative to <see cref="AuthorizationKey"/>).
    /// Used when <see cref="Provider"/> is <see cref="LlmProvider.GigaChat"/>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// OAuth access scope for Sber GigaChat. Defaults to <c>GIGACHAT_API_PERS</c>.
    /// </summary>
    public string GigaChatScope { get; set; } = "GIGACHAT_API_PERS";

    /// <summary>
    /// Chat model identifier. For Ollama: e.g. <c>llama3.2</c>. For GigaChat:
    /// e.g. <c>GigaChat</c>, <c>GigaChat-Pro</c>, <c>GigaChat-Max</c>.
    /// If null or empty, defaults automatically to the recommended model for the selected <see cref="Provider"/>.
    /// </summary>
    public string? ChatModel { get; set; }

    /// <summary>
    /// Resolves the effective model name based on <see cref="Provider"/> when not explicitly configured.
    /// </summary>
    public string ResolveChatModel()
    {
        if (!string.IsNullOrWhiteSpace(ChatModel))
        {
            return ChatModel.Trim();
        }

        return Provider switch
        {
            LlmProvider.GigaChat => SmartDocs.GigaChat.GigaChatModels.GigaChat2Pro,
            _ => "llama3.2",
        };
    }
}
