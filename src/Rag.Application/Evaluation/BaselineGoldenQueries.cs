using Rag.Core.Documents;
using Rag.Core.Retrieval;

namespace Rag.Application.Evaluation;

/// <summary>
/// Provides deterministic <see cref="GoldenQuery"/> definitions for baseline evaluation.
/// </summary>
/// <remarks>
/// <para>
/// The queries are based on realistic sensor monitoring terminology used throughout
/// the SmartDocs codebase (cyrillic terms from existing FTS5 tests).
/// </para>
/// <para>
/// Each query references a fixed set of <c>DocumentId</c> GUIDs that serve as
/// placeholders. In a real evaluation, these IDs would correspond to documents
/// ingested into the target system before running <see cref="RetrievalEvaluationRunner"/>.
/// </para>
/// <para>
/// <c>Query 15</c> ("несуществующий термин абракадабра") has an empty
/// <c>ExpectedDocumentIds</c> set to test the no-hit scenario.
/// </para>
/// </remarks>
public static class BaselineGoldenQueries
{
    // Fixed DocumentIds used as placeholders for baseline evaluation.
    private static readonly Guid _docSensor =      new("a1b2c3d4-e5f6-7890-abcd-ef1234567891");
    private static readonly Guid _docMonitoring =   new("a1b2c3d4-e5f6-7890-abcd-ef1234567892");
    private static readonly Guid _docVoltage =      new("a1b2c3d4-e5f6-7890-abcd-ef1234567893");
    private static readonly Guid _docPower =        new("a1b2c3d4-e5f6-7890-abcd-ef1234567894");
    private static readonly Guid _docProtection =   new("a1b2c3d4-e5f6-7890-abcd-ef1234567895");

    /// <summary>
    /// Returns the baseline golden queries for evaluation.
    /// </summary>
    /// <returns>A list of <see cref="GoldenQuery"/> instances.</returns>
    public static IReadOnlyList<GoldenQuery> GetQueries()
    {
        return new List<GoldenQuery>
        {
            // === Russian (original) ===
            GoldenQuery.Create(
                "Измеренное значение Канал 2",
                new HashSet<Guid> { _docSensor }),

            GoldenQuery.Create(
                "давление в системе МПа",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "температура технологического процесса",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "аварийный сигнал превышение порога",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "канал связи штатный режим",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "датчик техническая документация",
                new HashSet<Guid> { _docSensor }),

            GoldenQuery.Create(
                "мониторинг данные мониторинга",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "напряжение фаза A B C",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "частота сети герц",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "активная мощность кВт коэффициент",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "реактивная мощность варcos фи",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "ток короткого замыкания номинальный",
                new HashSet<Guid> { _docProtection }),

            GoldenQuery.Create(
                "дифференциальная защита ток утечки",
                new HashSet<Guid> { _docProtection }),

            GoldenQuery.Create(
                "Канал 1 Канал 2 Канал 3",
                new HashSet<Guid> { _docSensor, _docMonitoring }),

            // === Negative query ===
            GoldenQuery.Create(
                "несуществующий термин абракадабра",
                new HashSet<Guid>()),

            // === Synonyms / paraphrases (Russian) ===
            GoldenQuery.Create(
                "измеренная температура",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "температура измерения",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "значение температуры",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "температура датчика",
                new HashSet<Guid> { _docSensor, _docMonitoring }),

            GoldenQuery.Create(
                "напряжение канала",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "коэффициент мощности",
                new HashSet<Guid> { _docPower }),

            // === English ===
            GoldenQuery.Create(
                "measured value channel",
                new HashSet<Guid> { _docSensor }),

            GoldenQuery.Create(
                "sensor temperature",
                new HashSet<Guid> { _docSensor, _docMonitoring }),

            GoldenQuery.Create(
                "power factor",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "signal frequency",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "measured temperature",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "temperature measurement",
                new HashSet<Guid> { _docMonitoring }),

            // === Mixed Russian + English ===
            GoldenQuery.Create(
                "значение Channel B",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "Power Factor для Канала 2",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "Modbus RTU",
                new HashSet<Guid> { _docSensor }),

            // === Technical identifiers ===
            GoldenQuery.Create(
                "Channel A",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "Channel B",
                new HashSet<Guid> { _docVoltage }),

            // === Units ===
            GoldenQuery.Create(
                "220 В",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "380 В",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "50 Гц",
                new HashSet<Guid> { _docPower }),

            // === Formulas ===
            GoldenQuery.Create(
                "формула мощности",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "расчет P через U I и cos phi",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "power calculation formula",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "cos phi значение",
                new HashSet<Guid> { _docPower }),

            // === Negative queries ===
            GoldenQuery.Create(
                "блокчейн распределенный реестр",
                new HashSet<Guid>()),

            GoldenQuery.Create(
                "нейросеть машинное обучение облачные вычисления",
                new HashSet<Guid>()),

            GoldenQuery.Create(
                "REST API микросервисы Kubernetes",
                new HashSet<Guid>()),

            // === Hard negatives ===
            GoldenQuery.Create(
                "напряжение 220 В мониторинг",
                new HashSet<Guid> { _docVoltage }),

            GoldenQuery.Create(
                "давление температура защита",
                new HashSet<Guid> { _docMonitoring }),

            GoldenQuery.Create(
                "ток утечки короткое замыкание",
                new HashSet<Guid> { _docProtection }),

            GoldenQuery.Create(
                "канал 1 данные",
                new HashSet<Guid> { _docSensor, _docMonitoring, _docVoltage }),

            // === Greek / Unicode ===
            GoldenQuery.Create(
                "φ ΔT U₁ U₂",
                new HashSet<Guid> { _docPower }),

            GoldenQuery.Create(
                "Modbus параметры",
                new HashSet<Guid> { _docSensor })
        };
    }
}
