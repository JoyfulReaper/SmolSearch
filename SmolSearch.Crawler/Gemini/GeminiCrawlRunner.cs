using SmolSearch.Storage;

namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiCrawlRunner
{
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500);

    private readonly GeminiCrawler _crawler;
    private readonly CrawlFrontierStore _frontier;

    public GeminiCrawlRunner(GeminiCrawler crawler, CrawlFrontierStore frontier)
    {
        ArgumentNullException.ThrowIfNull(crawler);
        ArgumentNullException.ThrowIfNull(frontier);

        _crawler = crawler;
        _frontier = frontier;
    }

    public async Task<GeminiCrawlSummary> RunAsync(
        Uri seed,
        int maxPages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (maxPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }

        await _frontier.AddAsync(seed);

        var attempted = 0;
        var indexed = 0;

        while (attempted < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pending = await _frontier.GetPendingAsync(1);

            if (pending.Count == 0)
            {
                break;
            }

            var uri = pending[0];
            attempted++;

            Console.WriteLine($"Fetching: {uri}");

            try
            {
                var result = await _crawler.CrawlAsync(uri, cancellationToken);

                if (result is null)
                {
                    Console.WriteLine("  Not indexed");
                    continue;
                }

                indexed++;

                Console.WriteLine($"  Indexed: {result.Title ?? "(no title)"}");

                var links = result.Links
                    .Where(link => string.Equals(
                        link.Scheme,
                        "gemini",
                        StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(link => link.AbsoluteUri, StringComparer.Ordinal)
                    .ToList();

                await _frontier.AddRangeAsync(links);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await _frontier.MarkAttemptedAsync(uri);
                }

                await Task.Delay(RequestDelay, cancellationToken);
            }
        }

        return new GeminiCrawlSummary
        {
            Attempted = attempted,
            Indexed = indexed,
            Discovered = await _frontier.CountAsync(),
            Remaining = await _frontier.CountPendingAsync()
        };
    }
}