using SmolSearch.Core;
using SmolSearch.Storage;

namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiCrawler
{
    private readonly GeminiClient _client;
    private readonly DocumentStore _documents;
    private readonly GeminiRobotsPolicyProvider _robots;

    public GeminiCrawler(
        GeminiClient client,
        DocumentStore documents,
        GeminiRobotsPolicyProvider robots)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(robots);

        _client = client;
        _documents = documents;
        _robots = robots;
    }

    public async Task<GemtextParseResult?> CrawlAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        GeminiResponse response;

        try
        {
            response = await _client.GetAsync(
                uri,
                _robots.IsAllowedAsync,
                cancellationToken);
        }
        catch (GeminiRequestRejectedException)
        {
            return null;
        }

        if (response.StatusCode is < 20 or >= 30 ||
            response.Body is null ||
            !response.Meta.StartsWith(
                "text/gemini",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = GemtextParser.Parse(response.Uri, response.Body);

        await _documents.UpsertAsync(new SearchDocument
        {
            Url = response.Uri,
            Title = parsed.Title,
            Content = response.Body,
            ContentType = response.Meta,
            FetchedAt = DateTimeOffset.UtcNow
        });

        return parsed;
    }
}