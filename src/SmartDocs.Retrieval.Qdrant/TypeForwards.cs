using System.Runtime.CompilerServices;
using SmartDocs.Retrieval.VectorStores;

// Surface the Qdrant adapter through this Qdrant-scoped package while the type
// physically lives in the SmartDocs.Retrieval umbrella assembly (it depends on
// the umbrella's QdrantFilterCompiler, and the umbrella's DI extension depends
// on QdrantVectorStore — a physical move would form a reference cycle). The
// forward gives consumers of SmartDocs.Retrieval.Qdrant a real, resolvable
// QdrantVectorStore type and a genuine dependency edge onto the umbrella.
[assembly: TypeForwardedTo(typeof(QdrantVectorStore))]
