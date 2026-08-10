namespace SmolSearch.Core;

public sealed record SearchResult
{
    public required string Url { get; init; }
    public string? Title { get; init; }
    public double Rank { get; init; }
}