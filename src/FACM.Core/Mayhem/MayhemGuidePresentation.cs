using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FACM.Core.Mayhem;

public sealed record MayhemGuideSection(string Key, string Title, string Body);

/// <summary>
/// The single player-facing text projection for a HaiDou result. WinUI and the share card both
/// consume the same result surface, so missing optional source fields disappear instead of becoming
/// a wall of diagnostic placeholders.
/// </summary>
public sealed class MayhemGuidePresentation
{
    private static readonly Regex AdjustmentPattern = new(
        @"(?<name>造成伤害|承受伤害|攻击速度|技能急速|治疗|护盾|韧性|对小兵伤害|Damage\s+Dealt|Damage\s+Taken|Attack\s+Speed|Ability\s+Haste|Cooldown\s+Reduction|Healing|Shielding|Tenacity|Minion\s+Damage)\s*(?<value>[+-]\s*\d+(?:\.\d+)?%?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private MayhemGuidePresentation(
        string queryTitle,
        string officialName,
        string modeTitle,
        IReadOnlyList<MayhemGuideSection> sections)
    {
        QueryTitle = queryTitle;
        OfficialName = officialName;
        ModeTitle = modeTitle;
        Sections = sections;
    }

    public string QueryTitle { get; }
    public string OfficialName { get; }
    public string ModeTitle { get; }
    public IReadOnlyList<MayhemGuideSection> Sections { get; }

    public static MayhemGuidePresentation Create(MayhemChampionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var query = FirstNonEmpty(result.Query, result.ChampionName, result.ChampionSlug, "英雄");
        var official = result.ChampionName;
        if (string.Equals(query, official, StringComparison.OrdinalIgnoreCase)) official = string.Empty;

        var sections = new List<MayhemGuideSection>();
        Add(sections, "strength", "强度概览", BuildStrength(result));
        Add(sections, "mode", "模式调整", BuildAdjustments(result));
        Add(sections, "summoners", "推荐召唤师技能", JoinNames(result.SummonerSpells.Select(item => item.Name)));
        Add(sections, "runes", "推荐符文", BuildRunes(result.RuneRecommendation));
        Add(sections, "skills", "技能加点", BuildSkills(result));
        Add(sections, "build", "推荐出装", BuildItems(result));
        Add(sections, "ranking", "当前版本强势英雄", BuildRanking(result));
        Add(sections, "data", "数据说明", BuildData(result));

        return new MayhemGuidePresentation(
            query,
            official,
            "当前模式：海克斯大乱斗",
            sections);
    }

