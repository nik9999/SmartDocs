# SmartDocs.Retrieval.AzureSearch

Адаптеры **Azure AI Search** для RAG-стека **SmartDocs**.

## Возможности

* **`AzureAiSearchVectorStore`**: Реализация `IVectorStore` поверх библиотеки `Azure.Search.Documents` с поддержкой аутентификации по ключу (`AzureKeyCredential`) или Entra ID (`TokenCredential`).
* **`AzureAiSearchHybridRetriever`**: Гибридный векторный и ключевой поиск со встроенным слиянием RRF в Azure AI Search и поддержкой семантического ранжирования (Semantic Ranker).
* **`AzureSearchFilterCompiler`**: Трансляция деревьев `MetadataFilter` в синтаксис OData `$filter` для выполнения пре-фильтрации на стороне облачного поискового индекса с автоматическим fallback на post-фильтрацию при сложных выражениях.

## Регистрация в DI

```csharp
services.AddSmartDocsAzureAiSearchVectorStore(
    endpoint: new Uri("https://my-search.search.windows.net"),
    indexName: "smartdocs-index",
    vectorSize: 1024,
    credential: new AzureKeyCredential("<API_KEY>"));
```
