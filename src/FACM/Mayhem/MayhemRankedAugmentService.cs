using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class MayhemRankedAugmentService
    {
        private const string KiwiIconBase = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/assets/ux/kiwi/augments/icons/";
        internal const int MinimumUsableRows = 3;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        private sealed class LegacyAugment
        {
            public string Name { get; set; }
            public string Slug { get; set; }
            public double? WinRate { get; set; }
            public double? PickRate { get; set; }
            public string Rarity { get; set; }
            public string IconUrl { get; set; }
        }

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null) return;

            var buildTask = MayhemBuildDetailsService.EnrichAsync(result, token);
            var slug = (result.ChampionSlug ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                var url = "https://op.gg/zh-cn/lol/modes/aram-mayhem/" + Uri.EscapeDataString(slug) + "/augments";
                try
                {
                    var response = await LeaguePublicDataTransport.GetAsync(
                        url, TimeSpan.FromSeconds(5.5), token, true).ConfigureAwait(false);
                    var html = response == null ? null : response.ReadUtf8();
                    var richRows = ParseOpggRows(html).Take(12).ToList();
                    var rows = richRows;
                    var renderedFallback = false;
                    if (rows.Count < MinimumUsableRows)
                    {
                        var rendered = ParseRenderedAugmentRows(html).Take(12).ToList();
                        if (rendered.Count > rows.Count)
                        {
                            rows = rendered;
                            renderedFallback = true;
                        }
                    }
                    if (rows.Count > 0)
                    {
                        ApplyRich(result, rows);
                        result.AugmentSourceUrl = url;
                        result.AugmentSourceRoute = response.Route;
                        result.AugmentSourceStale = response.IsStale;
                        AppLog.Info("Mayhem augments loaded: count=" + rows.Count +
                            "; mode=" + (renderedFallback ? "rendered-html" : "rich-json") +
                            "; route=" + response.Route + "; stale=" + response.IsStale);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                }
                catch (Exception exception)
                {
                    AppLog.Info("Mayhem rich augment source skipped: " + exception.GetType().Name);
                }
            }

            // Wait for the parallel OP.GG build attempt before changing shared result fields.
            await buildTask.ConfigureAwait(false);
            if (result.AugmentRows.Count >= 5 && MayhemBuildDetailsService.HasUsableCoreBuild(result))
                return;
            await ApplyLegacyFallbackAsync(result, token).ConfigureAwait(false);
        }

        internal static int ApplyFromHtmlForSmokeTest(MayhemChampionResult result, string html)
        {
            var rich = ParseOpggRows(html).Take(12).ToList();
            var preferred = rich;
            if (rich.Count < MinimumUsableRows)
            {
                var rendered = ParseRenderedAugmentRows(html).Take(12).ToList();
                if (rendered.Count > rich.Count) preferred = rendered;
            }
            if (preferred.Count > 0)
            {
                ApplyRich(result, preferred);
                return preferred.Count;
            }
            var legacy = ParseLegacyPicks(html).Take(5).ToList();
            ApplyLegacy(result, legacy);
            return legacy.Count;
        }

        internal static void ValidatePartialAugmentFallbackForSmokeTest()
        {
            var partial = new MayhemChampionResult
            {
                AugmentRows = new List<MayhemAugmentRow>
                {
                    new MayhemAugmentRow { Rank = 1, Name = "Existing", Rarity = "gold", WinRate = 52.5 }
                }
            };
            var insufficient = new List<LegacyAugment>
            {
                new LegacyAugment { Name = "Fallback", Slug = "fallback", WinRate = 53 }
            };
            ApplyLegacyIfRicher(partial, insufficient);
            if (partial.AugmentRows.Count != 1 || partial.AugmentRows[0].Name != "Existing")
                throw new InvalidOperationException("A weaker augment fallback displaced existing data.");
            insufficient.Add(new LegacyAugment { Name = "Fallback 2", Slug = "fallback-2", WinRate = 51 });
            ApplyLegacyIfRicher(partial, insufficient);
            if (partial.AugmentRows.Count != 2 || partial.AugmentRows[0].Name != "Fallback")
                throw new InvalidOperationException("A richer augment fallback was not accepted.");
        }

        private static bool ApplyLegacyIfRicher(MayhemChampionResult result, IList<LegacyAugment> picks)
        {
            if (result == null || picks == null || picks.Count <= result.AugmentRows.Count) return false;
            ApplyLegacy(result, picks);
            return true;
        }

        internal static IList<MayhemAugmentRow> ParseOpggRowsForSmokeTest(string html)
        {
            return ParseOpggRows(html).ToList();
        }

        internal static bool IsCompatibleFallbackPatch(MayhemChampionResult result, string html)
        {
            return result != null &&
                MayhemRankingSourceService.SamePatch(
                    MayhemRankingSourceService.ReadPatch(html), result.SelectedRankingPatch);
        }

        internal static IList<MayhemAugmentRow> ParseAramMayhemForSmokeTest(
            MayhemChampionResult result, string html)
        {
            if (!IsCompatibleFallbackPatch(result, html))
                return new List<MayhemAugmentRow>();
            ApplyLegacyIfRicher(result, ParseLegacyPicks(html).Take(12).ToList());
            return result.AugmentRows;
        }

        private static async Task ApplyLegacyFallbackAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(result.RankingSourceUrl)) return;
            try
            {
                var response = await LeaguePublicDataTransport.GetAsync(
                    result.RankingSourceUrl, TimeSpan.FromSeconds(3),
                    token, true).ConfigureAwait(false);
                var html = response == null ? null : response.ReadUtf8();
                if (!IsCompatibleFallbackPatch(result, html))
                {
                    AppLog.Info("Mayhem companion fallback skipped: patch mismatch or unknown; selected=" +
                        (result.SelectedRankingPatch ?? "unknown") +
                        "; detail=" + (MayhemRankingSourceService.ReadPatch(html) ?? "unknown"));
                    return;
                }
                var picks = ParseLegacyPicks(html).Take(12).ToList();
                var augmentsApplied = ApplyLegacyIfRicher(result, picks);
                if (augmentsApplied)
                {
                    result.AugmentSourceUrl = result.RankingSourceUrl;
                    result.AugmentSourceRoute = response.Route;
                    result.AugmentSourceStale = response.IsStale;
                }

                var buildApplied = MayhemBuildDetailsService.ApplyAramMayhemFallback(result, html);
                if (buildApplied > 0)
                {
                    result.BuildSourceRoute = response.Route;
                    result.BuildSourceStale = response.IsStale;
                    result.BuildSourceStatus = "fallback-aram-mayhem";
                }
                AppLog.Info("Mayhem companion fallback: source=ARAMMayhem; patch=" +
                    result.SelectedRankingPatch + "; augments=" + result.AugmentRows.Count +
                    "; builds=" + result.CoreBuilds.Count +
                    "; build_fields=" + buildApplied + "; route=" + response.Route +
                    "; stale=" + response.IsStale);
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
            }
            catch (Exception exception)
            {
                AppLog.Info("Mayhem companion fallback skipped: " + exception.GetType().Name);
            }
        }

        private static IEnumerable<MayhemAugmentRow> ParseOpggRows(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Enumerable.Empty<MayhemAugmentRow>();
            var normalized = NormalizeEscapedHtml(html);
            var best = new List<MayhemAugmentRow>();
            var bestScore = -1;
            var searchFrom = 0;
            while (searchFrom < normalized.Length)
            {
                var marker = FindNextArrayMarker(normalized, searchFrom);
                if (marker < 0) break;
                var open = normalized.IndexOf('[', marker);
                if (open < 0) break;
                var close = FindBalancedEnd(normalized, open, '[', ']');
                if (close <= open)
                {
                    searchFrom = open + 1;
                    continue;
                }
                var json = normalized.Substring(open, close - open + 1);
                var parsed = ParseArray(json);
                var score = ScoreRows(parsed);
                if (score > bestScore)
                {
                    best = parsed;
                    bestScore = score;
                }
                searchFrom = close + 1;
            }
            return best;
        }

        private static IEnumerable<MayhemAugmentRow> ParseRenderedAugmentRows(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Enumerable.Empty<MayhemAugmentRow>();
            var normalized = NormalizeEscapedHtml(html);
            var rows = new List<MayhemAugmentRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(
                normalized,
                "<img\\b[^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tag = match.Value;
                var icon = MatchAttribute(tag, "src");
                if (string.IsNullOrWhiteSpace(icon)) icon = MatchAttribute(tag, "data-src");
                if (string.IsNullOrWhiteSpace(icon) || icon.IndexOf("/aram-augment/", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var name = WebUtility.HtmlDecode(MatchAttribute(tag, "alt") ?? string.Empty).Trim();
                if (name.Length < 2 || !seen.Add(name)) continue;
                rows.Add(new MayhemAugmentRow
                {
                    Rank = rows.Count + 1,
                    Name = name,
                    Slug = Slugify(name),
                    Rarity = MayhemUiCopy.Unknown,
                    IconUrl = CleanIconUrl(icon)
                });
            }
            return rows;
        }

        private static int ScoreRows(IList<MayhemAugmentRow> rows)
        {
            if (rows == null || rows.Count == 0) return 0;
            var stats = rows.Count(row => row.WinRate.HasValue || row.PickRate.HasValue);
            var samples = rows.Count(row => row.Games.HasValue && row.Games.Value > 0);
            return rows.Count * 10 + stats * 4 + samples;
        }

        private static int FindNextArrayMarker(string text, int start)
        {
            var markers = new[] { "\"augments\"", "\"data\"", "\"items\"" };
            var result = -1;
            foreach (var marker in markers)
            {
                var index = text.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (result < 0 || index < result)) result = index;
            }
            return result;
        }

        private static List<MayhemAugmentRow> ParseArray(string json)
        {
            var rows = new List<MayhemAugmentRow>();
            try
            {
                var value = Json.DeserializeObject(json) as object[];
                if (value == null) return rows;
                var rank = 1;
                foreach (var item in value)
                {
                    var dictionary = item as IDictionary<string, object>;
                    if (dictionary == null) continue;
                    var row = ParseRow(dictionary, rank);
                    if (row == null) continue;
                    rows.Add(row);
                    rank++;
                }
            }
            catch { }
            return rows;
        }

        private static MayhemAugmentRow ParseRow(IDictionary<string, object> item, int fallbackRank)
        {
            var name = FirstText(item, "name", "augmentName", "title");
            if (string.IsNullOrWhiteSpace(name)) return null;
            var performance = FirstNumber(item, "performance", "winRate", "win_rate", "rate");
            var popular = FirstNumber(item, "popular", "pickRate", "pick_rate", "popularity");
            var id = FirstText(item, "id", "augmentId", "augment_id");
            var slug = FirstText(item, "slug", "key");
            var icon = FirstText(item, "largeIcon", "smallIcon", "icon", "iconUrl", "image", "imageUrl");

            // Rich OP.GG rows are only accepted when OP.GG itself supplied an icon. Generic arrays in
            // the Next.js payload can contain similarly named objects without usable assets; accepting
            // those was the reason the card intermittently lost all augment artwork.
            if (string.IsNullOrWhiteSpace(icon)) return null;

            var rarity = NormalizeRarity(FirstText(item, "rarity", "grade", "tier"));
            var description = CleanDescription(FirstText(item, "description", "desc", "tooltip"));
            var games = FirstInteger(item, "games", "gameCount", "sampleCount", "sampleSize", "totalGames", "count");
            var rank = FirstInteger(item, "rank", "order") ?? fallbackRank;
            if (string.IsNullOrWhiteSpace(slug)) slug = Slugify(name);
            return new MayhemAugmentRow
            {
                Id = id,
                Rank = Math.Max(1, rank),
                Name = WebUtility.HtmlDecode(name).Trim(),
                Slug = slug,
                Rarity = rarity,
                WinRate = NormalizePercent(performance),
                PickRate = NormalizePercent(popular),
                Games = games,
                Description = description,
                IconUrl = CleanIconUrl(icon)
            };
        }

        private static void ApplyRich(MayhemChampionResult result, IList<MayhemAugmentRow> rows)
        {
            if (result == null || rows == null || rows.Count == 0) return;
            result.AugmentRows = rows.Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name)).Take(12).ToList();
            result.Augments = result.AugmentRows.Take(5).Select(row => row.Name).ToList();
            result.AugmentIconUrls = result.AugmentRows.Take(5).Select(row => row.IconUrl).ToList();
            result.AugmentRoutes = BuildRoutes(result.AugmentRows);
        }

        private static List<MayhemDecisionRoute> BuildRoutes(IList<MayhemAugmentRow> rows)
        {
            var usable = rows.Where(row => row != null && (row.WinRate.HasValue || row.PickRate.HasValue)).ToList();
            var routes = new List<MayhemDecisionRoute>();
            if (usable.Count == 0) return routes;

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddRoute(routes, used, usable.OrderByDescending(row => Score(row, 0.72, 0.28)), MayhemUiCopy.StableRoute, MayhemUiCopy.StableRouteHint, 0);
            AddRoute(routes, used, usable.OrderByDescending(row => row.WinRate ?? -1), MayhemUiCopy.HighWinRoute, MayhemUiCopy.HighWinRouteHint, 1);
            AddRoute(routes, used, usable.OrderByDescending(row => row.PickRate ?? -1), MayhemUiCopy.PopularRoute, MayhemUiCopy.PopularRouteHint, 2);
            return routes;
        }

        private static void AddRoute(
            ICollection<MayhemDecisionRoute> routes,
            ISet<string> used,
            IEnumerable<MayhemAugmentRow> ordered,
            string title,
            string hint,
            int scoreKind)
        {
            foreach (var row in ordered)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name) || !used.Add(row.Name)) continue;
                var score = scoreKind == 0
                    ? Score(row, 0.72, 0.28)
                    : scoreKind == 1 ? row.WinRate ?? 0 : row.PickRate ?? 0;
                routes.Add(new MayhemDecisionRoute
                {
                    Title = title,
                    AugmentName = row.Name,
                    Hint = hint,
                    Score = score
                });
                return;
            }
        }

        private static double Score(MayhemAugmentRow row, double winWeight, double pickWeight)
        {
            return (row.WinRate ?? 0) * winWeight + (row.PickRate ?? 0) * pickWeight;
        }

        private static void ApplyLegacy(MayhemChampionResult result, IList<LegacyAugment> picks)
        {
            if (result == null || picks == null || picks.Count == 0) return;
            result.Augments.Clear();
            result.AugmentIconUrls.Clear();
            result.AugmentRows.Clear();
            for (var i = 0; i < picks.Count && i < 12; i++)
            {
                var pick = picks[i];
                if (i < 5)
                {
                    result.Augments.Add(pick.Name);
                    result.AugmentIconUrls.Add(pick.IconUrl);
                }
                result.AugmentRows.Add(new MayhemAugmentRow
                {
                    Rank = i + 1,
                    Name = pick.Name,
                    Slug = pick.Slug,
                    WinRate = pick.WinRate,
                    PickRate = pick.PickRate,
                    IconUrl = pick.IconUrl,
                    Rarity = pick.Rarity ?? MayhemUiCopy.Unknown
                });
            }
            result.AugmentRoutes = BuildRoutes(result.AugmentRows);
        }

        private static IEnumerable<LegacyAugment> ParseLegacyPicks(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Enumerable.Empty<LegacyAugment>();
            var normalized = NormalizeEscapedHtml(html);
            var start = normalized.IndexOf("Best Augments for", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return Enumerable.Empty<LegacyAugment>();
            var end = normalized.IndexOf("Augment Combos", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0 || end <= start) end = Math.Min(normalized.Length, start + 24000);
            var section = normalized.Substring(start, Math.Min(end - start, 24000));
            var picks = new List<LegacyAugment>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(section,
                @"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']*/augments/(?<slug>[^/""']+)/?)[""'][^>]*>(?<body>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var slug = WebUtility.HtmlDecode(match.Groups["slug"].Value).Trim();
                var body = match.Groups["body"].Value;
                var text = CleanText(body);
                var label = Regex.Match(text,
                    @"^(?<n>.+?)\s+(?:S\+|S|A|B|C)\s*(?:[·•]\s*)?#\d+",
                    RegexOptions.IgnoreCase);
                var name = label.Success ? label.Groups["n"].Value.Trim() : MatchAttribute(body, "alt");
                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Replace(slug.Replace('-', ' '), @"\s+", " ").Trim();
                name = WebUtility.HtmlDecode(name).Trim();
                if (name.Length < 2 || !seen.Add(name)) continue;

                var rateMatch = Regex.Match(text,
                    @"Win\s*rate\s*:\s*(?<w>\d{1,2}(?:\.\d+)?)%", RegexOptions.IgnoreCase);
                var pickMatch = Regex.Match(text,
                    @"Appearance\s*rate\s*:\s*(?<w>\d{1,2}(?:\.\d+)?)%", RegexOptions.IgnoreCase);
                double rate, pick;
                var prefix = section.Substring(0, match.Index);
                var prism = prefix.LastIndexOf("Prismatic Augments", StringComparison.OrdinalIgnoreCase);
                var gold = prefix.LastIndexOf("Gold Augments", StringComparison.OrdinalIgnoreCase);
                var silver = prefix.LastIndexOf("Silver Augments", StringComparison.OrdinalIgnoreCase);
                var rarity = prism >= gold && prism >= silver ? "prismatic"
                    : gold >= silver ? "gold" : "silver";
                picks.Add(new LegacyAugment
                {
                    Name = name,
                    Slug = slug,
                    Rarity = rarity,
                    WinRate = rateMatch.Success &&
                        double.TryParse(rateMatch.Groups["w"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out rate) ? rate : (double?)null,
                    PickRate = pickMatch.Success &&
                        double.TryParse(pickMatch.Groups["w"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out pick) ? pick : (double?)null,
                    IconUrl = KiwiIconBase + NormalizeName(slug) + "_small.png"
                });
            }
            return picks;
        }

        private static int FindBalancedEnd(string value, int start, char open, char close)
        {
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (var i = start; i < value.Length; i++)
            {
                var c = value[i];
                if (quoted)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == open) depth++;
                else if (c == close && --depth == 0) return i;
            }
            return -1;
        }

        private static string FirstText(IDictionary<string, object> item, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = item.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                var text = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return null;
        }

        private static double? FirstNumber(IDictionary<string, object> item, params string[] keys)
        {
            var text = FirstText(item, keys);
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim().TrimEnd('%');
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : (double?)null;
        }

        private static int? FirstInteger(IDictionary<string, object> item, params string[] keys)
        {
            var text = FirstText(item, keys);
            if (string.IsNullOrWhiteSpace(text)) return null;
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : (int?)null;
        }

        private static double? NormalizePercent(double? value)
        {
            if (!value.HasValue) return null;
            var number = value.Value;
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0) return null;
            if (number <= 1.00001) number *= 100.0;
            return number <= 100.00001 ? number : (double?)null;
        }

        private static string NormalizeRarity(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (text.Contains("prismatic") || text.Contains("prism") || text.Contains(MayhemUiCopy.Prism)) return MayhemUiCopy.Prism;
            if (text.Contains("gold") || text.Contains(MayhemUiCopy.Gold) || text.Contains("金")) return MayhemUiCopy.Gold;
            if (text.Contains("silver") || text.Contains(MayhemUiCopy.Silver) || text.Contains("银")) return MayhemUiCopy.Silver;
            return string.IsNullOrWhiteSpace(value) ? MayhemUiCopy.Unknown : value.Trim();
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = Regex.Replace(NormalizeEscapedHtml(value), "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }

        private static string CleanIconUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var clean = WebUtility.HtmlDecode(value).Trim().Replace("\\/", "/");
            var imageIndex = clean.IndexOf("?image=", StringComparison.OrdinalIgnoreCase);
            return imageIndex > 0 ? clean.Substring(0, imageIndex) : clean;
        }

        private static string Slugify(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Regex.Replace(WebUtility.HtmlDecode(value).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }

        private static string MatchAttribute(string html, string attribute)
        {
            var match = Regex.Match(
                html ?? string.Empty,
                "\\b" + Regex.Escape(attribute) + "\\s*=\\s*[\"'](?<v>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value).Trim() : null;
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var c in WebUtility.HtmlDecode(value).ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.ToString();
        }

        private static string NormalizeEscapedHtml(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\u003c", "<")
                .Replace("\\u003e", ">")
                .Replace("\\u0026", "&")
                .Replace("\\\"", "\"")
                .Replace("\\/", "/");
        }

        private static string CleanText(string html)
        {
            var text = Regex.Replace(html ?? string.Empty, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();
        }
    }
}
