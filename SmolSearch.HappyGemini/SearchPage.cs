using HappyGemini.Extensibility;
using SmolSearch.Core;
using System.Net.Http.Json;
using System.Text;

namespace SmolSearch.HappyGemini;

[AutoRegisterGeminiPage]
public sealed class SearchPage(IHttpClientFactory httpClientFactory) : IHostScopedGeminiPage
{
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
                "Search Geminispace",
                cancellationToken);

            return;
        }

        var query = Uri.UnescapeDataString(request.Url.Query[1..]);
        var baseUrl = _options.BaseUrl;

        var client = httpClientFactory.CreateClient();
        var url = $"{baseUrl.TrimEnd('/')}/api/search?q={Uri.EscapeDataString(query)}";

        var results = await client.GetFromJsonAsync<SearchResult[]>(url, cancellationToken) ?? [];

        var displayQuery = ToSingleLine(query);

        var gemtext = new StringBuilder();
        gemtext.AppendLine("# SmolSearch");
        gemtext.AppendLine();
        gemtext.AppendLine($"## Results for: {displayQuery}");
        gemtext.AppendLine();

        foreach (var result in results)
        {
            var title = string.IsNullOrWhiteSpace(result.Title)
                ? result.Url
                : ToSingleLine(result.Title);

            gemtext.AppendLine($"=> {result.Url} {title}");
        }

        await response.WriteHeaderAsync(
            GeminiStatusCode.Success,
            "text/gemini; charset=utf-8",
            cancellationToken);

        await response.WriteTextAsync(
            gemtext.ToString().ReplaceLineEndings("\r\n"),
            cancellationToken);
    }

    private static string ToSingleLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}