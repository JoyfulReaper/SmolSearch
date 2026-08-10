using SmolSearch.Core;
using SmolSearch.Storage;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiClient
{
    private const int DefaultPort = 1965;
    private const int MaxRequestLength = 1024;
    private const int MaxMetaLength = 1024;
    private const int MaxBodyLength = 1_048_576;
    private const int MaxRedirects = 5;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly GeminiCertificateStore _certificateStore;

    public GeminiClient(GeminiCertificateStore certificateStore)
    {
        ArgumentNullException.ThrowIfNull(certificateStore);

        _certificateStore = certificateStore;
    }

    public async Task<GeminiResponse> GetAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var currentUri = uri;

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            var response = await SendAsync(currentUri, cancellationToken);

            if (response.StatusCode is < 30 or >= 40)
            {
                return response;
            }

            if (redirectCount == MaxRedirects)
            {
                throw new InvalidDataException("Gemini redirect limit exceeded.");
            }

            if (!Uri.TryCreate(currentUri, response.Meta, out var redirectUri))
            {
                throw new InvalidDataException(
                    $"Gemini server returned invalid redirect URI: {response.Meta}");
            }

            if (!string.Equals(
                    redirectUri.Scheme,
                    "gemini",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Gemini redirect uses unsupported scheme: {redirectUri.Scheme}");
            }

            currentUri = redirectUri;
        }

        throw new InvalidOperationException("Unreachable.");
    }

    private async Task<GeminiResponse> SendAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "URI must be an absolute Gemini URI.",
                nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException(
                "Gemini URI must contain a host.",
                nameof(uri));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "Gemini URIs cannot contain user info.",
                nameof(uri));
        }

        var request = uri.AbsoluteUri;

        if (Encoding.UTF8.GetByteCount(request) > MaxRequestLength)
        {
            throw new ArgumentException(
                "Gemini request URL exceeds 1024 bytes.",
                nameof(uri));
        }

        var port = uri.Port < 0 ? DefaultPort : uri.Port;

        var knownCertificate = await _certificateStore.GetAsync(uri.Host, port);
        GeminiCertificatePin? newCertificate = null;

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutSource.CancelAfter(RequestTimeout);

        var requestToken = timeoutSource.Token;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(
                uri.Host,
                requestToken);

            var allowedAddresses = addresses
                .Where(IsAllowedRemoteAddress)
                .ToArray();

            if (allowedAddresses.Length == 0)
            {
                throw new InvalidDataException(
                    $"Gemini host resolves only to non-public addresses: {uri.Host}");
            }

            using var tcpClient = new TcpClient();

            await tcpClient.ConnectAsync(
                allowedAddresses,
                port,
                requestToken);

            await using var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);

            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = uri.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,

                    RemoteCertificateValidationCallback =
                        (_, certificate, _, _) =>
                        {
                            if (certificate is null)
                            {
                                return false;
                            }

                            using var certificate2 =
                                X509CertificateLoader.LoadCertificate(
                                    certificate.GetRawCertData());

                            var now = DateTimeOffset.UtcNow;

                            var notBefore = new DateTimeOffset(
                                certificate2.NotBefore.ToUniversalTime());

                            var notAfter = new DateTimeOffset(
                                certificate2.NotAfter.ToUniversalTime());

                            if (now < notBefore || now > notAfter)
                            {
                                return false;
                            }

                            var fingerprint = certificate2.GetCertHashString(
                                HashAlgorithmName.SHA256);

                            if (knownCertificate is not null &&
                                knownCertificate.ExpiresAt > now)
                            {
                                return string.Equals(
                                    knownCertificate.Fingerprint,
                                    fingerprint,
                                    StringComparison.OrdinalIgnoreCase);
                            }

                            newCertificate = new GeminiCertificatePin
                            {
                                Host = uri.Host,
                                Port = port,
                                Fingerprint = fingerprint,
                                ExpiresAt = notAfter
                            };

                            return true;
                        }
                },
                requestToken);

            if (newCertificate is not null)
            {
                await _certificateStore.UpsertAsync(newCertificate);
            }

            var requestBytes = Encoding.UTF8.GetBytes($"{request}\r\n");

            await sslStream.WriteAsync(requestBytes, requestToken);
            await sslStream.FlushAsync(requestToken);

            var header = await ReadResponseHeaderAsync(
                sslStream,
                requestToken);

            using var reader = new StreamReader(
                sslStream,
                new UTF8Encoding(false, false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);

            if (header.Length < 3 ||
                header[2] != ' ' ||
                !int.TryParse(
                    header.AsSpan(0, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var statusCode))
            {
                throw new InvalidDataException(
                    "Gemini server returned an invalid response header.");
            }

            var meta = header[3..];

            if (Encoding.UTF8.GetByteCount(meta) > MaxMetaLength)
            {
                throw new InvalidDataException(
                    "Gemini response meta exceeds 1024 bytes.");
            }

            string? body = null;

            var isSuccess = statusCode is >= 20 and < 30;
            var isText =
                string.IsNullOrEmpty(meta) ||
                meta.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

            if (isSuccess && isText)
            {
                var builder = new StringBuilder();
                var buffer = new char[8192];

                while (true)
                {
                    var read = await reader.ReadAsync(buffer, requestToken);

                    if (read == 0)
                    {
                        break;
                    }

                    if (builder.Length + read > MaxBodyLength)
                    {
                        throw new InvalidDataException(
                            "Gemini response body exceeds maximum size.");
                    }

                    builder.Append(buffer, 0, read);
                }

                body = builder.ToString();
            }

            return new GeminiResponse(
                uri,
                statusCode,
                meta,
                body);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Gemini request timed out: {uri}");
        }
    }

    private static async Task<string> ReadResponseHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        const int headerPrefixLength = 3;
        const int terminatorLength = 2;

        var buffer = new byte[
            headerPrefixLength + MaxMetaLength + terminatorLength];

        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(length, 1),
                cancellationToken);

            if (read == 0)
            {
                throw new InvalidDataException(
                    "Gemini server returned an incomplete response header.");
            }

            length += read;

            if (length >= 2 &&
                buffer[length - 2] == '\r' &&
                buffer[length - 1] == '\n')
            {
                return Encoding.UTF8.GetString(
                    buffer,
                    0,
                    length - terminatorLength);
            }
        }

        throw new InvalidDataException(
            "Gemini response header exceeds maximum length.");
    }
    private static bool IsAllowedRemoteAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] != 0 &&
                   bytes[0] != 10 &&
                   bytes[0] != 127 &&
                   !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
                   !(bytes[0] == 169 && bytes[1] == 254) &&
                   !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                   !(bytes[0] == 192 && bytes[1] == 168) &&
                   !(bytes[0] == 198 && bytes[1] is 18 or 19) &&
                   bytes[0] < 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.Equals(IPAddress.IPv6Any) &&
                   !address.IsIPv6LinkLocal &&
                   !address.IsIPv6SiteLocal &&
                   !address.IsIPv6UniqueLocal &&
                   !address.IsIPv6Multicast;
        }

        return false;
    }

}