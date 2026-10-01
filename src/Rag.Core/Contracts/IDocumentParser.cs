namespace Rag.Core.Contracts;

using Rag.Core.Documents;

/// <summary>
/// Parses a document source into a <see cref="Document"/>.
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// Determines whether this parser can handle the given content type.
    /// </summary>
    bool CanParse(string contentType);

    /// <summary>
    /// Parses the document source asynchronously.
    /// </summary>
    Task<Document> ParseAsync(
        DocumentSource source,
        CancellationToken cancellationToken);
}

/// <summary>
/// Represents an abstract document source.
/// </summary>
public sealed record DocumentSource(
    string Name,
    string ContentType,
    byte[] Content);
