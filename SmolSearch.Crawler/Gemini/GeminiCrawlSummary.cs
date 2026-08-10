namespace SmolSearch.Crawler.Gemini;

public sealed record GeminiCrawlSummary
{
    public int Attempted { get; init; }
    public int Indexed { get; init; }
    public long Discovered { get; init; }
    public long Remaining { get; init; }
}