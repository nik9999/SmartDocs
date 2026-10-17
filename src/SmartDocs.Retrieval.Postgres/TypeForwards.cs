using System.Runtime.CompilerServices;
using SmartDocs.Retrieval.Hybrid;

// Surface the Postgres hybrid adapter (and its options) through this
// Postgres-scoped package. The type physically lives in the SmartDocs.Retrieval
// umbrella assembly, where the umbrella's AddSmartDocsHybridRetriever DI
// extension references it; forwarding keeps a single definition and a genuine
// dependency edge onto the umbrella.
[assembly: TypeForwardedTo(typeof(PostgresHybridRetriever))]
[assembly: TypeForwardedTo(typeof(PostgresHybridOptions))]
