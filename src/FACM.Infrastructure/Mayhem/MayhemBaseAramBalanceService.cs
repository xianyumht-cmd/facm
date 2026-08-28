using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FACM.Core.Mayhem;

namespace FACM.Infrastructure.Mayhem;

internal sealed class MayhemBaseBalanceChange
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
}

internal sealed class MayhemBaseBalanceSnapshot
{
    public string Status { get; set; } = string.Empty;
    public string Patch { get; set; } = string.Empty;
    public string DisplayPatch { get; set; } = string.Empty;
    public bool Complete { get; set; }
    public bool CurrentPatchVerified { get; set; }
    public List<MayhemBaseBalanceChange> Changes { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public string ErrorClass { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
}

/// <summary>
/// Base ARAM balance enrichment preserved from FACM 3.5.15. This layer is deliberately fail-closed:
/// unknown signed balance values or a stale patch are never presented as current complete values.
/// </summary>
internal sealed class MayhemBaseAramBalanceService
{
    internal static readonly TimeSpan SnapshotCacheDuration = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan SourceBudget = TimeSpan.FromSeconds(2.2);

    private readonly MayhemCachedPublicDataTransport _transport;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(DateTimeOffset CachedUtc, MayhemBaseBalanceSnapshot Snapshot);
    private sealed record FieldRule(string Key, string Label, string[] Aliases);

    private static readonly FieldRule[] FieldRules =
    [
        new("damage_dealt", "造成伤害", ["Damage Dealt", "造成伤害"]),
        new("damage_taken", "承受伤害", ["Damage Taken", "Damage Received", "承受伤害", "受到伤害", "承伤"]),
        new("attack_speed", "攻击速度", ["Attack Speed", "攻击速度", "攻速"]),
        new("ability_haste", "技能急速", ["Ability Haste", "Cooldown Reduction", "技能急速", "技能加速", "冷却缩减"]),
        new("healing", "治疗", ["Healing", "Healing Done", "治疗效果", "治疗", "生命恢复"]),
        new("shielding", "护盾", ["Shield Amount", "Shielding", "护盾吸收量", "护盾效果", "护盾量", "护盾"]),
        new("tenacity", "韧性", ["Tenacity", "韧性"]),
        new("minion_damage", "对小兵伤害", ["Damage Dealt to Minions", "Damage to Minions", "Minion Damage", "对小兵伤害", "小兵伤害"]),
        new("resource_regen", "资源回复", ["Energy Regen", "Energy Regeneration", "Mana Regen", "Mana Regeneration", "能量回复", "能量恢复", "法力回复", "法力恢复"])
    ];

    private static readonly string[] SectionMarkers = ["Balance adjustment", "Balance Adjustment", "平衡调整", "平衡性调整"];
    private static readonly string[] EndMarkers = ["Summoner spells", "Summoner Spells", "召唤师技能", "Build", "出装", "Runes", "符文"];

    public MayhemBaseAramBalanceService(
        MayhemCachedPublicDataTransport transport,
        Func<DateTimeOffset>? utcNow = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task EnrichAsync(MayhemChampionResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var slug = MayhemChampionAliases.Slugify(result.ChampionSlug);
        if (string.IsNullOrWhiteSpace(slug)) return;

        if (string.IsNullOrWhiteSpace(result.MayhemBalanceSummary))
            result.MayhemBalanceSummary = result.BalanceSummary;

        var snapshot = await FetchAsync(slug, result.Patch, cancellationToken).ConfigureAwait(false);
        ApplySnapshot(result, snapshot);
    }

    internal static MayhemBaseBalanceSnapshot ParseForSmoke(string? html, string? expectedPatch) =>
        ParsePage(html, expectedPatch);

    internal static void ApplySnapshotForSmoke(MayhemChampionResult result, MayhemBaseBalanceSnapshot? snapshot) =>
        ApplySnapshot(result, snapshot ?? Unavailable("missing_snapshot"));

    private async Task<MayhemBaseBalanceSnapshot> FetchAsync(
        string slug,
        string? expectedPatch,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(slug, out var cached) &&
                _utcNow() - cached.CachedUtc < SnapshotCacheDuration &&
                CachedPatchIsUsable(cached.Snapshot, expectedPatch))
                return Clone(cached.Snapshot);
        }

        var requests = new[]
        {
            new MayhemPublicResourceRequest(MayhemPublicResourceKind.AramLocalizedBuild, slug),
            new MayhemPublicResourceRequest(MayhemPublicResourceKind.AramGlobalBuild, slug)
        };
        var lastError = "unavailable";
        MayhemBaseBalanceSnapshot? lastPartial = null;

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await _transport.GetAsync(
                request,
                SourceBudget,
                cancellationToken,
                allowStale: true).ConfigureAwait(false);
            if (response is null)
            {
                lastError = "unavailable";
                continue;
            }

            var parsed = ParsePage(response.ReadUtf8(), expectedPatch);
            parsed.SourceUrl = MayhemCachedPublicDataTransport.Resolve(request).AbsoluteUri;
            if (!string.Equals(parsed.Status, "unavailable", StringComparison.OrdinalIgnoreCase))
            {
                if (parsed.Complete && !string.Equals(parsed.Status, "syncing", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_sync) _cache[slug] = new CacheEntry(_utcNow(), Clone(parsed));
                }
                return parsed;
            }

            lastError = string.IsNullOrWhiteSpace(parsed.ErrorClass) ? "validation_error" : parsed.ErrorClass;
            lastPartial = parsed;
        }

        if (lastPartial is not null)
        {
            lastPartial.ErrorClass = lastError;
            return lastPartial;
        }
        return Unavailable(lastError);
    }

