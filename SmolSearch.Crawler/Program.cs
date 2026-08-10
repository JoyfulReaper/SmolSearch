using SmolSearch.Crawler.Gemini;

var client = new GeminiClient();

var uri = new Uri("gemini://geminiprotocol.net/");

var response = await client.GetAsync(uri);

Console.WriteLine($"{response.StatusCode} {response.Meta}");

if (response.Body is not null)
{
    Console.WriteLine();
    Console.WriteLine(response.Body);
}