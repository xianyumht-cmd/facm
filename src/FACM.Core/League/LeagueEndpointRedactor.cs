namespace FACM.Core.League;

public static class LeagueEndpointRedactor
{
    private static readonly string[] DynamicSegmentPrefixes =
    [
        "/lol-match-history/v1/products/lol/",
        "/lol-summoner/v1/summoners/",
        "/lol-summoner/v1/summoner/"
    ];

    public static string Redact(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var value = path.Trim();
        var queryIndex = value.IndexOf('?');
        var route = queryIndex < 0 ? value : value[..queryIndex];
        var query = queryIndex < 0 ? string.Empty : value[queryIndex..];

        foreach (var prefix in DynamicSegmentPrefixes)
        {
            if (!route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var dynamicStart = prefix.Length;
            var nextSlash = route.IndexOf('/', dynamicStart);
            var dynamicEnd = nextSlash < 0 ? route.Length : nextSlash;
            if (dynamicEnd <= dynamicStart) return value;
            return route[..dynamicStart] + "{redacted}" + route[dynamicEnd..] + query;
        }

        return value;
    }
}
