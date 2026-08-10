namespace SmolSearch.Crawler.Gemini;

public sealed record GemtextParseResult
{
    public string? Title { get; init; }
    public required IReadOnlyList<Uri> Links { get; init; }
}