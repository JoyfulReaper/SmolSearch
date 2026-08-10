using SmolSearch.Storage;

var builder = WebApplication.CreateBuilder(args);

var configuredDatabasePath = builder.Configuration["SmolSearch:DatabasePath"];

var databasePath = Path.GetFullPath(
    string.IsNullOrWhiteSpace(configuredDatabasePath)
        ? "smolsearch.db"
        : configuredDatabasePath);

if (!File.Exists(databasePath))
{
    throw new FileNotFoundException(
        "SmolSearch database snapshot was not found.",
        databasePath);
}

Console.WriteLine($"Database: {databasePath}");

var database = new SmolSearchDatabase(
    databasePath,
    readOnly: true);

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

    if (q.Length > 256)
    {
        return Results.BadRequest(new
        {
            Error = "Query must be 256 characters or fewer."
        });
    }

    if (q.Any(char.IsControl))
    {
        return Results.BadRequest(new
        {
            Error = "Query cannot contain control characters."
        });
    }

    var resultLimit = Math.Clamp(limit ?? 20, 1, 100);
    var results = await documents.SearchAsync(q, resultLimit);

    return Results.Ok(results);
});

await app.RunAsync();