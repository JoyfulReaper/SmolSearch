using System.Text.Json;

namespace SmolSearch.HappyGemini;

internal sealed record SmolSearchPluginOptions
{
    public required string BaseUrl { get; init; }

    public static SmolSearchPluginOptions Load()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(SmolSearchPluginOptions).Assembly.Location)
            ?? throw new InvalidOperationException("Could not determine SmolSearch plugin directory.");

        var path = Path.Combine(assemblyDirectory, "smolsearch.json");

        using var stream = File.OpenRead(path);

        return JsonSerializer.Deserialize<SmolSearchPluginOptions>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"SmolSearch configuration '{path}' is empty.");
    }
}