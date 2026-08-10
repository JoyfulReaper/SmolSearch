namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiRobotsPolicyProvider
{
    private const int DefaultPort = 1965;

    private readonly GeminiClient _client;
    private readonly Dictionary<(string Host, int Port), GeminiRobotsPolicy?> _cache = [];

    public GeminiRobotsPolicyProvider(GeminiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    public async Task<bool> IsAllowedAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, "gemini", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "URI must be an absolute Gemini URI.",
                nameof(uri));
        }

        var key = (
            uri.IdnHost.ToLowerInvariant(),
            GetPort(uri));

        if (!_cache.TryGetValue(key, out var policy))
        {
            policy = await LoadAsync(uri, cancellationToken);
            _cache[key] = policy;
        }

        // null means we could not determine the site's policy.
        // Fail closed for this crawl.
        return policy is not null && policy.IsAllowed(uri);
    }

    private async Task<GeminiRobotsPolicy?> LoadAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var robotsUri = new UriBuilder(
            "gemini",
            uri.Host,
            GetPort(uri),
            "/robots.txt").Uri;

        try
        {
            var response = await _client.GetAsync(
                robotsUri,
                cancellationToken);

            if (!IsSameAuthority(robotsUri, response.Uri))
            {
                return null;
            }

            if (response.StatusCode == 51)
            {
                return GeminiRobotsPolicy.Parse(string.Empty);
            }

            if (response.StatusCode != 20 ||
                response.Body is null ||
                !IsSupportedContentType(response.Meta))
            {
                return null;
            }

            return GeminiRobotsPolicy.Parse(response.Body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSupportedContentType(string meta)
    {
        var separator = meta.IndexOf(';');

        var contentType = separator < 0
            ? meta
            : meta[..separator];

        contentType = contentType.Trim();

        return contentType.Equals(
                   "text/plain",
                   StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals(
                   "text/gemini",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameAuthority(Uri left, Uri right)
    {
        return string.Equals(
                   left.IdnHost,
                   right.IdnHost,
                   StringComparison.OrdinalIgnoreCase) &&
               GetPort(left) == GetPort(right);
    }

    private static int GetPort(Uri uri)
    {
        return uri.Port < 0
            ? DefaultPort
            : uri.Port;
    }
}