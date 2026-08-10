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

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        const string sql =
            """
            SELECT
                d.url AS Url,
                d.title AS Title,
                snippet(document_fts, 1, '', '', '...', 24) AS Snippet,
                bm25(document_fts) AS Rank
            FROM document_fts
            JOIN documents d ON d.id = document_fts.rowid
            WHERE document_fts MATCH @Query
            ORDER BY Rank
            LIMIT @Limit;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        var results = await connection.QueryAsync<SearchResult>(
            sql,
            new
            {
                Query = query,
                Limit = limit
            });

        return results.AsList();
    }
}