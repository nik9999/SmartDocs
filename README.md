# SmartDocs

Промышленный стек Retrieval-Augmented Generation (RAG) на **C# 14** и **.NET 10**.

## Обзор архитектуры

SmartDocs — высокопроизводительная модульная RAG-система enterprise-уровня, оптимизированная для обработки сложных корпоративных документов (PDF, Word DOCX, Excel XLSX, Markdown, C# исходный код), включая таблицы, графики, чертежи и двуязычные тексты (русский + английский).

### Ключевые архитектурные уровни:

1. **Препроцессинг и очистка текста ([`SmartDocs.Ingestion.Preprocessing`](src/SmartDocs.Ingestion/))**:
   - Автоматическая дедефисация разорванных слов на стыках строк (например, `преобразова-\nтель` → `преобразователь`).
   - Фильтрация пустых страниц и страниц с шаблонами («Для заметок», «Лист регистрации изменений»).
   - Очистка бегущих колонтитулов и номеров страниц.
   - Схлопывание точечных отточий в оглавлениях и спецификациях.
2. **Стратегии чанкинга ([`SmartDocs.Ingestion`](src/SmartDocs.Ingestion/))**:
   - `RecursiveCharacterChunker` — рекурсивный Markdown-aware чанкинг по заголовкам, параграфам и предложениям.
   - `SentenceChunker` — разбиение текста по границам предложений с поддержкой многоязычного Unicode (`\p{Lu}`).
   - `CodeFileChunker` — синтаксический анализ C# на базе Roslyn с сохранением пространств имен и контекста классов.
   - `SemanticChunker` — смысловое разделение по точкам переключения темы с вычислением косинусного сходства векторов.
   - `ContextualChunker` — обогащение чанков контекстом документа (Anthropic Contextual Retrieval).
   - `MultimodalChunker` — связывание текстовых чанков с описаниями изображений (`ImageCaptioner`) и таблицами.
3. **Модели эмбеддингов**:
   - Автономный in-process инференс модели **`BGE-M3`** (1024 измерений, контекст до 8192 токенов, многоязычная поддержка RU/EN) через **ONNX Runtime** и **SentencePieceTokenizer**.
   - Облачные эмбеддинги **Sber GigaChat** (`Embeddings`, `EmbeddingsGigaR`) через [`SmartDocs.GigaChat`](src/SmartDocs.GigaChat/).
4. **Хранилища векторов и гибридный поиск ([`SmartDocs.Retrieval`](src/SmartDocs.Retrieval/))**:
   - **Встраиваемый SQLite ([`SmartDocs.Retrieval.Sqlite`](src/SmartDocs.Retrieval.Sqlite/))**:
     - `SqliteVectorStore` — векторное хранилище с аппаратным SIMD-ускорением вычисления косинусного сходства (`CosineKernel`).
     - `SqliteHybridRetriever` — гибридный поиск, объединяющий SIMD-косинус и полнотекстовый поиск SQLite FTS5 (BM25) со слиянием рангов Reciprocal Rank Fusion (RRF $K=60$) за один SQL-запрос.
     - `SqliteFilterCompiler` — компиляция предикатов `MetadataFilter` в SQL-условия над `json_extract` для true database pre-filtering.
   - **Qdrant ([`SmartDocs.Retrieval.Qdrant`](src/SmartDocs.Retrieval.Qdrant/))**: gRPC-адаптер и компилятор фильтров в Protobuf.
   - **PostgreSQL ([`SmartDocs.Retrieval.Postgres`](src/SmartDocs.Retrieval.Postgres/))**: гибридный поиск `pgvector` + `tsvector` с RRF в SQL.
   - **Azure AI Search ([`SmartDocs.Retrieval.AzureSearch`](src/SmartDocs.Retrieval.AzureSearch/))**: OData-фильтрация и гибридный поиск.
   - **Продвинутые стратегии извлечения**: HyDE, RAG-Fusion, CRAG, Step-Back, GraphRAG и структурная маршрутизация.
5. **Реранкинг (Reranking) ([`SmartDocs.Reranking`](src/SmartDocs.Reranking/))**:
   - Паттерн *«Retrieve broadly, rerank precisely»* через `RerankingMiddleware` с поддержкой отсечения по порогу `MinScore`.
   - **Локальный Cross-Encoder ([`SmartDocs.Reranking.Onnx`](src/SmartDocs.Reranking.Onnx/))**: модель **`bge-reranker-v2-m3`** на базе ONNX Runtime.
   - **LLM-as-a-judge (`LlmRerank`)**: оценка релевантности через GigaChat на русском языке.
   - **Cohere Rerank (`CohereReranker`)**: облачный API Cohere.
   - **NoOpReranker**: контрольный реранкер для A/B-тестирования.

---

## Структура репозитория

```
SmartDocs/
├── data/                                 # Тестовые корпоративные документы (DOCX, XLSX, PDF, MD)
├── models/                               # Локальные веса ONNX моделей (исключены из Git)
│   ├── bge-m3/                           # ONNX модель BGE-M3 (веса, токенизатор, BPE)
│   ├── bge-reranker-v2-m3/               # ONNX модель реранкера
│   └── download_models.py                # Скрипт автоматической загрузки моделей
├── src/
│   ├── SmartDocs.Core/                   # Ядро: абстракции, доменная модель, SIMD CosineKernel, DI
│   ├── SmartDocs.Ingestion/              # Препроцессинг текста, чанкеры, пайплайн векторизации, PDF/DOCX/XLSX парсеры
│   ├── SmartDocs.GigaChat/               # Провайдер Microsoft.Extensions.AI для Sber GigaChat API
│   ├── SmartDocs.Retrieval/              # Движок поиска: Dense, Sparse, Hybrid, HyDE, CRAG, GraphRAG, InMemory
│   ├── SmartDocs.Retrieval.Sqlite/       # Embedded SQLite векторное хранилище и FTS5 гибридный ретривер
│   ├── SmartDocs.Retrieval.Qdrant/       # Qdrant векторный адаптер и gRPC компилятор фильтров
│   ├── SmartDocs.Retrieval.Postgres/     # PostgreSQL + pgvector гибридный ретривер
│   ├── SmartDocs.Retrieval.AzureSearch/  # Azure AI Search векторный и гибридный адаптер
│   ├── SmartDocs.Reranking/              # Слой реранкинга: IReranker, RerankingMiddleware, LlmRerank, Cohere
│   └── SmartDocs.Reranking.Onnx/         # Локальный ONNX кросс-энкодер bge-reranker-v2-m3
├── samples/
│   ├── ChunkingPlayground/               # Консольная демонстрация стратегий чанкинга
│   └── ChunkingStrategy/                 # Комплексный мультимодальный RAG-пайплайн для сложных документов
├── tests/
│   ├── SmartDocs.GigaChat.Tests/         # Модульные тесты интеграции GigaChat
│   └── SmartDocs.Ingestion.Tests/        # Модульные тесты препроцессинга, SQLite, реранкинга и фильтров
├── SmartDocs.slnx                        # Файл решения .NET
└── Directory.Packages.props              # Централизованное управление версиями пакетов (CPM)
```

---

## Быстрый старт

### 1. Требования
* **.NET 10 SDK** (версия `10.0.100` или новее)
* **Python 3** (для скрипта загрузки моделей)

### 2. Загрузка моделей
Перед первым запуском скачайте локальные ONNX-веса для `bge-m3` и `bge-reranker-v2-m3`:

```bash
python models/download_models.py
```

### 3. Сборка решения и запуск тестов
```bash
dotnet build SmartDocs.slnx
dotnet test SmartDocs.slnx
```

### 4. Запуск сэмплов

#### Chunking Playground (Сравнение стратегий)
```bash
dotnet run --project samples/ChunkingPlayground
```

#### Chunking Strategy (Мультимодальный корпоративный RAG-пайплайн)
```bash
dotnet run --project samples/ChunkingStrategy
```

---

## Интеграция со Сбер GigaChat (`SmartDocs.GigaChat`)

Пакет [`SmartDocs.GigaChat`](src/SmartDocs.GigaChat/) реализует абстракции **Microsoft.Extensions.AI** для экосистемы **Sber GigaChat REST API**:

* `GigaChatChatClient : IChatClient` — синхронная генерация (`GetResponseAsync`) и потоковая передача токенов SSE (`GetStreamingResponseAsync`).
* `GigaChatEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>` — векторизация текста через модели `Embeddings`, `Embeddings-2`, `EmbeddingsGigaR`.
* **OAuth 2.0**: Автоматическое кэширование и бесшовное продление токена доступа с уникальными `RqUID`.
* **Поддержка сертификатов Минцифры**: Опция `IgnoreSslValidation = true` исключает сбои валидации цепочки сертификатов Russian Trusted Root CA.
* **Function Calling**: Двусторонняя трансляция между `AIFunction` и `functions` в GigaChat API.

---

## Конфигурация (`appsettings.json`)

```json
{
  "SmartDocs": {
    "Embedding": {
      "EmbeddingModel": "bge-m3",
      "Dimensions": 1024,
      "ModelPath": "models/bge-m3/model.onnx"
    },
    "Llm": {
      "Provider": "GigaChat",
      "AuthorizationKey": "<Base64_Client_ID:Client_Secret>",
      "ChatModel": "GigaChat-Pro",
      "GigaChatScope": "GIGACHAT_API_PERS"
    },
    "Ingestion": {
      "MaxChunkSize": 800,
      "UseContextualRetrieval": false
    },
    "Reranking": {
      "Mode": "onnx",
      "CandidateCount": 20,
      "MinScore": 0.0
    }
  }
}
```

---

## Лицензия

MIT License
