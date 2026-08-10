using Dapper;
using Microsoft.Data.Sqlite;

namespace SmolSearch.Storage;

public sealed class SmolSearchDatabase
{
    private readonly string _connectionString;

    public SmolSearchDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS documents
            (
                id           INTEGER PRIMARY KEY,
                url          TEXT NOT NULL UNIQUE,
                title        TEXT,
                content      TEXT NOT NULL,
                content_type TEXT NOT NULL,
                fetched_at   TEXT NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS document_fts USING fts5(
                title,
                content,
                url,
                content = 'documents',
                content_rowid = 'id'
            );

            CREATE TRIGGER IF NOT EXISTS documents_ai
            AFTER INSERT ON documents
            BEGIN
                INSERT INTO document_fts(rowid, title, content, url)
                VALUES (new.id, new.title, new.content, new.url);
            END;

            CREATE TRIGGER IF NOT EXISTS documents_ad
            AFTER DELETE ON documents
            BEGIN
                INSERT INTO document_fts(document_fts, rowid, title, content, url)
                VALUES ('delete', old.id, old.title, old.content, old.url);
            END;

            CREATE TRIGGER IF NOT EXISTS documents_au
            AFTER UPDATE ON documents
            BEGIN
                INSERT INTO document_fts(document_fts, rowid, title, content, url)
                VALUES ('delete', old.id, old.title, old.content, old.url);

                INSERT INTO document_fts(rowid, title, content, url)
                VALUES (new.id, new.title, new.content, new.url);
            END;
            """;

        await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                cancellationToken: cancellationToken));
    }
}