using System.Collections.Generic;
using SmartDocs.Samples.StrategyTest.Cli.Models;

namespace SmartDocs.Samples.StrategyTest.Cli.Benchmarking;

/// <summary>
/// Benchmark dataset tailored to the technical passport document (MDS AI-8UI / MDS AI-8UI/D).
/// </summary>
public static class BenchmarkDataset
{
    public static IReadOnlyList<BenchmarkQuery> GetPassportQueries() =>
    [
        new BenchmarkQuery(
            Id: "Q1_MODBUS_DEFAULT",
            Question: "Каковы заводские настройки протокола Modbus RTU по умолчанию (сетевой адрес, скорость передачи данных, чётность и сторожевой таймер)?",
            ExpectedFacts:
            [
                "01",
                "9600",
                "без контроля по чётности",
                "сторожевого"
            ],
            RelevantKeywords:
            [
                "Modbus RTU", "01", "9600", "чётности", "INIT", "тайм-аут"
            ],
            TargetSection: "6. Подготовка к работе и порядок работы"),

        new BenchmarkQuery(
            Id: "Q2_RS485_PINS",
            Question: "К каким контактам клеммного соединителя X2 подключаются линии А и В интерфейса RS-485?",
            ExpectedFacts:
            [
                "Х2:4",
                "Х2:5",
                "витой парой"
            ],
            RelevantKeywords:
            [
                "RS-485", "Х2:4", "Х2:5", "клеммам А", "В", "Х2:7", "Х2:8"
            ],
            TargetSection: "3.6.2 Подключение цепей интерфейса RS-485"),

        new BenchmarkQuery(
            Id: "Q3_POWER_PINS",
            Question: "К каким клеммам разъема X2 подключается источник питания постоянного тока и каков допустимый уровень пульсаций?",
            ExpectedFacts:
            [
                "X2:1",
                "X2:2",
                "1,5 В"
            ],
            RelevantKeywords:
            [
                "питания", "X2:1", "X2:2", "пульсаций", "постоянного напряжения", "полярности"
            ],
            TargetSection: "3.6.1 Подключение цепей питания"),

        new BenchmarkQuery(
            Id: "Q4_INIT_MODE",
            Question: "Как перевести модуль MDS AI-8UI в технологический режим конфигурации INIT и какие параметры при этом фиксируются?",
            ExpectedFacts:
            [
                "перемычка",
                "верхней плате",
                "9600",
                "01"
            ],
            RelevantKeywords:
            [
                "INIT", "перемычка", "джампер", "верхней плате", "адрес 01", "9600"
            ],
            TargetSection: "6. Подготовка к работе и технологический режим"),

        new BenchmarkQuery(
            Id: "Q5_INPUT_SIGNALS",
            Question: "Какие типы входных аналоговых сигналов и датчиков поддерживают модули MDS AI-8UI?",
            ExpectedFacts:
            [
                "термопар",
                "термопреобразователей",
                "тока",
                "напряжения"
            ],
            RelevantKeywords:
            [
                "входных", "термопар", "4-20 мА", "сопротивления", "напряжения", "8 каналов"
            ],
            TargetSection: "3.1 Входные сигналы и каналы"),

        new BenchmarkQuery(
            Id: "Q6_ACCURACY_LIMITS",
            Question: "Каков предел допускаемой основной приведенной погрешности измерения модуля и есть ли гальваническая изоляция?",
            ExpectedFacts:
            [
                "погрешност",
                "гальваническ"
            ],
            RelevantKeywords:
            [
                "погрешности", "гальваническая", "развязка", "приведенной", "каналами"
            ],
            TargetSection: "3.2 Метрологические характеристики"),

        new BenchmarkQuery(
            Id: "Q7_PACKAGE_SET",
            Question: "Что входит в комплектность поставки модуля MDS AI-8UI?",
            ExpectedFacts:
            [
                "модуль",
                "паспорт",
                "тара"
            ],
            RelevantKeywords:
            [
                "комплектность", "паспорт", "модуль ввода", "тара", "упаковка"
            ],
            TargetSection: "4. Комплектность"),

        new BenchmarkQuery(
            Id: "Q8_MANUFACTURER",
            Question: "Кто является изготовителем модуля, где расположено предприятие и каков гарантийный срок службы изделия?",
            ExpectedFacts:
            [
                "КонтрАвт",
                "Нижний Новгород",
                "24"
            ],
            RelevantKeywords:
            [
                "КонтрАвт", "Нижний Новгород", "гарантийный", "24", "изготовителя"
            ],
            TargetSection: "9. Гарантии и 10. Адрес изготовителя")
    ];
}

