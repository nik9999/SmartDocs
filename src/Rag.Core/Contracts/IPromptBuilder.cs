namespace Rag.Core.Contracts;

using Rag.Core.Generation;

/// <summary>
/// Builds a prompt string from a RAG context.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Builds a prompt from the given RAG context.
    /// </summary>
    string Build(RagContext context);
}
