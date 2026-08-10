using Dapper;
using Microsoft.Data.Sqlite;
using SmolSearch.Core;

namespace SmolSearch.Storage;

public sealed class GeminiCertificateStore
{
    private readonly string _connectionString;

    internal GeminiCertificateStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task<GeminiCertificatePin?> GetAsync(
        string host,
        int port)
    {
        const string sql =
            """
            SELECT
                host AS Host,
                port AS Port,
                fingerprint AS Fingerprint,
                expires_at AS ExpiresAt
            FROM gemini_certificates
            WHERE host = @Host
              AND port = @Port;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        return await connection.QuerySingleOrDefaultAsync<GeminiCertificatePin>(
            sql,
            new
            {
                Host = host,
                Port = port
            });
    }

    public async Task UpsertAsync(GeminiCertificatePin certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        const string sql =
            """
            INSERT INTO gemini_certificates
            (
                host,
                port,
                fingerprint,
                expires_at
            )
            VALUES
            (
                @Host,
                @Port,
                @Fingerprint,
                @ExpiresAt
            )
            ON CONFLICT(host, port) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                expires_at = excluded.expires_at;
            """;

        await using var connection = new SqliteConnection(_connectionString);

        await connection.ExecuteAsync(sql, certificate);
    }
}