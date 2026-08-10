using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

const int maxPages = 10;

var database = new SmolSearchDatabase("smolsearch.db");

await database.InitializeAsync();

var certificates =
    database.CreateGeminiCertificateStore();

var documents =
    database.CreateDocumentStore();

var client =
    new GeminiClient(certificates);

var crawler =
    new GeminiCrawler(client, documents);

var queue = new Queue<Uri>();

var discovered = new HashSet<string>(
    StringComparer.Ordinal);

var seed =
    new Uri("gemini://geminiprotocol.net/");

discovered.Add(seed.AbsoluteUri);
queue.Enqueue(seed);

var attempted = 0;
var indexed = 0;

while (queue.Count > 0 && attempted < maxPages)
{
    var uri = queue.Dequeue();
    attempted++;

    Console.WriteLine($"Fetching: {uri}");

    try
    {
        var result = await crawler.CrawlAsync(uri);

        if (result is null)
        {
            Console.WriteLine("  Not indexed");
            continue;
        }

        indexed++;

        Console.WriteLine($"  Indexed: {result.Title ?? "(no title)"}");

        foreach (var link in result.Links)
        {
            if (!string.Equals(
                    link.Scheme,
                    "gemini",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (discovered.Add(link.AbsoluteUri))
            {
                queue.Enqueue(link);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"  Failed: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
    }
}

Console.WriteLine();
Console.WriteLine($"Attempted: {attempted}");
Console.WriteLine($"Indexed: {indexed}");
Console.WriteLine($"Discovered: {discovered.Count}");
Console.WriteLine($"Remaining queue: {queue.Count}");