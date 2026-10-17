# SmartDocs.Core

Базовые абстракции, доменная модель и вычислительные примитивы для RAG-стека **SmartDocs**.

## Назначение

`SmartDocs.Core` — фундаментальная библиотека, от которой зависят все остальные пакеты решения. Она определяет чистую доменную модель без тяжелых внешних зависимостей.

## Основные компоненты

### 1. Доменная модель (`SmartDocs.Core.Documents`)
* **`Document`** — исходный документ со структурированными метаданными и сырым содержимым.
* **`DocumentChunk`** — фрагмент документа с позиционными смещениями в символах (`StartCharOffset`, `EndCharOffset`) и метаданными.
* **`EmbeddedChunk`** — чанк с вычисленным плотным вектором (`ReadOnlyMemory<float>`) и идентификатором модели эмбеддингов.
* **`DocumentMetadata`** — строго типизированные метаданные документа (Id, Silo, Department, Office, ConfidentialityLevel, DocumentType, FiscalYear, Author, LastModified, Title).
* **`RetrievalResult`** — результат поиска, связывающий `DocumentChunk` со скором релевантности (`double Score`).
* **`ExtractedTable`** — структурированное табличное представление.

### 2. Порты и абстракции (`SmartDocs.Core.Abstractions`)
* **`IRetriever`** — единый контракт поискового движка (`RetrieveAsync`).
* **`IVectorStore`** — контракт векторного хранилища (`SearchAsync`, `UpsertAsync`, `DeleteAsync`, `CountAsync`).
* **`IEmbeddingService`** — сервис вычисления плотных эмбеддингов для чанков и поисковых запросов.
* **`IChunker`** — контракт стратегий разбиения текста.
* **`IDocumentParser`** — парсер документов различных форматов.
* **`IImageCaptioner`** — мультимодальный генератор текстовых описаний для изображений и графиков.

### 3. Фильтрация метаданных (`SmartDocs.Core.Filtering`)
* **`MetadataFilter`** — легковесная типизированная обертка над C# выражениями `Expression<Func<DocumentMetadata, bool>>`. Позволяет составлять предикаты в коде (`MetadataFilter.Where(m => m.Department == "HR")`) и транслировать их через специализированные компиляторы в нативные языки хранилищ (SQLite SQL, Qdrant gRPC Protobuf, Azure OData).

### 4. Аппаратное SIMD-ускорение (`SmartDocs.Core.Numerics`)
* **`CosineKernel`** — высокопроизводительное вычисление косинусного сходства (Cosine Similarity) между векторами `ReadOnlySpan<float>` с использованием аппаратных инструкций SIMD (`Vector<float>`, AVX2/AVX-512/ARM NEON) и развертыванием циклов.

### 5. Конфигурация и DI (`SmartDocs.Core.Configuration`, `SmartDocs.Core.DependencyInjection`)
* **`SmartDocsOptions`** — единая точка связывания секции `SmartDocs` из `appsettings.json`.
* **`services.AddSmartDocsCore(...)`** — регистрация основных служб в DI-контейнере.
