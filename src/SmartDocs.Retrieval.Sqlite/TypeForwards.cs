using System.Runtime.CompilerServices;
using SmartDocs.Retrieval.Filtering;
using SmartDocs.Retrieval.Hybrid;
using SmartDocs.Retrieval.VectorStores;

// Surface the Sqlite vector store, hybrid retriever, and filter compiler (and their options) through this
// Sqlite-scoped package. The types physically live in the SmartDocs.Retrieval
// umbrella assembly; forwarding keeps a single definition and a genuine
// dependency edge onto the umbrella.
[assembly: TypeForwardedTo(typeof(SqliteVectorStore))]
[assembly: TypeForwardedTo(typeof(SqliteVectorStoreOptions))]
[assembly: TypeForwardedTo(typeof(SqliteHybridRetriever))]
[assembly: TypeForwardedTo(typeof(SqliteHybridOptions))]
[assembly: TypeForwardedTo(typeof(SqliteFilterCompiler))]
[assembly: TypeForwardedTo(typeof(SqliteCompiledFilter))]


