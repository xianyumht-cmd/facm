using FACM.Core.Text;

namespace FACM.Infrastructure.Text;

public sealed class DictionaryUiTextProvider : IUiTextProvider
{
    private readonly IReadOnlyDictionary<string, string> _overrides;

    public DictionaryUiTextProvider(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _overrides = overrides ?? new Dictionary<string, string>();
    }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _overrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : FoundationUiTextDefaults.Get(key);
    }
}

public static class LegacyUiTextOverrideCodec
{
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string>? lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lines is null) return result;
        var inTextSection = false;
        foreach (var raw in lines)
        {
            var line = raw?.Trim() ?? string.Empty;
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inTextSection = line.Equals("[Text]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inTextSection) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0) result[key] = value;
        }
        return result;
    }
}
