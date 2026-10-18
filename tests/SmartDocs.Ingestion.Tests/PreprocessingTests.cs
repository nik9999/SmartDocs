using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Preprocessing;
using Xunit;

namespace SmartDocs.Ingestion.Tests;

public class PreprocessingTests
{
    [Fact]
    public void Clean_DehyphenatesRussianAndEnglishWords_Correctly()
    {
        var cleaner = new DocumentPreprocessor();

        var input =
            "Входной аналоговый преобразо-\n" +
            "ватель сигналов МВА8 предназначен для изме-\n" +
            "рения температуры.\n\n" +
            "The system uses multi-\n" +
            "modal retrieval for complex technical manuals.";

        var cleaned = cleaner.Clean(input);

        Assert.Contains("преобразователь", cleaned);
        Assert.Contains("измерения", cleaned);
        Assert.Contains("multimodal", cleaned);
        Assert.DoesNotContain("преобразо-", cleaned);
        Assert.DoesNotContain("изме-", cleaned);
    }

    [Fact]
    public void Clean_PreservesCompoundCodesWithHyphens()
    {
        var cleaner = new DocumentPreprocessor();

        var input =
            "Интерфейс RS-\n" +
            "485 и прибор МВА8-\n" +
            "24В должны быть заземлены.";

        var cleaned = cleaner.Clean(input);

        Assert.Contains("RS-485", cleaned);
        Assert.Contains("МВА8-24В", cleaned);
    }

    [Fact]
    public void Clean_RemovesSoftHyphenAndNormalizesWhitespace()
    {
        var cleaner = new DocumentPreprocessor();

        // Contains soft hyphen \u00AD, non-breaking spaces \u00A0, and multiple spaces
        var input = "Слово\u00ADперенос   с\u00A0неразрывным   пробелом   и\u200Bzero-width.";
        var cleaned = cleaner.Clean(input);

        Assert.Equal("Словоперенос с неразрывным пробелом иzero-width.", cleaned);
    }

    [Fact]
    public void Clean_CollapsesDotLeadersInTOC()
    {
        var cleaner = new DocumentPreprocessor();

        var input =
            "1. Введение ..................................................... 3\n" +
            "2. Технические характеристики . . . . . . . . . . . . . . . . . 15";

        var cleaned = cleaner.Clean(input);

        Assert.Contains("1. Введение 3", cleaned);
        Assert.Contains("2. Технические характеристики 15", cleaned);
        Assert.DoesNotContain(".....", cleaned);
    }

    [Fact]
    public void Preprocess_FiltersEmptyAndBoilerplatePages()
    {
        var preprocessor = new DocumentPreprocessor(new DocumentPreprocessorOptions
        {
            MinInformativeCharactersPerPage = 30
        });

        var content =
            "## Page 1\n" +
            "Руководство по эксплуатации прибора МВА8. Назначение и основные функции измерителя.\n\n" +
            "## Page 2\n" +
            "Для заметок.\n\n" +
            "## Page 3\n" +
            "3\n\n" +
            "## Page 4\n" +
            "Технические характеристики прибора: питание от сети 220 В, 8 аналоговых входов.";

        var doc = new Document(
            new DocumentMetadata("doc-1", "silo", "dept", "office", "internal", "pdf", 2026, "author", DateOnly.MinValue, "title"),
            content,
            "path.pdf");

        var result = preprocessor.Preprocess(doc);

        Assert.Contains("## Page 1", result.Content);
        Assert.Contains("## Page 4", result.Content);
        Assert.DoesNotContain("## Page 2", result.Content);
        Assert.DoesNotContain("## Page 3", result.Content);
        Assert.DoesNotContain("Для заметок", result.Content);
    }

    [Fact]
    public void Preprocess_StripsRepeatingHeadersAndFooters()
    {
        var preprocessor = new DocumentPreprocessor(new DocumentPreprocessorOptions
        {
            StripRepeatingHeadersAndFooters = true,
            MinInformativeCharactersPerPage = 20
        });

        var content =
            "## Page 1\n" +
            "Модуль ввода аналоговый МВА8\n" +
            "Содержание раздела один: подробное описание технических параметров и монтажа устройства.\n" +
            "Стр. 1 из 3\n\n" +
            "## Page 2\n" +
            "Модуль ввода аналоговый МВА8\n" +
            "Содержание раздела два: правила подключения датчиков к клеммной колодке прибора.\n" +
            "Стр. 2 из 3\n\n" +
            "## Page 3\n" +
            "Модуль ввода аналоговый МВА8\n" +
            "Содержание раздела три: конфигурирование сетевых параметров протокола Modbus.\n" +
            "Стр. 3 из 3";

        var doc = new Document(
            new DocumentMetadata("doc-2", "silo", "dept", "office", "internal", "pdf", 2026, "author", DateOnly.MinValue, "title"),
            content,
            "path.pdf");

        var result = preprocessor.Preprocess(doc);

        // Header "Модуль ввода аналоговый МВА8" was present on all 3 pages and should be stripped from body
        Assert.DoesNotContain("Модуль ввода аналоговый МВА8", result.Content);
        Assert.DoesNotContain("Стр. 1 из 3", result.Content);
        Assert.Contains("Содержание раздела один", result.Content);
        Assert.Contains("Содержание раздела два", result.Content);
        Assert.Contains("Содержание раздела три", result.Content);
    }

    [Fact]
    public async Task PreprocessingChunker_CleansDocumentBeforeInnerChunking()
    {
        var rawContent =
            "Входной аналоговый преобразо-\n" +
            "ватель сигналов МВА8 предназначен для изме-\n" +
            "рения температуры датчиками.";

        var doc = new Document(
            new DocumentMetadata("doc-3", "silo", "dept", "office", "internal", "pdf", 2026, "author", DateOnly.MinValue, "title"),
            rawContent,
            "path.pdf");

        var inner = new RecursiveCharacterChunker(maxChunkSize: 500);
        var chunker = new PreprocessingChunker(inner);

        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in chunker.ChunkAsync(doc))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.Contains("преобразователь", chunks[0].Text);
        Assert.Contains("измерения", chunks[0].Text);
        Assert.DoesNotContain("преобразо-", chunks[0].Text);
    }

    [Fact]
    public void Clean_RemovesTrademarkAndCopyrightSymbols()
    {
        var cleaner = new DocumentPreprocessor();

        var input = "НПФ «КонтрАвт»® производит приборы под маркой DataTrack™ © 2026";
        var cleaned = cleaner.Clean(input);

        Assert.Equal("НПФ «КонтрАвт» производит приборы под маркой DataTrack 2026", cleaned);
    }

    [Fact]
    public void Preprocess_CleansMetadataTitleAndAuthor()
    {
        var preprocessor = new DocumentPreprocessor();

        var doc = new Document(
            new DocumentMetadata("doc-tm", "silo", "dept", "office", "internal", "pdf", 2026, "КонтрАвт®", DateOnly.MinValue, "MasterSCADA™ Guide ©"),
            "Содержимое документа с символом ® и знаком копирайта ©.",
            "path.pdf");

        var result = preprocessor.Preprocess(doc);

        Assert.Equal("КонтрАвт", result.Metadata.Author);
        Assert.Equal("MasterSCADA Guide", result.Metadata.Title);
        Assert.DoesNotContain("®", result.Content);
        Assert.DoesNotContain("©", result.Content);
    }
}

