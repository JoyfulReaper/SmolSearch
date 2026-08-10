using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

const int defaultMaxPages = 50;

var maxPages = args.Length > 0 && int.TryParse(args[0], out var configuredMaxPages)
    ? configuredMaxPages
    : defaultMaxPages;

if (maxPages <= 0)
{
    throw new ArgumentOutOfRangeException(
        nameof(maxPages),
        "Maximum pages must be greater than zero.");
}

var database = new SmolSearchDatabase("smolsearch.db");
await database.InitializeAsync();

var certificates = database.CreateGeminiCertificateStore();
var documents = database.CreateDocumentStore();
var frontier = database.CreateCrawlFrontierStore();

var client = new GeminiClient(certificates);
var crawler = new GeminiCrawler(client, documents);
var runner = new GeminiCrawlRunner(crawler, frontier);

var seed = new Uri("gemini://geminiprotocol.net/");

var summary = await runner.RunAsync(seed, maxPages);

Console.WriteLine();
Console.WriteLine($"Attempted: {summary.Attempted}");
Console.WriteLine($"Indexed: {summary.Indexed}");
Console.WriteLine($"Discovered: {summary.Discovered}");
Console.WriteLine($"Remaining queue: {summary.Remaining}");