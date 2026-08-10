namespace SmolSearch.Core;

public sealed record SearchDocument
{
    public required Uri Url { get; init; }
    public string? Title { get; init; }
    public required string Content { get; init; }
    public required string ContentType { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
}