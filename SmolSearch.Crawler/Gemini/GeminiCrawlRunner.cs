namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiCrawlRunner
{
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500);

    private readonly GeminiCrawler _crawler;

    public GeminiCrawlRunner(GeminiCrawler crawler)
    {
        ArgumentNullException.ThrowIfNull(crawler);
        _crawler = crawler;
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

        var queue = new Queue<Uri>();
        var discovered = new HashSet<string>(StringComparer.Ordinal);

        discovered.Add(seed.AbsoluteUri);
        queue.Enqueue(seed);

        var attempted = 0;
        var indexed = 0;

        while (queue.Count > 0 && attempted < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = queue.Dequeue();
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

                foreach (var link in result.Links)
                {
                    if (!string.Equals(link.Scheme, "gemini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (discovered.Add(link.AbsoluteUri))
                    {
                        queue.Enqueue(link);
                    }
                }
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
                await Task.Delay(RequestDelay, cancellationToken);
            }
        }

        return new GeminiCrawlSummary
        {
            Attempted = attempted,
            Indexed = indexed,
            Discovered = discovered.Count,
            Remaining = queue.Count
        };
    }
}