using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiClient
{
    private const int DefaultPort = 1965;
    private const int MaxRequestLength = 1024;
    private const int MaxMetaLength = 1024;

    public async Task<GeminiResponse> GetAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("URI must be an absolute Gemini URI.", nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Gemini URI must contain a host.", nameof(uri));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Gemini URIs cannot contain user info.", nameof(uri));
        }

        var request = uri.AbsoluteUri;

        if (Encoding.UTF8.GetByteCount(request) > MaxRequestLength)
        {
            throw new ArgumentException("Gemini request URL exceeds 1024 bytes.", nameof(uri));
        }

        var port = uri.Port < 0
            ? DefaultPort
            : uri.Port;

        using var tcpClient = new TcpClient();

        await tcpClient.ConnectAsync(
            uri.Host,
            port,
            cancellationToken);

        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false);

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = uri.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            cancellationToken);

        var requestBytes = Encoding.UTF8.GetBytes($"{request}\r\n");

        await sslStream.WriteAsync(
            requestBytes,
            cancellationToken);

        await sslStream.FlushAsync(cancellationToken);

        using var reader = new StreamReader(
            sslStream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var header = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidDataException("Gemini server returned no response header.");

        if (header.Length < 3 ||
            header[2] != ' ' ||
            !int.TryParse(
                header.AsSpan(0, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var statusCode))
        {
            throw new InvalidDataException("Gemini server returned an invalid response header.");
        }

        var meta = header[3..];

        if (Encoding.UTF8.GetByteCount(meta) > MaxMetaLength)
        {
            throw new InvalidDataException("Gemini response meta exceeds 1024 bytes.");
        }

        string? body = null;

        var isSuccess = statusCode is >= 20 and < 30;
        var isText =
            string.IsNullOrEmpty(meta) ||
            meta.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

        if (isSuccess && isText)
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        return new GeminiResponse(statusCode, meta, body);
    }
}