using HappyGemini.Extensibility;
using SmolSearch.Core;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmolSearch.HappyGemini;

[AutoRegisterGeminiPage]
public sealed class SearchPage(IHttpClientFactory httpClientFactory) : IHostScopedGeminiPage
{
    private const int PageSize = 20;
    private const string PageQueryPrefix = "__smolsearch_page=";
    private readonly SmolSearchPluginOptions _options = SmolSearchPluginOptions.Load();
    public string Path => "/search";

    public IReadOnlyCollection<string> Hostnames { get; } =
        ["gemini.kgivler.com"];

    public async Task WriteAsync(
        GeminiRequest request,
        GeminiResponseWriter response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Url.Query))
        {
            await response.WriteHeaderAsync(
                GeminiStatusCode.Input,
                "Search Geminispace (POC snapshot)",
                cancellationToken);

            return;
        }

        var (query, page) = ParseQueryAndPage(request.Url.Query);
        var offset = (page - 1) * PageSize;
        var baseUrl = _options.BaseUrl;

        var client = httpClientFactory.CreateClient();
        var url = $"{baseUrl.TrimEnd('/')}/api/search?q={Uri.EscapeDataString(query)}&limit={PageSize + 1}&offset={offset}";

        SearchResult[] results;

        try
        {
            results = await client.GetFromJsonAsync<SearchResult[]>(url, cancellationToken) ?? [];
        }
        catch (HttpRequestException)
        {
            await WriteBackendUnavailableAsync(response, cancellationToken);
            return;
        }
        catch (JsonException)
        {
            await WriteBackendUnavailableAsync(response, cancellationToken);
            return;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteBackendUnavailableAsync(response, cancellationToken);
            return;
        }

        var displayQuery = ToSingleLine(query);

        var gemtext = new StringBuilder();
        gemtext.AppendLine("# SmolSearch");
        gemtext.AppendLine();
        gemtext.AppendLine("> Proof of concept: results come from a static crawl snapshot.");
        gemtext.AppendLine("> The index is not continuously updated yet and results may be stale.");
        gemtext.AppendLine();
        gemtext.AppendLine($"## Results for: {displayQuery}");
        gemtext.AppendLine();
        gemtext.AppendLine($"Page {page}");
        gemtext.AppendLine();

        if (results.Length == 0)
        {
            gemtext.AppendLine("No results found on this page.");
            gemtext.AppendLine();
        }

        foreach (var result in results.Take(PageSize))
        {
            var title = string.IsNullOrWhiteSpace(result.Title)
                ? result.Url
                : ToSingleLine(result.Title);

            gemtext.AppendLine($"=> {result.Url} {title}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                gemtext.AppendLine($"> {ToSingleLine(result.Snippet)}");
            }

            gemtext.AppendLine();
        }

        if (page > 1)
        {
            gemtext.AppendLine($"=> {BuildPageUrl(query, page - 1)} Previous page");
        }

        if (results.Length > PageSize)
        {
            gemtext.AppendLine($"=> {BuildPageUrl(query, page + 1)} Next page");
        }

        gemtext.AppendLine("=> /search New search");

        await response.WriteHeaderAsync(
            GeminiStatusCode.Success,
            "text/gemini; charset=utf-8",
            cancellationToken);

        await response.WriteTextAsync(
            gemtext.ToString().ReplaceLineEndings("\r\n"),
            cancellationToken);
    }

    private static async Task WriteBackendUnavailableAsync(
        GeminiResponseWriter response,
        CancellationToken cancellationToken)
    {
        await response.WriteHeaderAsync(
            GeminiStatusCode.ServerUnavailable,
            "SmolSearch backend temporarily unavailable",
            cancellationToken);
    }

    private static string ToSingleLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static (string Query, int Page) ParseQueryAndPage(string rawQuery)
    {
        var encodedQuery = rawQuery[1..];
        const string separator = "&q=";

        if (encodedQuery.StartsWith(PageQueryPrefix, StringComparison.Ordinal))
        {
            var separatorIndex = encodedQuery.IndexOf(
                separator,
                PageQueryPrefix.Length,
                StringComparison.Ordinal);

            if (separatorIndex >= 0 &&
                int.TryParse(encodedQuery.AsSpan(
                    PageQueryPrefix.Length,
                    separatorIndex - PageQueryPrefix.Length), out var page) &&
                page >= 1 &&
                page <= (int.MaxValue / PageSize) + 1)
            {
                var query = Uri.UnescapeDataString(encodedQuery[(separatorIndex + separator.Length)..]);
                return (query, page);
            }
        }

        return (Uri.UnescapeDataString(encodedQuery), 1);
    }

    private static string BuildPageUrl(string query, int page)
    {
        return $"/search?{PageQueryPrefix}{page}&q={Uri.EscapeDataString(query)}";
    }
}
