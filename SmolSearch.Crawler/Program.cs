using SmolSearch.Core;
using SmolSearch.Storage;

var database = new SmolSearchDatabase("smolsearch.db");

await database.InitializeAsync();

var documents = database.CreateDocumentStore();

await documents.UpsertAsync(new SearchDocument
{
    Url = new Uri("gemini://example.org/freebsd.gmi"),
    Title = "Running Gemini on FreeBSD",
    Content = "Notes about running a Gemini capsule on FreeBSD using jails.",
    ContentType = "text/gemini",
    FetchedAt = DateTimeOffset.UtcNow
});

await documents.UpsertAsync(new SearchDocument
{
    Url = new Uri("gemini://example.org/linux.gmi"),
    Title = "Linux Server Notes",
    Content = "Various notes about running services on Linux.",
    ContentType = "text/gemini",
    FetchedAt = DateTimeOffset.UtcNow
});

var results = await documents.SearchAsync("freebsd");

foreach (var result in results)
{
    Console.WriteLine($"{result.Rank}: {result.Title}");
    Console.WriteLine(result.Url);
}