using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

const int maxPages = 50;

var database = new SmolSearchDatabase("smolsearch.db");
await database.InitializeAsync();

var certificates = database.CreateGeminiCertificateStore();
var documents = database.CreateDocumentStore();

var client = new GeminiClient(certificates);
var crawler = new GeminiCrawler(client, documents);
var runner = new GeminiCrawlRunner(crawler);

var seed = new Uri("gemini://geminiprotocol.net/");

var summary = await runner.RunAsync(seed, maxPages);

Console.WriteLine();
Console.WriteLine($"Attempted: {summary.Attempted}");
Console.WriteLine($"Indexed: {summary.Indexed}");
Console.WriteLine($"Discovered: {summary.Discovered}");
Console.WriteLine($"Remaining queue: {summary.Remaining}");