using Dapper;
using Microsoft.Data.Sqlite;
using SmolSearch.Core;

namespace SmolSearch.Storage;

public sealed class DocumentStore
{
    private readonly string _connectionString;

    internal DocumentStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public DocumentStore CreateDocumentStore()
    {
        return new DocumentStore(_connectionString);
    }

    public async Task UpsertAsync(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        const string sql =
            """
            INSERT INTO documents
            (
                url,
                title,
                content,
                content_type,
                fetched_at
            )
            VALUES
            (
                @Url,
                @Title,
                @Content,
                @ContentType,
                @FetchedAt
            )
            ON CONFLICT(url) DO UPDATE SET
                title = excluded.title,
                content = excluded.content,
                content_type = excluded.content_type,
                fetched_at = excluded.fetched_at;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        await connection.ExecuteAsync(sql, new
        {
            Url = document.Url.ToString(),
            document.Title,
            document.Content,
            document.ContentType,
            document.FetchedAt
        });
    }
}