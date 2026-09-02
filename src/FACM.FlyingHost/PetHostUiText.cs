using System.Text;

namespace FACM.FlyingHost;

internal static class PetHostUiText
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppName"] = "FACM",
        ["ControlCenter"] = "控制中心",
        ["Cleanup"] = "清理环境",
        ["CheckUpdate"] = "检查更新",
        ["OpenLog"] = "操作日志",
        ["Exit"] = "退出程序",
        ["PanelTheme"] = "面板主题",
        ["DesktopPet"] = "桌面宠物",
        ["PetReset"] = "宠物复位",
        ["RestoreFloatingBall"] = "恢复默认悬浮球",
        ["MayhemRanking"] = "海斗排行榜",
        ["Close"] = "关闭",
        ["ApplyPet"] = "应用桌宠",
        ["PetSource"] = "来源"
    };

    private static readonly object Sync = new();
    private static Dictionary<string, string> _values = new(Defaults, StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _replacements = new(StringComparer.Ordinal);
    private static string _path = string.Empty;
    private static DateTime _lastWriteUtc = DateTime.MinValue;

    internal static void Configure(string? path)
    {
        lock (Sync)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ui-text.ini"))
                : Path.GetFullPath(path);
            _lastWriteUtc = DateTime.MinValue;
            LoadLocked();
        }
    }

    internal static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        lock (Sync)
        {
            ReloadWhenChangedLocked();
            var translated = text;

            foreach (var rule in _replacements.OrderByDescending(x => x.Key.Length))
            {
                if (rule.Key.Length == 0 || string.Equals(rule.Key, rule.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(rule.Key, rule.Value, StringComparison.Ordinal);
            }

            foreach (var entry in Defaults
                         .Select(x => new KeyValuePair<string, string>(x.Value, GetValueLocked(x.Key, x.Value)))
                         .OrderByDescending(x => x.Key.Length))
            {
                if (entry.Key.Length == 0 || string.Equals(entry.Key, entry.Value, StringComparison.Ordinal)) continue;
                translated = translated.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
            }

            return translated;
        }
    }

    private static string GetValueLocked(string key, string fallback)
    {
        return _values.TryGetValue(key, out var value) ? value : fallback;
    }

    private static void ReloadWhenChangedLocked()
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;
        var write = File.GetLastWriteTimeUtc(_path);
        if (write <= _lastWriteUtc) return;
        LoadLocked();
    }

    private static void LoadLocked()
    {
        _values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        _replacements = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return;
            var section = string.Empty;
            foreach (var sourceLine in File.ReadAllLines(_path, Encoding.UTF8))
            {
                var line = sourceLine ?? string.Empty;
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
                {
                    section = trimmed[1..^1].Trim();
                    continue;
                }

                var separator = FindUnescapedEquals(line);
                if (separator <= 0) continue;
                var key = Unescape(line[..separator].Trim());
                var value = Unescape(line[(separator + 1)..].Trim());
                if (key.Length == 0) continue;

                if (section.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                    _replacements[key] = value;
                else
                    _values[key] = value;
            }
        }
        catch
        {
        }
        finally
        {
            _lastWriteUtc = !string.IsNullOrWhiteSpace(_path) && File.Exists(_path)
                ? File.GetLastWriteTimeUtc(_path)
                : DateTime.MinValue;
        }
    }

    private static int FindUnescapedEquals(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '=') continue;
            var slashes = 0;
            for (var scan = index - 1; scan >= 0 && value[scan] == '\\'; scan--) slashes++;
            if (slashes % 2 == 0) return index;
        }
        return -1;
    }

    private static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current != '\\' || index + 1 >= value.Length)
            {
                builder.Append(current);
                continue;
            }

            var next = value[++index];
            switch (next)
            {
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case '=': builder.Append('='); break;
                case '\\': builder.Append('\\'); break;
                default:
                    builder.Append('\\');
                    builder.Append(next);
                    break;
            }
        }
        return builder.ToString();
    }
}
