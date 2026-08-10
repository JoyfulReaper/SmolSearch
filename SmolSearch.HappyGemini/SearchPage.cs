using HappyGemini.Extensibility;

namespace SmolSearch.HappyGemini;

[AutoRegisterGeminiPage]
public sealed class SearchPage : IHostScopedGeminiPage
{
    public string Path => "/search";

    public IReadOnlyCollection<string> Hostnames { get; } =
        ["gemini.kgivler.com"];

    public async Task WriteAsync(
        GeminiRequest request,
        GeminiResponseWriter response,
        CancellationToken cancellationToken)
    {
        await response.WriteHeaderAsync(
            GeminiStatusCode.Success,
            "text/gemini; charset=utf-8",
            cancellationToken);

        await response.WriteTextAsync(
            """
            # SmolSearch

            SmolSearch HappyGemini plugin loaded successfully.

            """.ReplaceLineEndings("\r\n"),
            cancellationToken);
    }
}