namespace SmolSearch.Crawler.Gemini;

public sealed record GeminiResponse(
    Uri Uri,
    int StatusCode,
    string Meta,
    string? Body);