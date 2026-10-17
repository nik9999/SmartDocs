# SmartDocs.Retrieval.Sqlite

Встраиваемое векторное хранилище и гибридный поисковый движок на базе **SQLite** для RAG-стека **SmartDocs**.

## Назначение

Пакет предназначен для автономной работы (desktop-приложения, edge-устройства, офлайн RAG, локальная разработка) без необходимости развертывания внешних баз данных (Qdrant, PostgreSQL, Elasticsearch).

## Основные компоненты

### 1. `SqliteVectorStore`
* Реализация `IVectorStore` на базе Microsoft.Data.Sqlite.
* Векторы хранятся в бинарных полях (`BLOB`).
* Точный поиск ближайших соседей (k-NN) использует **аппаратное SIMD-ускорение** (AVX2/AVX-512) через встроенную функцию `cosine_similarity(queryVector, vector)`.
* Поддерживает работу как с файловыми БД (`Data Source=vectors.db`), так и с in-memory базами данных (`Data Source=:memory:;Mode=Memory;Cache=Shared`).
* **Database Pre-Filtering**: Интеграция с `SqliteFilterCompiler` обеспечивает фильтрацию метаданных в движке SQLite до вычисления векторов, ускоряя поиск в разы на больших коллекциях.

### 2. `SqliteHybridRetriever`
* Реализует паттерн **«Single-store Hybrid»** («база данных сама является гибридом»).
* Объединяет:
  * Векторный семантический поиск по SIMD-функции `cosine_similarity`;
  * Разреженный полнотекстовый поиск SQLite **FTS5** (BM25) с токенизатором `unicode61` и префиксным поиском морфологических форм русских слов (`"слово"*`);
  * Автоматическую синхронизацию FTS5 через триггеры (`AFTER INSERT`, `AFTER DELETE`, `AFTER UPDATE`);
  * Слияние рангов **Reciprocal Rank Fusion (RRF $K=60$)** за **один SQL-запрос** через CTE (`WITH dense_ranks AS (...), sparse_ranks AS (...) SELECT chunk_id, SUM(1.0 / (60 + rank)) ...`).

### 3. `SqliteFilterCompiler`
* Транслирует деревья выражений `MetadataFilter` (`Expression<Func<DocumentMetadata, bool>>`) в параметризованные условия SQL над JSON-полями (`json_extract(metadata_json, '$.Department') = @flt_0`).
* Поддерживает операции `=`, `!=`, `>`, `<`, `>=`, `<=`, `IN (...)`, `LIKE ... ESCAPE '\'`, логические комбинации `AND`, `OR`, `NOT`, а также безопасный fallback на C#-делегат при сложных выражениях.

## Регистрация в DI

```csharp
// 1. Векторное хранилище SQLite
services.AddSmartDocsSqliteVectorStore(
    connectionString: "Data Source=smartdocs_vectors.db",
    collectionName: "default");

// 2. Гибридный ретривер SQLite
services.AddSmartDocsSqliteHybridRetriever(
    connectionString: "Data Source=smartdocs_vectors.db",
    collectionName: "default");
```
