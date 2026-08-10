using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace SmolSearch.Storage;

public sealed class CrawlFrontierStore
{
    private readonly string _connectionString;

    internal CrawlFrontierStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task AddAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        const string sql =
            """
            INSERT OR IGNORE INTO crawl_frontier
            (
                url,
                discovered_at
            )
            VALUES
            (
                @Url,
                @DiscoveredAt
            );
            """;

        await using var connection = new SqliteConnection(_connectionString);

        await connection.ExecuteAsync(
            sql,
            new
            {
                Url = uri.AbsoluteUri,
                DiscoveredAt = DateTimeOffset.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
            });
    }

    public async Task<IReadOnlyList<Uri>> GetPendingAsync(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        const string sql =
            """
            SELECT url
            FROM crawl_frontier
            WHERE attempted_at IS NULL
            ORDER BY discovered_at
            LIMIT @Limit;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        var urls = await connection.QueryAsync<string>(
            sql,
            new { Limit = limit });

        return urls.Select(url => new Uri(url)).ToList();
    }

    public async Task MarkAttemptedAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        const string sql =
            """
            UPDATE crawl_frontier
            SET attempted_at = @AttemptedAt
            WHERE url = @Url;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        await connection.ExecuteAsync(
            sql,
            new
            {
                Url = uri.AbsoluteUri,
                AttemptedAt = DateTimeOffset.UtcNow.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
            });
    }
}