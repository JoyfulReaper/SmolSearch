using SmolSearch.Storage;

var database = new SmolSearchDatabase("smolsearch.db");
await database.InitializeAsync();

var documents = database.CreateDocumentStore();

Console.Write("Search: ");

var query = Console.ReadLine();

if (string.IsNullOrWhiteSpace(query))
{
    return;
}

var results = await documents.SearchAsync(query, 10);

Console.WriteLine();
Console.WriteLine($"# Search results for: {query}");
Console.WriteLine();

foreach (var result in results)
{
    Console.WriteLine($"=> {result.Url} {result.Title ?? result.Url}");
}