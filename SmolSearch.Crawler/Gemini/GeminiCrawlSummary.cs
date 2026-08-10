namespace SmolSearch.Crawler.Gemini;

public sealed record GeminiCrawlSummary
{
    public int Attempted { get; init; }
    public int Indexed { get; init; }
    public int Discovered { get; init; }
    public int Remaining { get; init; }
}