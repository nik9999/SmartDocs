namespace Rag.Core.Contracts;

/// <summary>
/// Generates a text response from a prompt using GigaChat.
/// </summary>
public interface IGigaChatService
{
    /// <summary>
    /// Generates a response from the given prompt.
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken);
}
