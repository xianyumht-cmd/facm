using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.Mayhem
{
    internal sealed class AramBaseBalanceChange
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public string Direction { get; set; }
    }

    internal sealed class AramBaseBalanceSnapshot
    {
        public string Status { get; set; }
        public string Patch { get; set; }
        public string DisplayPatch { get; set; }
        public bool Complete { get; set; }
        public bool CurrentPatchVerified { get; set; }
        public List<AramBaseBalanceChange> Changes { get; set; } = new List<AramBaseBalanceChange>();
        public string Summary { get; set; }
        public string ErrorClass { get; set; }
    }

    internal static class OpggAramBaseBalanceService
    {
        private const string BaseUrl = "https://op.gg/lol/modes/aram/";
        private static readonly HttpClient Client = CreateClient();
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            public DateTime Time { get; set; }
            public AramBaseBalanceSnapshot Snapshot { get; set; }
        }

        private sealed class FieldRule
        {
            public string Key { get; set; }
            public string Label { get; set; }
            public string[] Aliases { get; set; }
        }

        private static readonly FieldRule[] FieldRules =
        {
            new FieldRule { Key = "damage_dealt", Label = "造成伤害", Aliases = new[] { "Damage Dealt", "造成伤害" } },
            new FieldRule { Key = "damage_taken", Label = "承受伤害", Aliases = new[] { "Damage Taken", "Damage Received", "承受伤害", "受到伤害", "承伤" } },
            new FieldRule { Key = "attack_speed", Label = "攻击速度", Aliases = new[] { "Attack Speed", "攻击速度", "攻速" } },
            new FieldRule { Key = "ability_haste", Label = "技能急速", Aliases = new[] { "Ability Haste", "Cooldown Reduction", "技能急速", "技能加速", "冷却缩减" } },
            new FieldRule { Key = "healing", Label = "治疗", Aliases = new[] { "Healing", "Healing Done", "治疗效果", "治疗", "生命恢复" } },
            new FieldRule { Key = "shielding", Label = "护盾", Aliases = new[] { "Shield Amount", "Shielding", "护盾吸收量", "护盾效果", "护盾量", "护盾" } },
            new FieldRule { Key = "tenacity", Label = "韧性", Aliases = new[] { "Tenacity", "韧性" } },
            new FieldRule { Key = "minion_damage", Label = "对小兵伤害", Aliases = new[] { "Damage Dealt to Minions", "Damage to Minions", "Minion Damage", "对小兵伤害", "小兵伤害" } },
            new FieldRule { Key = "resource_regen", Label = "资源回复", Aliases = new[] { "Energy Regen", "Energy Regeneration", "Mana Regen", "Mana Regeneration", "能量回复", "能量恢复", "法力回复", "法力恢复" } }
        };

        private static readonly string[] SectionMarkers =
        {
            "Balance adjustment", "Balance Adjustment", "平衡调整", "平衡性调整"
        };

        private static readonly string[] EndMarkers =
        {
            "Summoner spells", "Summoner Spells", "召唤师技能", "Build", "出装", "Runes", "符文"
        };

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.ChampionSlug)) return;

            if (string.IsNullOrWhiteSpace(result.MayhemBalanceSummary))
                result.MayhemBalanceSummary = result.BalanceSummary;

            AramBaseBalanceSnapshot snapshot;
            try
            {
                snapshot = await FetchAsync(result.ChampionSlug, result.Patch, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
                snapshot = Unavailable("timeout");
            }
            catch (Exception exception)
            {
                AppLog.Info("Base ARAM balance enrichment skipped: " + exception.Message);
                snapshot = Unavailable(exception.GetType().Name);
            }

            ApplySnapshot(result, snapshot);
        }

        internal static AramBaseBalanceSnapshot ParseForSmokeTest(string html, string expectedPatch)
        {
            return ParsePage(html, expectedPatch);
        }

        private static async Task<AramBaseBalanceSnapshot> FetchAsync(string slug, string expectedPatch, CancellationToken token)
        {
            var key = (slug ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0) return Unavailable("missing_slug");

            lock (Sync)
            {
                CacheEntry cached;
                if (Cache.TryGetValue(key, out cached) &&
                    cached != null && cached.Snapshot != null &&
                    DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(10) &&
                    CachedPatchIsUsable(cached.Snapshot, expectedPatch))
                {
                    return Clone(cached.Snapshot);
                }
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(2.5));
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + key + "/build"))
                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return Unavailable("http_" + (int)response.StatusCode);
                        var html = await CancelableHttpContentReader.ReadStringAsync(response.Content, timeout.Token).ConfigureAwait(false);
                        var parsed = ParsePage(html, expectedPatch);
                        if (parsed.Complete && parsed.Status != "syncing")
                        {
                            lock (Sync)
                            {
                                Cache[key] = new CacheEntry { Time = DateTime.UtcNow, Snapshot = Clone(parsed) };
                            }
                        }
                        return parsed;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    return Unavailable("timeout");
                }
                catch (Exception exception)
                {
                    AppLog.Info("Base ARAM balance source request failed: " + exception.Message);
                    return Unavailable(exception.GetType().Name);
                }
            }
        }

        private static AramBaseBalanceSnapshot ParsePage(string html, string expectedPatch)
        {
            var text = CleanVisibleText(html);
            var pagePatch = ExtractPatch(text);
            var section = ExtractSection(text);
            if (section.Length == 0)
                return Unavailable("balance_section_missing", pagePatch);

            var changes = new List<AramBaseBalanceChange>();
            var recognizedNumericValues = 0;
            foreach (var rule in FieldRules)
            {
                var aliases = string.Join("|", rule.Aliases.OrderByDescending(value => value.Length).Select(Regex.Escape));
                var match = Regex.Match(
                    section,
                    "(?:" + aliases + ")\\s*[:：]?\\s*(?<v>[+-]?\\d+(?:\\.\\d+)?%?|-)",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                var raw = match.Groups["v"].Value.Trim();
                double numeric;
                if (TryNumeric(raw, out numeric)) recognizedNumericValues++;
                if (raw == "-" || (TryNumeric(raw, out numeric) && Math.Abs(numeric) < 0.0001)) continue;

                changes.Add(new AramBaseBalanceChange
                {
                    Key = rule.Key,
                    Label = rule.Label,
                    Value = raw,
                    Direction = Direction(rule.Key, raw)
                });
            }

            var numericTokens = Regex.Matches(section, "(?<![\\d.])[+-]?\\d+(?:\\.\\d+)?%?")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToArray();
            if (numericTokens.Length > recognizedNumericValues)
                return Unavailable("unparsed_balance_values", pagePatch, changes);

            var displayPatch = DisplayPatch(pagePatch);
            if (!string.IsNullOrWhiteSpace(expectedPatch) && !string.IsNullOrWhiteSpace(pagePatch) &&
                !PatchesMatch(displayPatch, expectedPatch))
            {
                return new AramBaseBalanceSnapshot
                {
                    Status = "syncing",
                    Patch = pagePatch,
                    DisplayPatch = displayPatch,
                    Complete = false,
                    CurrentPatchVerified = true,
                    Changes = new List<AramBaseBalanceChange>(),
                    Summary = "当前版本 " + expectedPatch + "，基础 ARAM 页面仍为 " + displayPatch + "，旧完整数值已隐藏。",
                    ErrorClass = "patch_mismatch"
                };
            }

            var status = changes.Count > 0 ? "ok" : "none";
            if (string.IsNullOrWhiteSpace(pagePatch)) status = "unverified";
            return new AramBaseBalanceSnapshot
            {
                Status = status,
                Patch = pagePatch,
                DisplayPatch = displayPatch,
                Complete = true,
                CurrentPatchVerified = !string.IsNullOrWhiteSpace(expectedPatch) && !string.IsNullOrWhiteSpace(pagePatch) && PatchesMatch(displayPatch, expectedPatch),
                Changes = changes,
                Summary = changes.Count == 0
                    ? "当前无英雄专属基础平衡修正"
                    : string.Join(" · ", changes.Select(item => item.Label + " " + item.Value)),
                ErrorClass = string.IsNullOrWhiteSpace(pagePatch) ? "patch_unverified" : string.Empty
            };
        }

        private static void ApplySnapshot(MayhemChampionResult result, AramBaseBalanceSnapshot snapshot)
        {
            snapshot = snapshot ?? Unavailable("missing_snapshot");
            result.BaseBalancePatch = snapshot.DisplayPatch;
            result.BaseBalanceStatus = snapshot.Status;
            result.BaseBalanceComplete = snapshot.Complete;

            string baseText;
            switch ((snapshot.Status ?? string.Empty).ToLowerInvariant())
            {
                case "ok":
                    baseText = "基础 ARAM（完整）：" + snapshot.Summary;
                    break;
                case "none":
                    baseText = "基础 ARAM（完整）：当前无英雄专属修正";
                    break;
                case "unverified":
                    baseText = "基础 ARAM（完整，版本未校验）：" + snapshot.Summary;
                    break;
                case "syncing":
                    baseText = "基础 ARAM：" + snapshot.Summary;
                    break;
                default:
                    baseText = "基础 ARAM：完整平衡暂不可用（不等于无修正）";
                    break;
            }

            result.BaseBalanceSummary = baseText;
            var mayhem = string.IsNullOrWhiteSpace(result.MayhemBalanceSummary)
                ? "Mayhem：当前未发现英雄专属修正"
                : "Mayhem：" + result.MayhemBalanceSummary;
            result.BalanceSummary = baseText + "\r\n" + mayhem;

            if (string.IsNullOrWhiteSpace(result.SourceNote))
                result.SourceNote = "基础平衡：OP.GG ARAM";
            else if (result.SourceNote.IndexOf("基础平衡：", StringComparison.OrdinalIgnoreCase) < 0)
                result.SourceNote += "；基础平衡：OP.GG ARAM";
        }

        private static bool CachedPatchIsUsable(AramBaseBalanceSnapshot snapshot, string expectedPatch)
        {
            if (snapshot == null || !snapshot.Complete) return false;
            if (string.IsNullOrWhiteSpace(expectedPatch)) return true;
            if (string.IsNullOrWhiteSpace(snapshot.DisplayPatch)) return false;
            return PatchesMatch(snapshot.DisplayPatch, expectedPatch);
        }

        private static AramBaseBalanceSnapshot Unavailable(
            string errorClass,
            string pagePatch = null,
            List<AramBaseBalanceChange> partial = null)
        {
            return new AramBaseBalanceSnapshot
            {
                Status = "unavailable",
                Patch = pagePatch,
                DisplayPatch = DisplayPatch(pagePatch),
                Complete = false,
                CurrentPatchVerified = false,
                Changes = partial ?? new List<AramBaseBalanceChange>(),
                Summary = string.Empty,
                ErrorClass = errorClass ?? string.Empty
            };
        }

        private static AramBaseBalanceSnapshot Clone(AramBaseBalanceSnapshot source)
        {
            return new AramBaseBalanceSnapshot
            {
                Status = source.Status,
                Patch = source.Patch,
                DisplayPatch = source.DisplayPatch,
                Complete = source.Complete,
                CurrentPatchVerified = source.CurrentPatchVerified,
                Summary = source.Summary,
                ErrorClass = source.ErrorClass,
                Changes = (source.Changes ?? new List<AramBaseBalanceChange>()).Select(item => new AramBaseBalanceChange
                {
                    Key = item.Key,
                    Label = item.Label,
                    Value = item.Value,
                    Direction = item.Direction
                }).ToList()
            };
        }

        private static string CleanVisibleText(string html)
        {
            var text = html ?? string.Empty;
            text = text.Replace("\\u003c", "<")
                .Replace("\\u003e", ">")
                .Replace("\\u0026", "&")
                .Replace("\\/", "/")
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Replace('−', '-');
            text = Regex.Replace(text, "<(script|style)\\b[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static string ExtractPatch(string text)
        {
            var match = Regex.Match(text ?? string.Empty, "\\bPatch\\s*:?\\s*(?<v>\\d{1,2}\\.\\d{1,2})\\b", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(text ?? string.Empty, "\\b(?<v>\\d{1,2}\\.\\d{1,2})\\s*版本\\b", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : null;
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
            var length = Math.Min(2200, text.Length - start);
            var section = text.Substring(start, length);
            var ends = EndMarkers
                .Select(marker => section.IndexOf(marker, 24, StringComparison.OrdinalIgnoreCase))
                .Where(index => index > 0)
                .ToArray();
            return ends.Length == 0 ? section : section.Substring(0, ends.Min());
        }

        private static string Direction(string key, string raw)
        {
            double numeric;
            if (!TryNumeric(raw, out numeric) || Math.Abs(numeric) < 0.0001) return "neutral";

            var signed = raw.StartsWith("+", StringComparison.Ordinal) || raw.StartsWith("-", StringComparison.Ordinal);
            var delta = signed ? numeric : (raw.EndsWith("%", StringComparison.Ordinal) ? numeric - 100D : numeric);
            if (Math.Abs(delta) < 0.0001) return "neutral";

            if (string.Equals(key, "damage_taken", StringComparison.OrdinalIgnoreCase))
                return delta > 0 ? "debuff" : "buff";
            return delta > 0 ? "buff" : "debuff";
        }

        private static bool TryNumeric(string raw, out double value)
        {
            value = 0D;
            var match = Regex.Match(raw ?? string.Empty, "[+-]?\\d+(?:\\.\\d+)?");
            return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string DisplayPatch(string patch)
        {
            if (string.IsNullOrWhiteSpace(patch)) return null;
            var parts = patch.Split('.');
            int major;
            int minor;
            if (parts.Length < 2 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor)) return patch;
            if (major >= 10 && major <= 19) major += 10;
            return major.ToString(CultureInfo.InvariantCulture) + "." + minor.ToString(CultureInfo.InvariantCulture);
        }

        private static bool PatchesMatch(string first, string second)
        {
            Version a;
            Version b;
            return Version.TryParse(DisplayPatch(first), out a) && Version.TryParse(DisplayPatch(second), out b) && a.Equals(b);
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.7,zh;q=0.6");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            return client;
        }
    }
}