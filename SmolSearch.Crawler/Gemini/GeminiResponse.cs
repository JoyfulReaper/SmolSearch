namespace SmolSearch.Crawler.Gemini;

public sealed record GeminiResponse(
    int StatusCode,
    string Meta,
    string? Body);