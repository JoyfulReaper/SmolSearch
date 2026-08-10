using System.Text;

namespace SmolSearch.Crawler.Gemini;

public static class GemtextParser
{
    private const int MaxLinks = 512;
    private const int MaxGeminiUrlLength = 1024;

    public static GemtextParseResult Parse(
        Uri baseUri,
        string content)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(content);

        string? title = null;
        var links = new List<Uri>();
        var preformatted = false;

        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                preformatted = !preformatted;
                continue;
            }

            if (preformatted)
            {
                continue;
            }

            if (title is null &&
                TryGetHeading(line, out var heading))
            {
                title = heading;
            }

            if (links.Count < MaxLinks &&
                TryGetLink(baseUri, line, out var link))
            {
                links.Add(link);
            }
        }

        return new GemtextParseResult
        {
            Title = title,
            Links = links
        };
    }

    private static bool TryGetHeading(
        string line,
        out string heading)
    {
        heading = string.Empty;

        var index = 0;

        while (index < line.Length &&
               index < 3 &&
               line[index] == '#')
        {
            index++;
        }

        if (index == 0 ||
            index == line.Length ||
            line[index] is not (' ' or '\t'))
        {
            return false;
        }

        heading = line[index..].Trim();

        return heading.Length > 0;
    }

    private static bool TryGetLink(
        Uri baseUri,
        string line,
        out Uri link)
    {
        link = null!;

        if (!line.StartsWith("=>", StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = line[2..].TrimStart();

        if (remainder.Length == 0)
        {
            return false;
        }

        var separator = remainder.IndexOfAny([' ', '\t']);

        var target = separator < 0
            ? remainder
            : remainder[..separator];

        if (!Uri.TryCreate(baseUri, target, out var parsedLink))
        {
            return false;
        }

        if (string.Equals(
                parsedLink.Scheme,
                "gemini",
                StringComparison.OrdinalIgnoreCase) &&
            Uri.CheckHostName(parsedLink.IdnHost) == UriHostNameType.Unknown)
        {
            return false;
        }

        var normalizedLink = string.IsNullOrEmpty(parsedLink.Fragment)
            ? parsedLink
            : new UriBuilder(parsedLink)
            {
                Fragment = string.Empty
            }.Uri;

        if (string.Equals(
                normalizedLink.Scheme,
                "gemini",
                StringComparison.OrdinalIgnoreCase) &&
            Encoding.UTF8.GetByteCount(normalizedLink.AbsoluteUri) > MaxGeminiUrlLength)
        {
            return false;
        }

        link = normalizedLink;
        return true;
    }
}