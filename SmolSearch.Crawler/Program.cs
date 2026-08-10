using SmolSearch.Crawler.Gemini;
using SmolSearch.Storage;

var database = new SmolSearchDatabase("smolsearch.db");

await database.InitializeAsync();

var certificates =
    database.CreateGeminiCertificateStore();

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

Console.WriteLine();
Console.WriteLine($"Title: {parsed.Title ?? "(none)"}");
Console.WriteLine($"Links: {parsed.Links.Count}");
Console.WriteLine();

foreach (var link in parsed.Links)
{
    Console.WriteLine(link);
}