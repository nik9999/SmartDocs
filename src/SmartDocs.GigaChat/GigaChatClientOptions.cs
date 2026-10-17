using System;
using System.Net.Http;

namespace SmartDocs.GigaChat;

/// <summary>
/// Configuration options for connecting to the Sber GigaChat API.
/// </summary>
public sealed class GigaChatClientOptions
{
    /// <summary>
    /// Configuration section name when bound from IConfiguration.
    /// </summary>
    public const string SectionName = "GigaChat";

    /// <summary>
    /// Base64-encoded Authorization Key (Client ID:Client Secret) obtained from Sber Developers Studio.
    /// </summary>
    public string? AuthorizationKey { get; set; }

    /// <summary>
    /// Optional pre-obtained temporary Bearer access token (if managing OAuth tokens externally).
    /// </summary>
    public string? AccessToken { get; set; }


    /// <summary>
    /// Explicit Client ID from Sber Developers Studio (alternative to <see cref="AuthorizationKey"/>; used together with <see cref="ClientSecret"/>).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Explicit Client Secret from Sber Developers Studio (alternative to <see cref="AuthorizationKey"/>; used together with <see cref="ClientId"/>).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Resolves the Base64-encoded Authorization Key from <see cref="AuthorizationKey"/> or <see cref="ClientId"/> + <see cref="ClientSecret"/>.
    /// </summary>
    public string? ResolveAuthorizationKey()
    {
        if (!string.IsNullOrWhiteSpace(AuthorizationKey))
        {
            return AuthorizationKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret))
        {
            var raw = $"{ClientId}:{ClientSecret}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
        }

        return null;
    }

    /// <summary>
    /// OAuth access scope. Defaults to <see cref="GigaChatScope.Personal"/>.
    /// </summary>
    public string Scope { get; set; } = GigaChatScope.Personal;

    /// <summary>
    /// Sber OAuth token endpoint. Defaults to <c>https://ngw.devices.sberbank.ru:9443/api/v2/oauth</c>.
    /// </summary>
    public Uri AuthUrl { get; set; } = new("https://ngw.devices.sberbank.ru:9443/api/v2/oauth");

    /// <summary>
    /// GigaChat REST API base URL. Defaults to <c>https://gigachat.devices.sberbank.ru/api/v1</c>.
    /// Can also be configured to <c>https://api.giga.chat/v1</c>.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://gigachat.devices.sberbank.ru/api/v1");

    /// <summary>
    /// Default chat completion model. Defaults to <see cref="GigaChatModels.GigaChat2"/> ("GigaChat-2" / GigaChat 2 Lite).
    /// </summary>
    public string DefaultModel { get; set; } = GigaChatModels.GigaChat2;

    /// <summary>
    /// Default embedding model. Defaults to <see cref="GigaChatModels.Embeddings"/>.
    /// </summary>
    public string DefaultEmbeddingModel { get; set; } = GigaChatModels.Embeddings;

    /// <summary>
    /// When true, bypasses custom SSL certificate validation for Sberbank endpoints signed by Russian Trusted Root CA.
    /// Defaults to <c>true</c> for out-of-the-box cross-platform operation.
    /// </summary>
    public bool IgnoreSslValidation { get; set; } = true;

    /// <summary>
    /// HTTP request timeout. Defaults to 120 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Optional custom <see cref="HttpClient"/> instance to use for all outbound calls.
    /// If null, a managed client configured with <see cref="IgnoreSslValidation"/> is created.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
