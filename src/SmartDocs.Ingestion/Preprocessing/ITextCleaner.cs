namespace SmartDocs.Ingestion.Preprocessing;

/// <summary>
/// Low-level text sanitization interface for cleaning raw text strings.
/// </summary>
public interface ITextCleaner
{
    /// <summary>
    /// Normalizes and cleans the specified <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The raw text to sanitize.</param>
    /// <returns>The sanitized text.</returns>
    string Clean(string text);
}
