using System;

namespace SmartDocs.Samples.StrategyTest.Cli.Cost;

/// <summary>
/// Tariff configuration and cost calculator for embedding and LLM generation stages.
/// </summary>
public sealed record CostPricingModel
{
    /// <summary>
    /// Cost in rubles per 1,000 embedding tokens.
    /// Default is 0.00 ₽ for local ONNX inference (e.g. BGE-M3).
    /// </summary>
    public decimal EmbeddingCostPer1KTokens { get; init; } = 0.00m;

    /// <summary>
    /// Cost in rubles per 1,000 LLM prompt (input) tokens.
    /// Default is 0.20 ₽ / 1K tokens for GigaChat.
    /// </summary>
    public decimal LlmInputCostPer1KTokens { get; init; } = 0.20m;

    /// <summary>
    /// Cost in rubles per 1,000 LLM completion (output) tokens.
    /// Default is 0.40 ₽ / 1K tokens for GigaChat.
    /// </summary>
    public decimal LlmOutputCostPer1KTokens { get; init; } = 0.40m;

    /// <summary>
    /// Calculates the cost breakdown for the specified token usages.
    /// </summary>
    public CostBreakdown Calculate(int embeddingTokens, int inputPromptTokens, int outputCompletionTokens)
    {
        decimal embeddingCost = (embeddingTokens / 1000.0m) * EmbeddingCostPer1KTokens;
        decimal promptCost = (inputPromptTokens / 1000.0m) * LlmInputCostPer1KTokens;
        decimal completionCost = (outputCompletionTokens / 1000.0m) * LlmOutputCostPer1KTokens;
        decimal totalCost = embeddingCost + promptCost + completionCost;

        return new CostBreakdown(
            EmbeddingTokens: embeddingTokens,
            PromptTokens: inputPromptTokens,
            CompletionTokens: outputCompletionTokens,
            EmbeddingCost: embeddingCost,
            PromptCost: promptCost,
            CompletionCost: completionCost,
            TotalCost: totalCost);
    }
}

/// <summary>
/// Detailed cost breakdown for pipeline stages.
/// </summary>
public sealed record CostBreakdown(
    int EmbeddingTokens,
    int PromptTokens,
    int CompletionTokens,
    decimal EmbeddingCost,
    decimal PromptCost,
    decimal CompletionCost,
    decimal TotalCost)
{
    public int TotalTokens => EmbeddingTokens + PromptTokens + CompletionTokens;
}

