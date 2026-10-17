using System.Runtime.CompilerServices;
using SmartDocs.Retrieval.Hybrid;
using SmartDocs.Retrieval.VectorStores;

// Surface the Azure AI Search adapters through this Azure-scoped package. The
// types physically live in the SmartDocs.Retrieval umbrella assembly: the vector
// store depends on the umbrella's Azure filter compiler and the hybrid retriever
// on the umbrella's SearchDocumentRecord, and the umbrella's DI extension wires
// both — so they stay put and are re-exported here for a genuine dependency edge.
[assembly: TypeForwardedTo(typeof(AzureAiSearchVectorStore))]
[assembly: TypeForwardedTo(typeof(AzureAiSearchHybridRetriever))]
