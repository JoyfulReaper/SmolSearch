namespace SmolSearch.Crawler.Gemini;

public sealed class GeminiRobotsPolicy
{
    private static readonly string[] UserAgents =
    [
        "smolsearch",
        "indexer"
    ];

    private readonly IReadOnlyList<string> _disallowedPaths;

    private GeminiRobotsPolicy(IReadOnlyList<string> disallowedPaths)
    {
        _disallowedPaths = disallowedPaths;
    }

    public static GeminiRobotsPolicy Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var groups = new List<RobotsGroup>();
        RobotsGroup? currentGroup = null;
        var sawDirective = false;

        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            var commentIndex = line.IndexOf('#');

            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            line = line.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');

            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (name.Equals(
                    "user-agent",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (currentGroup is null || sawDirective)
                {
                    currentGroup = new RobotsGroup();
                    groups.Add(currentGroup);
                    sawDirective = false;
                }

                currentGroup.UserAgents.Add(value);
                continue;
            }

            if (!name.Equals(
                    "disallow",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (currentGroup is null)
            {
                currentGroup = new RobotsGroup();
                currentGroup.UserAgents.Add("*");
                groups.Add(currentGroup);
            }

            sawDirective = true;

            if (value.Length > 0)
            {
                currentGroup.DisallowedPaths.Add(value);
            }
        }

        var specificGroups = groups
            .Where(group => group.UserAgents.Any(IsSpecificUserAgent))
            .ToList();

        var applicableGroups = specificGroups.Count > 0
            ? specificGroups
            : groups.Where(group => group.UserAgents.Any(agent =>
                agent.Equals(
                    "*",
                    StringComparison.OrdinalIgnoreCase)));

        var disallowedPaths = applicableGroups
            .SelectMany(group => group.DisallowedPaths)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new GeminiRobotsPolicy(disallowedPaths);
    }

    public bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return !_disallowedPaths.Any(pattern =>
            Matches(uri.AbsolutePath, pattern));
    }

    private static bool IsSpecificUserAgent(string agent)
    {
        return UserAgents.Any(userAgent =>
            agent.Equals(
                userAgent,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool Matches(
        string path,
        string pattern)
    {
        var anchored = pattern.EndsWith(
            "$",
            StringComparison.Ordinal);

        if (anchored)
        {
            pattern = pattern[..^1];
        }

        if (!anchored)
        {
            pattern += '*';
        }

        var pathIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var retryPathIndex = -1;

        while (pathIndex < path.Length)
        {
            if (patternIndex < pattern.Length &&
                pattern[patternIndex] == path[pathIndex])
            {
                patternIndex++;
                pathIndex++;
                continue;
            }

            if (patternIndex < pattern.Length &&
                pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryPathIndex = pathIndex;
                continue;
            }

            if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                pathIndex = ++retryPathIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length &&
               pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private sealed class RobotsGroup
    {
        public List<string> UserAgents { get; } = [];

        public List<string> DisallowedPaths { get; } = [];
    }
}