namespace Rag.Infrastructure.Persistence;

/// <summary>
/// Database schema definition and versioning.
/// </summary>
public static class DatabaseSchema
{
    public const int CurrentVersion = 3;
    public const string VersionKey = "schema_version";

    public const string CreateSystemMetadataTable = """
        CREATE TABLE IF NOT EXISTS SystemMetadata (
            Key TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;

    public const string CreateDocumentsTable = """
        CREATE TABLE IF NOT EXISTS Documents (
            DocumentId TEXT NOT NULL PRIMARY KEY,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            Source TEXT NOT NULL
        );
        """;

    public const string CreateDocumentMetadataTable = """
        CREATE TABLE IF NOT EXISTS DocumentMetadata (
            DocumentId TEXT NOT NULL,
            Key TEXT NOT NULL,
            Value TEXT NOT NULL,

            PRIMARY KEY (DocumentId, Key),
            FOREIGN KEY (DocumentId)
                REFERENCES Documents(DocumentId)
                ON DELETE CASCADE
        );
        """;

    public const string CreateChunksTable = """
        CREATE TABLE IF NOT EXISTS Chunks (
            ChunkId TEXT NOT NULL PRIMARY KEY,
            DocumentId TEXT NOT NULL,
            Text TEXT NOT NULL,
            Position INTEGER NOT NULL,
            PageNumber INTEGER NULL,
            Section TEXT NULL,
            TokenCount INTEGER NULL,

            FOREIGN KEY (DocumentId)
                REFERENCES Documents(DocumentId)
                ON DELETE CASCADE,

            UNIQUE (DocumentId, Position)
        );
        """;

    public const string CreateChunksIndex = """
        CREATE INDEX IF NOT EXISTS IX_Chunks_DocumentId_Position
        ON Chunks (DocumentId, Position);
        """;

    public const string CreateEmbeddingsTable = """
        CREATE TABLE IF NOT EXISTS Embeddings (
            ChunkId TEXT NOT NULL PRIMARY KEY,
            Model TEXT NOT NULL,
            Dimensions INTEGER NOT NULL,
            Vector BLOB NOT NULL,

            FOREIGN KEY (ChunkId)
                REFERENCES Chunks(ChunkId)
                ON DELETE CASCADE
        );
        """;

    public const string CreateDocumentMetadataIndex = """
        CREATE INDEX IF NOT EXISTS IX_DocumentMetadata_DocumentId_Key
        ON DocumentMetadata (DocumentId, Key);
        """;

    // FTS5 virtual table for sparse/full-text search on chunks.
    // Standalone table — triggers populate it explicitly from Chunks.
    public const string CreateChunksFtsTable = """
        CREATE VIRTUAL TABLE IF NOT EXISTS ChunksFts
        USING fts5(
            Text,
            DocumentId UNINDEXED,
            ChunkId UNINDEXED
        );
        """;

    // Triggers to keep ChunksFts in sync with Chunks.
    public const string CreateChunksFtsTriggers = """
        /* AFTER INSERT: add new row to FTS index */
        CREATE TRIGGER IF NOT EXISTS ChunksFtsAfterInsert
        AFTER INSERT ON Chunks
        BEGIN
            INSERT INTO ChunksFts (Text, DocumentId, ChunkId)
            VALUES (NEW.Text, NEW.DocumentId, NEW.ChunkId);
        END;

        /* AFTER DELETE: remove from FTS index */
        CREATE TRIGGER IF NOT EXISTS ChunksFtsAfterDelete
        AFTER DELETE ON Chunks
        BEGIN
            DELETE FROM ChunksFts
            WHERE DocumentId = OLD.DocumentId AND ChunkId = OLD.ChunkId;
        END;

        /* AFTER UPDATE: delete old, insert new */
        CREATE TRIGGER IF NOT EXISTS ChunksFtsAfterUpdate
        AFTER UPDATE ON Chunks
        BEGIN
            DELETE FROM ChunksFts
            WHERE DocumentId = OLD.DocumentId AND ChunkId = OLD.ChunkId;
            INSERT INTO ChunksFts (Text, DocumentId, ChunkId)
            VALUES (NEW.Text, NEW.DocumentId, NEW.ChunkId);
        END;
        """;
}
