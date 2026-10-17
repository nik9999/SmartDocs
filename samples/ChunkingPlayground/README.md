# Chunking Playground
 
Interactive console sample demonstrating and comparing text chunking strategies in **SmartDocs**:

* **`FixedSizeChunker(200, 50)`** — разбиение фиксированного размера с перекрытием (overlap).
* **`SentenceChunker(2)`** — разбиение по предложениям с поддержкой Unicode/кириллицы (`[\p{Lu}\d]`).
* **`RecursiveCharacterChunker(200)`** — иерархический Markdown-aware чанкинг.
* **`CodeFileChunker`** — синтаксическое разбиение C# кода на базе Roslyn (классы, методы, usings).
* **`SemanticChunker(0.75)`** — семантическое разбиение на базе локальной ONNX модели **`BGE-M3`** (вычисление косинусного сходства между предложениями).
* **`ContextualChunker`** — контекстуализация Anthropic Contextual Retrieval (требует доступного LLM ChatClient).

## Запуск

```bash
dotnet run --project samples/ChunkingPlayground
```
