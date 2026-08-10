using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

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

var uri =
    new Uri("gemini://geminiprotocol.net/");

var result =
    await crawler.CrawlAsync(uri);

Console.WriteLine(
    $"Indexed: {result?.Title ?? "(not indexed)"}");

if (result is not null)
{
    Console.WriteLine(
        $"Discovered: {result.Links.Count} links");
}