    private static string BuildStrength(MayhemChampionResult result)
    {
        var lines = new List<string>();
        var metrics = new List<string>();
        if (result.WinRate.HasValue) metrics.Add("胜率 " + FormatPercent(result.WinRate.Value));
        if (result.PickRate.HasValue) metrics.Add("登场率 " + FormatPercent(result.PickRate.Value));
        if (result.SampleSize is > 0) metrics.Add("样本 " + result.SampleSize.Value.ToString("N0", CultureInfo.InvariantCulture) + " 局");
        if (metrics.Count > 0) lines.Add(string.Join(" · ", metrics));

        var rating = FirstNonEmpty(result.Tier);
        if (!string.IsNullOrWhiteSpace(rating)) lines.Add("当前评价：" + rating + " 档");
        if (result.Rank is > 0) lines.Add("全英雄排名：第 " + result.Rank.Value.ToString(CultureInfo.InvariantCulture) + " 名");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildAdjustments(MayhemChampionResult result)
    {
        var raw = FirstNonEmpty(result.MayhemBalanceSummary, result.BalanceSummary);
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var values = new List<string>();
        foreach (Match match in AdjustmentPattern.Matches(raw))
        {
            var label = TranslateAdjustment(match.Groups["name"].Value);
            var value = Regex.Replace(match.Groups["value"].Value, @"\s+", string.Empty, RegexOptions.CultureInvariant);
            var item = label + " " + value;
            if (!values.Contains(item, StringComparer.OrdinalIgnoreCase)) values.Add(item);
        }

        return string.Join(" · ", values);
    }

    private static string BuildRunes(MayhemRuneRecommendation? rune)
    {
        if (rune is null || !rune.HasLocalizedContent) return string.Empty;
        var lines = new List<string>();
        AddRuneLine(lines, "主系", rune.PrimaryTree);
        AddRuneLine(lines, "基石", rune.Keystone);
        AddRuneLine(lines, "符文", JoinNames(rune.PrimaryRunes));
        AddRuneLine(lines, "副系", rune.SecondaryTree);
        AddRuneLine(lines, "副系符文", JoinNames(rune.SecondaryRunes));
        AddRuneLine(lines, "属性", JoinNames(rune.StatShards));
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSkills(MayhemChampionResult result)
    {
        var lines = new List<string>();
        var skills = result.SkillPriority
            .Where(skill => skill is not null && (skill.Key is "Q" or "W" or "E"))
            .GroupBy(skill => skill.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(3)
            .ToArray();
        if (skills.Length > 0)
        {
            lines.Add("优先级：" + string.Join(" > ", skills.Select(skill => skill.Key)));
            var named = skills
                .Take(2)
                .Select(skill => string.IsNullOrWhiteSpace(skill.Name) || skill.Name == skill.Key
                    ? skill.Key
                    : skill.Key + "（" + skill.Name + "）")
                .ToArray();
            if (named.Length > 0) lines.Add("主升：" + string.Join(" · ", named));
        }

        var order = NormalizeSkillOrder(result.SkillOrder);
        if (!string.IsNullOrWhiteSpace(order)) lines.Add("等级顺序：" + order);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildItems(MayhemChampionResult result)
    {
        var lines = new List<string>();
        AddItemLine(lines, "出门装", result.StarterItems.Select(item => item.Name));
        AddItemLine(lines, "鞋子", result.BootItems.Select(item => item.Name));

        var paths = result.CoreBuilds
            .Where(path => path is not null)
            .Select(path => JoinNames(path.Items.Select(item => item.Name)))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(2)
            .ToArray();
        if (paths.Length > 0) AddItemLine(lines, "核心装备", [paths[0]]);
        if (paths.Length > 1) AddItemLine(lines, "可选装备", [paths[1]]);

        if (lines.Count == 0 && result.CoreItems.Count > 0)
            AddItemLine(lines, "核心装备", result.CoreItems);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildRanking(MayhemChampionResult result)
    {
        var values = result.TopTen
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name))
            .Take(3)
            .Select(item => item.Name + (item.WinRate.HasValue ? " " + FormatPercent(item.WinRate.Value) : string.Empty))
            .ToArray();
        return values.Length == 0 ? string.Empty : string.Join(" · ", values);
    }

    private static string BuildData(MayhemChampionResult result)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Patch)) lines.Add("版本：" + result.Patch);
        lines.Add("数据来源：OP.GG 海克斯大乱斗攻略");
        if (!string.IsNullOrWhiteSpace(result.SourceNote) && result.SourceNote.Contains("腾讯官网已校验", StringComparison.Ordinal))
            lines.Add("版本核验：游戏官方数据");
        if (result.UpdatedAtUtc.HasValue)
            lines.Add("更新时间：" + result.UpdatedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        if (result.SampleSize is > 0)
            lines.Add(result.SampleSize >= 1000 ? "数据质量：数据充足" : "数据质量：样本较少");
        else
            lines.Add("数据质量：该模式数据有限");
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeSkillOrder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var keys = Regex.Matches(value.ToUpperInvariant(), @"[QWER]", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Value)
            .ToArray();
        if (keys.Length > 15) keys = keys[^15..];
        return keys.Length < 4 ? string.Empty : string.Join(" → ", keys);
    }

    private static string TranslateAdjustment(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ", RegexOptions.CultureInvariant) switch
        {
            "damage dealt" => "造成伤害",
            "damage taken" => "承受伤害",
            "attack speed" => "攻击速度",
            "ability haste" or "cooldown reduction" => "技能急速",
            "healing" => "治疗",
            "shielding" => "护盾",
            "tenacity" => "韧性",
            "minion damage" => "对小兵伤害",
            _ => value.Trim()
        };

    private static string JoinNames(IEnumerable<string> values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

    private static void AddItemLine(ICollection<string> lines, string label, IEnumerable<string> values)
    {
        var text = JoinNames(values);
        if (!string.IsNullOrWhiteSpace(text)) lines.Add(label + "：" + text);
    }

    private static void AddRuneLine(ICollection<string> lines, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(label + "：" + value);
    }

    private static void Add(ICollection<MayhemGuideSection> sections, string key, string title, string body)
    {
        if (!string.IsNullOrWhiteSpace(body)) sections.Add(new MayhemGuideSection(key, title, body));
    }

    private static string FormatPercent(double value)
    {
        var normalized = value is > 0 and <= 1 ? value * 100 : value;
        return normalized.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