    private static MayhemBaseBalanceSnapshot ParsePage(string? html, string? expectedPatch)
    {
        var text = CleanVisibleText(html);
        var pagePatch = ExtractPatch(text);
        var section = ExtractSection(text);
        if (section.Length == 0) return Unavailable("balance_section_missing", pagePatch);

        var changes = new List<MayhemBaseBalanceChange>();
        var recognizedSignedValues = 0;
        foreach (var rule in FieldRules)
        {
            var aliases = string.Join("|", rule.Aliases.OrderByDescending(value => value.Length).Select(Regex.Escape));
            var match = Regex.Match(
                section,
                "(?:" + aliases + ")\\s*[:：]?\\s*(?<v>(?:[+-]\\s*)?\\d+(?:\\.\\d+)?%?|-)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) continue;

            var raw = match.Groups["v"].Value.Trim();
            raw = Regex.Replace(raw, "^([+-])\\s+", "$1", RegexOptions.CultureInvariant);
            if (raw.StartsWith('+') || (raw.StartsWith('-') && raw.Length > 1)) recognizedSignedValues++;

            if (raw == "-" || (TryNumeric(raw, out var numeric) && Math.Abs(numeric) < 0.0001d)) continue;
            changes.Add(new MayhemBaseBalanceChange
            {
                Key = rule.Key,
                Label = rule.Label,
                Value = raw,
                Direction = Direction(rule.Key, raw)
            });
        }

        var signedTokens = Regex.Matches(
                section,
                "(?<![\\d.])[+-]\\s*\\d+(?:\\.\\d+)?%?",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();
        if (signedTokens.Length > recognizedSignedValues)
            return Unavailable("unparsed_balance_values", pagePatch, changes);

        var displayPatch = DisplayPatch(pagePatch);
        if (!string.IsNullOrWhiteSpace(expectedPatch) &&
            !string.IsNullOrWhiteSpace(pagePatch) &&
            !PatchesMatch(displayPatch, expectedPatch))
        {
            return new MayhemBaseBalanceSnapshot
            {
                Status = "syncing",
                Patch = pagePatch,
                DisplayPatch = displayPatch,
                Complete = false,
                CurrentPatchVerified = true,
                Changes = [],
                Summary = "当前版本 " + expectedPatch + "，基础 ARAM 页面仍为 " + displayPatch + "，旧完整数值已隐藏。",
                ErrorClass = "patch_mismatch"
            };
        }

        var status = changes.Count > 0 ? "ok" : "none";
        if (string.IsNullOrWhiteSpace(pagePatch)) status = "unverified";
        return new MayhemBaseBalanceSnapshot
        {
            Status = status,
            Patch = pagePatch,
            DisplayPatch = displayPatch,
            Complete = true,
            CurrentPatchVerified = !string.IsNullOrWhiteSpace(expectedPatch) &&
                                   !string.IsNullOrWhiteSpace(pagePatch) &&
                                   PatchesMatch(displayPatch, expectedPatch),
            Changes = changes,
            Summary = changes.Count == 0
                ? "当前无英雄专属基础平衡修正"
                : string.Join(" · ", changes.Select(item => item.Label + " " + item.Value)),
            ErrorClass = string.IsNullOrWhiteSpace(pagePatch) ? "patch_unverified" : string.Empty
        };
    }

    private static void ApplySnapshot(MayhemChampionResult result, MayhemBaseBalanceSnapshot snapshot)
    {
        result.BaseBalancePatch = snapshot.DisplayPatch;
        result.BaseBalanceStatus = snapshot.Status;
        result.BaseBalanceErrorClass = snapshot.ErrorClass;
        result.BaseBalanceComplete = snapshot.Complete;

        var baseText = (snapshot.Status ?? string.Empty).ToLowerInvariant() switch
        {
            "ok" => "基础 ARAM（完整）：" + snapshot.Summary,
            "none" => "基础 ARAM（完整）：当前无英雄专属修正",
            "unverified" => "基础 ARAM（完整，版本未校验）：" + snapshot.Summary,
            "syncing" => "基础 ARAM：" + snapshot.Summary,
            _ => "基础 ARAM：完整平衡暂不可用（不等于无修正）"
        };

        result.BaseBalanceSummary = baseText;
        var mayhem = string.IsNullOrWhiteSpace(result.MayhemBalanceSummary)
            ? "Mayhem：当前未发现英雄专属修正"
            : "Mayhem：" + result.MayhemBalanceSummary;
        result.BalanceSummary = baseText + "\r\n" + mayhem;

        if (string.IsNullOrWhiteSpace(result.SourceNote))
            result.SourceNote = "基础平衡：OP.GG ARAM";
        else if (!result.SourceNote.Contains("基础平衡：", StringComparison.OrdinalIgnoreCase))
            result.SourceNote += "；基础平衡：OP.GG ARAM";
    }

    private static bool CachedPatchIsUsable(MayhemBaseBalanceSnapshot snapshot, string? expectedPatch)
    {
        if (!snapshot.Complete) return false;
        if (string.IsNullOrWhiteSpace(expectedPatch)) return true;
        if (string.IsNullOrWhiteSpace(snapshot.DisplayPatch)) return false;
        return PatchesMatch(snapshot.DisplayPatch, expectedPatch);
    }

    private static MayhemBaseBalanceSnapshot Unavailable(
        string? errorClass,
        string? pagePatch = null,
        List<MayhemBaseBalanceChange>? partial = null) => new()
    {
        Status = "unavailable",
        Patch = pagePatch ?? string.Empty,
        DisplayPatch = DisplayPatch(pagePatch),
        Complete = false,
        CurrentPatchVerified = false,
        Changes = partial ?? [],
        Summary = string.Empty,
        ErrorClass = errorClass ?? string.Empty
    };

    private static MayhemBaseBalanceSnapshot Clone(MayhemBaseBalanceSnapshot source) => new()
    {
        Status = source.Status,
        Patch = source.Patch,
        DisplayPatch = source.DisplayPatch,
        Complete = source.Complete,
        CurrentPatchVerified = source.CurrentPatchVerified,
        Summary = source.Summary,
        ErrorClass = source.ErrorClass,
        SourceUrl = source.SourceUrl,
        Changes = source.Changes.Select(item => new MayhemBaseBalanceChange
        {
            Key = item.Key,
            Label = item.Label,
            Value = item.Value,
            Direction = item.Direction
        }).ToList()
    };

    private static string CleanVisibleText(string? html)
    {
        var text = (html ?? string.Empty)
            .Replace("\\u003c", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("\\u003e", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("\\/", "/", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace('−', '-');
        text = Regex.Replace(
            text,
            "<(script|style)\\b[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string ExtractPatch(string text)
    {
        var patterns = new[]
        {
            "\\bPatch\\s*:?\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\b",
            "\\bVer(?:sion)?\\s*[:：]?\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\b",
            "\\b(?<v>\\d{1,2}\\.\\d{1,2})\\s*版本\\b",
            "版本(?:号)?\\s*[:：]?\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\b"
        };
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success) return match.Groups["v"].Value;
        }
        return string.Empty;
    }

    private static string ExtractSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var starts = SectionMarkers
            .Select(marker => text.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .ToArray();
        if (starts.Length == 0) return string.Empty;
        var start = starts.Min();
        var section = text.Substring(start, Math.Min(2200, text.Length - start));
        var ends = EndMarkers
            .Select(marker => section.IndexOf(marker, 24, StringComparison.OrdinalIgnoreCase))
            .Where(index => index > 0)
            .ToArray();
        return ends.Length == 0 ? section : section[..ends.Min()];
    }

    private static string Direction(string key, string raw)
    {
        if (!TryNumeric(raw, out var numeric) || Math.Abs(numeric) < 0.0001d) return "neutral";
        var signed = raw.StartsWith('+') || raw.StartsWith('-');
        var delta = signed ? numeric : raw.EndsWith('%') ? numeric - 100d : numeric;
        if (Math.Abs(delta) < 0.0001d) return "neutral";
        if (string.Equals(key, "damage_taken", StringComparison.OrdinalIgnoreCase))
            return delta > 0 ? "debuff" : "buff";
        return delta > 0 ? "buff" : "debuff";
    }

    private static bool TryNumeric(string raw, out double value)
    {
        value = 0d;
        var match = Regex.Match(raw ?? string.Empty, "[+-]?\\s*\\d+(?:\\.\\d+)?", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var normalized = Regex.Replace(match.Value, "\\s+", string.Empty, RegexOptions.CultureInvariant);
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string DisplayPatch(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch)) return string.Empty;
        var parts = patch.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return patch;
        if (major is >= 10 and <= 19) major += 10;
        return major.ToString(CultureInfo.InvariantCulture) + "." + minor.ToString(CultureInfo.InvariantCulture);
    }

    private static bool PatchesMatch(string? first, string? second) =>
        Version.TryParse(DisplayPatch(first), out var left) &&
        Version.TryParse(DisplayPatch(second), out var right) &&
        left.Equals(right);
}
