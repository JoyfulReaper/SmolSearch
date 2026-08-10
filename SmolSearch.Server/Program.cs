using SmolSearch.Storage;

var builder = WebApplication.CreateBuilder(args);

var configuredDatabasePath = builder.Configuration["SmolSearch:DatabasePath"];

var databasePath = Path.GetFullPath(
    string.IsNullOrWhiteSpace(configuredDatabasePath)
        ? "smolsearch.db"
        : configuredDatabasePath);

Console.WriteLine($"Database: {databasePath}");

var database = new SmolSearchDatabase(databasePath);
await database.InitializeAsync();

var documents = database.CreateDocumentStore();

var app = builder.Build();

app.MapGet("/api/search", async (string? q, int? limit) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new
        {
            Error = "Query parameter 'q' is required."
        });
    }

    var resultLimit = Math.Clamp(limit ?? 20, 1, 100);
    var results = await documents.SearchAsync(q, resultLimit);

    return Results.Ok(results);
});

await app.RunAsync();