# SmartDocs.Retrieval.Qdrant

Адаптер векторной базы данных **Qdrant** для RAG-стека **SmartDocs**.

## Возможности

* **`QdrantVectorStore`**: Реализация `IVectorStore` поверх официального клиента `Qdrant.Client` по протоколу gRPC.
* **Скалярное квантование**: Опциональная поддержка int8 скалярного квантования при создании коллекций (`useScalarQuantization: true`) для снижения объема памяти.
* **`QdrantFilterCompiler`**: Двусторонний компилятор предикатов `MetadataFilter` (`Expression<Func<DocumentMetadata, bool>>`):
  * в strongly-typed Protobuf `Filter` для gRPC API;
  * в JSON-представление Qdrant Filter.
* **`QdrantHybridRetriever`**: Гибридный поиск по плотным и разреженным (sparse) векторам со слиянием через `Fusion.Rrf` на стороне сервера Qdrant.

## Регистрация в DI

```csharp
services.AddSmartDocsQdrantVectorStore(
    collectionName: "knowledge-base",
    vectorSize: 1024,
    host: "localhost",
    port: 6334);
```
