using Dapper;
using Microsoft.Data.Sqlite;
using SmolSearch.Core;
using System.Globalization;

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

        var row = await connection.QuerySingleOrDefaultAsync<CertificateRow>(
            sql,
            new
            {
                Host = host,
                Port = port
            });

        if (row is null)
        {
            return null;
        }

        return new GeminiCertificatePin
        {
            Host = row.Host,
            Port = row.Port,
            Fingerprint = row.Fingerprint,
            ExpiresAt = DateTimeOffset.Parse(
                row.ExpiresAt,
                CultureInfo.InvariantCulture)
        };
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

        await connection.ExecuteAsync(
            sql,
            new
            {
                certificate.Host,
                certificate.Port,
                certificate.Fingerprint,
                ExpiresAt = certificate.ExpiresAt.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
            });
    }

    private sealed record CertificateRow
    {
        public required string Host { get; init; }
        public int Port { get; init; }
        public required string Fingerprint { get; init; }
        public required string ExpiresAt { get; init; }
    }
}