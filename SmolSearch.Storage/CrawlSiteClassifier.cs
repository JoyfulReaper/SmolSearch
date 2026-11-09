
namespace SmolSearch.Storage;

public static class CrawlSiteClassifier
{
    private const string YesterwebCities = "cities.yesterweb.org";

    public static (string Host, string SiteGroup) Classify(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("A URL with a valid hostname is required.", nameof(uri));
        }

        var host = uri.IdnHost.ToLowerInvariant();

        var siteGroup =
            host == YesterwebCities ||
            host.EndsWith("." + YesterwebCities, StringComparison.Ordinal)
                ? YesterwebCities
                : host;

        return (host, siteGroup);
    }
}
