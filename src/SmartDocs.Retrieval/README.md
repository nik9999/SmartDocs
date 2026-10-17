# SmartDocs.Retrieval

Поисковый движок для RAG-стека **SmartDocs**. Включает плотный (dense), разреженный (sparse) и гибридный поиск, продвинутые декораторы запросов и адаптеры хранилищ.

## Возможности

### 1. Стратегии поиска и извлечения
* **Плотный поиск (Dense Retrieval)**: Семантический поиск по косинусному расстоянию векторов.
* **Гибридный поиск (Hybrid Retrieval)**:
  * Объединение векторного ранжирования и полнотекстового поиска (BM25) через **Reciprocal Rank Fusion (RRF $K=60$)**.
  * **Паттерн «Database is the hybrid»**: выполнение слияния векторов и текста внутри единого SQL-запроса к базе данных без промежуточных пересылок данных.
* **Продвинутые декораторы (Enhancement Decorators)** (все промпты адаптированы для русского языка и GigaChat):
  * **HyDE (`HydeRetriever`)**: Генерация гипотетического документа через LLM для поиска по его вектору.
  * **RAG-Fusion (`RagFusionRetriever`)**: Развертывание пользовательского запроса в несколько поисковых вариантов с последующим слиянием результатов через RRF.
  * **CRAG (`CragRetriever`)**: Корректирующий RAG — оценка релевантности найденных фрагментов и опциональный поиск во внешних источниках.
  * **Step-Back (`StepBackRetriever`)**: Формирование более общего («шаг назад») вопроса для извлечения высокоуровневых принципов.
* **Графовый и структурный поиск (`GraphRagPipeline`, `StructuralRetriever`, `LlmSectionRouter`)**:
  * Маршрутизация запросов по оглавлению и разделам документов.
  * Извлечение сущностей и связей для поиска по графу знаний.

### 2. Хранилища и адаптеры
* **`InMemoryVectorStore`**: Быстрое in-memory векторное хранилище для тестов и разработки.
* **`SqliteVectorStore`**: Встраиваемое векторное хранилище на базе SQLite с аппаратным SIMD-ускорением (`CosineKernel`).
* **`SqliteHybridRetriever`**: Однобазовый гибридный ретривер (FTS5 + SIMD Cosine + RRF за 1 SQL round-trip).
* Отдельные пакеты расширений для внешних систем:
  * [`SmartDocs.Retrieval.Sqlite`](../SmartDocs.Retrieval.Sqlite/) — автономный пакет с forwarded types для SQLite.
  * [`SmartDocs.Retrieval.Qdrant`](../SmartDocs.Retrieval.Qdrant/) — векторное хранилище на базе Qdrant (gRPC).
  * [`SmartDocs.Retrieval.Postgres`](../SmartDocs.Retrieval.Postgres/) — гибридное хранилище PostgreSQL + `pgvector`.
  * [`SmartDocs.Retrieval.AzureSearch`](../SmartDocs.Retrieval.AzureSearch/) — интеграция с Azure AI Search.

### 3. Компиляторы фильтров метаданных (`SmartDocs.Retrieval.Filtering`)
* **`SqliteFilterCompiler`**: Трансляция `MetadataFilter` в SQL `json_extract(...)` для фильтрации на стороне базы данных до вычисления векторов.
* **`QdrantFilterCompiler`**: Трансляция в Protobuf `Filter` для gRPC API Qdrant.
* **`AzureSearchFilterCompiler`**: Трансляция в OData `$filter`.
