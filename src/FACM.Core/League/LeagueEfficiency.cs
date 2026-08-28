namespace FACM.Core.League;

public enum LeagueEfficiencyAction
{
    ExitGame,
    CloseLobby
}

[Flags]
public enum LeagueHotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

/// <summary>
/// Platform-neutral representation of the FACM 3.5 League efficiency global-hotkey grammar.
/// Empty text disables a binding. Bare typing keys require a modifier, while function/navigation
/// keys may be registered alone.
/// </summary>
public sealed record LeagueHotkeyBinding(LeagueHotkeyModifiers Modifiers, string Key)
{
    public static LeagueHotkeyBinding Disabled { get; } = new(LeagueHotkeyModifiers.None, string.Empty);

    public bool Enabled => !string.IsNullOrWhiteSpace(Key);

    public override string ToString()
    {
        if (!Enabled) return string.Empty;
        var parts = new List<string>(5);
        if ((Modifiers & LeagueHotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & LeagueHotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & LeagueHotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & LeagueHotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out LeagueHotkeyBinding binding, out string error)
    {
        binding = Disabled;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length == 0) return true;

        var modifiers = LeagueHotkeyModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= LeagueHotkeyModifiers.Control;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= LeagueHotkeyModifiers.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= LeagueHotkeyModifiers.Shift;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= LeagueHotkeyModifiers.Win;
            }
            else
            {
                if (key is not null)
                {
                    error = "快捷键只能包含一个主按键。";
                    return false;
                }
                if (!TryNormalizeKey(part, out key))
                {
                    error = "无法识别按键：" + part;
                    return false;
                }
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            error = "请选择一个非修饰键作为主按键。";
            return false;
        }
        if (modifiers == LeagueHotkeyModifiers.None && IsBareTypingKey(key))
        {
            error = "裸字母/数字容易在聊天或输入账号时误触，请加 Ctrl / Alt / Shift / Win，或使用 F1-F12。";
            return false;
        }

        binding = new LeagueHotkeyBinding(modifiers, key);
        return true;
    }

    private static bool TryNormalizeKey(string value, out string key)
    {
        key = string.Empty;
        var token = (value ?? string.Empty).Trim();
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')
            {
                key = ch.ToString();
                return true;
            }
        }

        if (token.Length >= 2 && (token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            key = "F" + functionKey;
            return true;
        }

        if (token.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && token.Length == 7 &&
            char.IsDigit(token[6]))
        {
            key = "NumPad" + token[6];
            return true;
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = "Escape",
            ["Escape"] = "Escape",
            ["Return"] = "Enter",
            ["Enter"] = "Enter",
            ["Spacebar"] = "Space",
            ["Space"] = "Space",
            ["PgUp"] = "PageUp",
            ["PageUp"] = "PageUp",
            ["PgDn"] = "PageDown",
            ["PageDown"] = "PageDown",
            ["Del"] = "Delete",
            ["Delete"] = "Delete",
            ["Ins"] = "Insert",
            ["Insert"] = "Insert",
            ["Back"] = "Backspace",
            ["Backspace"] = "Backspace",
            ["Tab"] = "Tab",
            ["Home"] = "Home",
            ["End"] = "End",
            ["Left"] = "Left",
            ["Right"] = "Right",
            ["Up"] = "Up",
            ["Down"] = "Down",
            ["CapsLock"] = "CapsLock",
            ["Pause"] = "Pause",
            ["PrintScreen"] = "PrintScreen",
            ["ScrollLock"] = "ScrollLock",
            ["NumLock"] = "NumLock",
            ["Multiply"] = "Multiply",
            ["Add"] = "Add",
            ["Subtract"] = "Subtract",
            ["Decimal"] = "Decimal",
            ["Divide"] = "Divide"
        };
        if (!aliases.TryGetValue(token, out var normalized)) return false;
        key = normalized;
        return true;
    }

    private static bool IsBareTypingKey(string key) =>
        key.Length == 1 && (char.IsLetterOrDigit(key[0])) ||
        key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase);
}

public sealed record LeagueEfficiencyActionResult(string Status, string Detail, int AffectedProcesses);

public interface ILeagueEfficiencyActionService
{
    Task<LeagueEfficiencyActionResult> ExitGameAsync(CancellationToken cancellationToken = default);
    Task<LeagueEfficiencyActionResult> CloseLobbyAsync(CancellationToken cancellationToken = default);
}

public sealed class LeagueGlobalHotkeyPressedEventArgs(LeagueEfficiencyAction action) : EventArgs
{
    public LeagueEfficiencyAction Action { get; } = action;
}

public interface ILeagueGlobalHotkeyService : IDisposable
{
    event EventHandler<LeagueGlobalHotkeyPressedEventArgs>? HotkeyPressed;

    bool TryApply(
        IReadOnlyDictionary<LeagueEfficiencyAction, LeagueHotkeyBinding> bindings,
        out string error);
}
