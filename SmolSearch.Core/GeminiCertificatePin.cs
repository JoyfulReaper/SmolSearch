namespace SmolSearch.Core;

public sealed record GeminiCertificatePin
{
    public required string Host { get; init; }
    public int Port { get; init; }
    public required string Fingerprint { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}