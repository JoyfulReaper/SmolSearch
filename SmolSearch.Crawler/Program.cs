using SmolSearch.Core;
using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

var database = new SmolSearchDatabase("smolsearch.db");

await database.InitializeAsync();

var certificates =
    database.CreateGeminiCertificateStore();

var documents =
    database.CreateDocumentStore();

var client = new GeminiClient(certificates);

var uri = new Uri("gemini://geminiprotocol.net/");

var response = await client.GetAsync(uri);

Console.WriteLine(
    $"{response.StatusCode} {response.Meta}");

if (response.Body is null)
{
    return;
}

var parsed = GemtextParser.Parse(
    uri,
    response.Body);

await documents.UpsertAsync(new SearchDocument
{
    Url = uri,
    Title = parsed.Title,
    Content = response.Body,
    ContentType = response.Meta,
    FetchedAt = DateTimeOffset.UtcNow
});

Console.WriteLine();
Console.WriteLine($"Indexed: {parsed.Title ?? uri.ToString()}");

var results = await documents.SearchAsync("Gemini");

Console.WriteLine();
Console.WriteLine("Search results:");

foreach (var result in results)
{
    Console.WriteLine($"{result.Rank}: {result.Title}");
    Console.WriteLine(result.Url);
}