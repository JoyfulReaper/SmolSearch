using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace SmolSearch.Storage;

public sealed class CrawlFrontierStore
{
    private readonly string _connectionString;
    private const int MaxFrontierEntries = 250_000;
    private const int MaxSiteGroupEntries = 2_000;

    internal CrawlFrontierStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task AddRangeAsync(IEnumerable<Uri> uris)
    {
        ArgumentNullException.ThrowIfNull(uris);


        const string sql =
            """
            INSERT OR IGNORE INTO crawl_frontier
            (
                url,
                discovered_at,
                host,
                site_group
            )
            SELECT
                @Url,
                @DiscoveredAt,
                @Host,
                @SiteGroup
            WHERE
                (SELECT COUNT(*) FROM crawl_frontier) < @MaxEntries
                AND
                (
                    SELECT COUNT(*)
                    FROM crawl_frontier
                    WHERE site_group = @SiteGroup
                ) < @MaxSiteGroupEntries;
            """;


        var discoveredAt = DateTimeOffset.UtcNow.ToString(
            "O",
            CultureInfo.InvariantCulture);


        var rows = uris.Select(uri =>
        {
            ArgumentNullException.ThrowIfNull(uri);

            var (host, siteGroup) = CrawlSiteClassifier.Classify(uri);

            return new
            {
                Url = uri.AbsoluteUri,
                DiscoveredAt = discoveredAt,
                Host = host,
                SiteGroup = siteGroup,
                MaxEntries = MaxFrontierEntries,
                MaxSiteGroupEntries = MaxSiteGroupEntries
            };
        }).ToList();


        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync(sql, rows, transaction);

        await transaction.CommitAsync();
    }

    public async Task AddAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);


        const string sql =
            """
            INSERT OR IGNORE INTO crawl_frontier
            (
                url,
                discovered_at,
                host,
                site_group
            )
            SELECT
                @Url,
                @DiscoveredAt,
                @Host,
                @SiteGroup
            WHERE
                (SELECT COUNT(*) FROM crawl_frontier) < @MaxEntries
                AND
                (
                    SELECT COUNT(*)
                    FROM crawl_frontier
                    WHERE site_group = @SiteGroup
                ) < @MaxSiteGroupEntries;
            """;

        var (host, siteGroup) = CrawlSiteClassifier.Classify(uri);
        await using var connection = new SqliteConnection(_connectionString);

        await connection.ExecuteAsync(
            sql,
            new
            {
                Url = uri.AbsoluteUri,
                DiscoveredAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Host = host,
                SiteGroup = siteGroup,
                MaxEntries = MaxFrontierEntries,
                MaxSiteGroupEntries = MaxSiteGroupEntries
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
            WITH host_heads AS
            (
                SELECT
                    site_group,
                    host,
                    MIN(rowid) AS first_rowid
                FROM crawl_frontier
                WHERE attempted_at IS NULL
                GROUP BY site_group, host
            ),
            group_heads AS
            (
                SELECT
                    first_rowid,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY site_group
                        ORDER BY RANDOM()
                    ) AS host_position
                FROM host_heads
            )
            SELECT f.url
            FROM group_heads g
            JOIN crawl_frontier f ON f.rowid = g.first_rowid
            WHERE g.host_position = 1
            ORDER BY RANDOM()
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

    public async Task<long> CountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM crawl_frontier;";

        await using var connection = new SqliteConnection(_connectionString);

        return await connection.ExecuteScalarAsync<long>(sql);
    }

    public async Task<long> CountPendingAsync()
    {
        const string sql =
            """
            SELECT COUNT(*)
            FROM crawl_frontier
            WHERE attempted_at IS NULL;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        return await connection.ExecuteScalarAsync<long>(sql);
    }
}