# SmartDocs.Retrieval.Postgres

Гибридный поисковый адаптер на базе **PostgreSQL** и расширения **`pgvector`** для RAG-стека **SmartDocs**.

## Возможности

* **`PostgresHybridRetriever`**:
  * Реализует паттерн «Single-store Hybrid» на базе PostgreSQL.
  * Плотный векторный поиск по косинусному расстоянию (`<=>`) с использованием расширения `pgvector` и индексов HNSW / IVFFlat.
  * Разреженный полнотекстовый поиск с ранжированием `ts_rank_cd` по полям `tsvector`.
  * Слияние рангов **Reciprocal Rank Fusion (RRF $K=60$)** напрямую внутри SQL через Common Table Expressions (CTE) за один сетевой round-trip к базе данных.
  * Автоматическая инициализация схемы таблиц, расширений и индексов (`EnsureSchemaExistsAsync`).

## Регистрация в DI

```csharp
services.AddSmartDocsHybridRetriever("postgres", new HybridRetrieverOptions
{
    PostgresConnectionString = "Host=localhost;Database=smartdocs;Username=postgres;Password=secret"
});
```
