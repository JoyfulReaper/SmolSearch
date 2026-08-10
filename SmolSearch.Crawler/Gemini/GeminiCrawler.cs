using SmolSearch.Core;
using SmolSearch.Storage;

namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiCrawler
{
    private readonly GeminiClient _client;
    private readonly DocumentStore _documents;

    public GeminiCrawler(
        GeminiClient client,
        DocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(documents);

        _client = client;
        _documents = documents;
    }

    public async Task<GemtextParseResult?> CrawlAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var response = await _client.GetAsync(
            uri,
            cancellationToken);

        if (response.StatusCode is < 20 or >= 30 ||
            response.Body is null ||
            !response.Meta.StartsWith(
                "text/gemini",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = GemtextParser.Parse(
            uri,
            response.Body);

        await _documents.UpsertAsync(new SearchDocument
        {
            Url = uri,
            Title = parsed.Title,
            Content = response.Body,
            ContentType = response.Meta,
            FetchedAt = DateTimeOffset.UtcNow
        });

        return parsed;
    }
}