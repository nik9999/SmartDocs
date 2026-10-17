namespace SmartDocs.Ingestion.Chunking;

/// <summary>
/// Обособленные бюджеты токенов для многоэтапного пайплайна чанкинга:
/// бюджет исходного текста (<see cref="BaseTextBudget"/>), контекста LLM (<see cref="ContextBudget"/>),
/// мультимодальных описаний (<see cref="MultimodalBudget"/>) и итоговый жесткий лимит (<see cref="FinalTargetCeiling"/>).
/// </summary>
/// <remarks>
/// Предотвращает переполнение контекста эмбеддинг-моделей (например, BGE-M3 с лимитом 450 или 512 токенов),
/// когда обогащение чанка (Anthropic Contextual Retrieval и Vision LLM подписи) увеличивает его размер
/// сверх лимита уже после первичного разбиения.
/// </remarks>
public sealed record ChunkingTokenBudgets
{
    /// <summary>
    /// Итоговый предельный бюджет токенов для одного чанка, подаваемого в модель эмбеддингов (по умолчанию 450).
    /// </summary>
    public int FinalTargetCeiling { get; init; } = 450;

    /// <summary>
    /// Бюджет токенов, зарезервированный под ситуационный контекст от LLM (Contextual Retrieval).
    /// </summary>
    public int ContextBudget { get; init; } = 80;

    /// <summary>
    /// Бюджет токенов, зарезервированный под мультимодальные подписи изображений и Markdown-таблицы.
    /// </summary>
    public int MultimodalBudget { get; init; } = 70;

    /// <summary>
    /// Допустимый бюджет токенов для исходного немодифицированного текста чанка.
    /// Рассчитывается как: FinalTargetCeiling - ContextBudget - MultimodalBudget.
    /// </summary>
    public int BaseTextBudget => FinalTargetCeiling - ContextBudget - MultimodalBudget;

    /// <summary>
    /// Инициализирует конфигурацию бюджетов токенов с проверкой граничных условий.
    /// </summary>
    /// <param name="finalTargetCeiling">Итоговый жесткий лимит токенов для одного чанка.</param>
    /// <param name="contextBudget">Резерв токенов под ситуационный контекст LLM.</param>
    /// <param name="multimodalBudget">Резерв токенов под мультимодальные подписи и таблицы.</param>
    public ChunkingTokenBudgets(int finalTargetCeiling = 450, int contextBudget = 80, int multimodalBudget = 70)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(finalTargetCeiling);
        ArgumentOutOfRangeException.ThrowIfNegative(contextBudget);
        ArgumentOutOfRangeException.ThrowIfNegative(multimodalBudget);

        if (contextBudget + multimodalBudget >= finalTargetCeiling)
        {
            throw new ArgumentException(
                $"Сумма резервов (ContextBudget={contextBudget} + MultimodalBudget={multimodalBudget}) должна быть меньше итогового лимита FinalTargetCeiling={finalTargetCeiling}.");
        }

        FinalTargetCeiling = finalTargetCeiling;
        ContextBudget = contextBudget;
        MultimodalBudget = multimodalBudget;
    }
}
