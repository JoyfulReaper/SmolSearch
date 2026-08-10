using HappyGemini.Extensibility;
using Microsoft.Extensions.Configuration;
using SmolSearch.Core;
using System.Net.Http.Json;
using System.Text;

namespace SmolSearch.HappyGemini;

[AutoRegisterGeminiPage]
public sealed class SearchPage(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IHostScopedGeminiPage
{
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
        var baseUrl = configuration["SmolSearch:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("SmolSearch:BaseUrl is not configured.");
        }

        var client = httpClientFactory.CreateClient();
        var url = $"{baseUrl.TrimEnd('/')}/api/search?q={Uri.EscapeDataString(query)}";

        var results = await client.GetFromJsonAsync<SearchResult[]>(url, cancellationToken) ?? [];

        var gemtext = new StringBuilder();
        gemtext.AppendLine("# SmolSearch");
        gemtext.AppendLine();
        gemtext.AppendLine($"## Results for: {query}");
        gemtext.AppendLine();

        foreach (var result in results)
        {
            var title = string.IsNullOrWhiteSpace(result.Title) ? result.Url : result.Title;
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
}