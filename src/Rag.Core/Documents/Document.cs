namespace Rag.Core.Documents;

/// <summary>
/// Represents a logical unit of source material.
/// </summary>
public sealed class Document
{
    public Guid DocumentId { get; init; }
    public string Title { get; init; }
    public string Content { get; init; }
    public string Source { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    public Document(
        Guid documentId,
        string title,
        string content,
        string source,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title must not be null or empty.", nameof(title));

        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content must not be null or empty.", nameof(content));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source must not be null or empty.", nameof(source));

        DocumentId = documentId;
        Title = title;
        Content = content;
        Source = source;
        Metadata = metadata;
    }

    public Document(string title, string content, string source, IReadOnlyDictionary<string, string>? metadata = null)
        : this(Guid.NewGuid(), title, content, source, metadata ?? new Dictionary<string, string>())
    {
    }
}
