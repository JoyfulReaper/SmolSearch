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

if (response.Body is not null)
{
    Console.WriteLine();
    Console.WriteLine(response.Body);
}