using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FACM.League;
using FACM.Services;

namespace FACM.Mayhem
{
    internal static class MayhemBuildDetailsService
    {
        private const int MaximumCoreBuilds = 2;

        public static async Task EnrichAsync(MayhemChampionResult result, CancellationToken token)
        {
            if (result == null) return;
            if (HasDetailedBuild(result))
            {
                EnsureFallbackSkillPriority(result);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.SourceUrl))
            {
                try
                {
                    var response = await LeaguePublicDataTransport.GetAsync(
                        result.SourceUrl,
                        TimeSpan.FromSeconds(1.8),
                        token,
                        true).ConfigureAwait(false);
                    if (response != null)
                    {
                        ApplyHtml(result, response.ReadUtf8());
                        result.BuildSourceRoute = response.Route;
                        result.BuildSourceStale = response.IsStale;
                        result.BuildSourceStatus = HasDetailedBuild(result) ? "ok" : "empty";
                    }
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    result.BuildSourceStatus = "timeout";
                }
                catch (Exception exception)
                {
                    AppLog.Info("Mayhem compact build enrichment skipped: " + exception.GetType().Name);
                    result.BuildSourceStatus = "unavailable";
                }
            }

            ProjectLegacyBuild(result);
            EnsureFallbackSkillPriority(result);
        }

        internal static void ApplyHtmlForSmokeTest(MayhemChampionResult result, string html)
        {
            if (result == null) return;
            ApplyHtml(result, html);
            ProjectLegacyBuild(result);
            EnsureFallbackSkillPriority(result);
        }

        private static void ApplyHtml(MayhemChampionResult result, string html)
        {
            if (result == null || string.IsNullOrWhiteSpace(html)) return;
            var normalized = NormalizeEscapedHtml(html);

            var core = ExtractItemRows(normalized, "core_items", 3, 5).Take(MaximumCoreBuilds).ToList();
            var starterRows = ExtractItemRows(normalized, "starter_items", 2, 3);
            var bootRows = ExtractItemRows(normalized, "boots", 2, 1);
            var summoner = ExtractSummonerSpells(normalized);
            var skills = ExtractSkillPriority(normalized);

            if (core.Count > 0) result.CoreBuilds = core;
            if (starterRows.Count > 0) result.StarterItems = starterRows[0].Items.Take(3).ToList();
            if (bootRows.Count > 0) result.BootItems = bootRows[0].Items.Take(1).ToList();
            if (summoner.Count > 0) result.SummonerSpells = summoner.Take(2).ToList();
            if (skills.Count > 0) result.SkillPriority = skills.Take(3).ToList();

            if (result.CoreBuilds.Count > 0)
            {
                var first = result.CoreBuilds[0].Items.Take(5).ToList();
                result.CoreItems = first.Select(item => item.Name).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
                result.CoreItemIconUrls = first.Select(item => item.IconUrl).ToList();
            }
        }

        private static List<MayhemBuildPath> ExtractItemRows(string html, string prefix, int limit, int maxItems)
        {
            var output = new List<MayhemBuildPath>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return output;

            for (var index = 0; index < limit; index++)
            {
                var marker = prefix + "_" + index;
                var start = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0) continue;
                var nextMarker = prefix + "_" + (index + 1);
                var next = html.IndexOf(nextMarker, start + marker.Length, StringComparison.OrdinalIgnoreCase);
                var length = next > start ? next - start : Math.Min(9000, html.Length - start);
                if (length <= 0) continue;
                var segment = html.Substring(start, Math.Min(length, 9000));
                var items = ExtractJsonItems(segment, maxItems);
                if (items.Count == 0) items = ExtractHtmlItems(segment, maxItems, true);
                if (items.Count == 0) continue;

                var signature = string.Join("|", items.Select(item => FirstNonEmpty(item.Id, item.Name, item.IconUrl)));
                if (signature.Length == 0 || !seen.Add(signature)) continue;
                output.Add(new MayhemBuildPath { Rank = index + 1, Items = items });
            }
            return output;
        }

        private static List<MayhemBuildItem> ExtractJsonItems(string segment, int maxItems)
        {
            var output = new List<MayhemBuildItem>();
            foreach (Match match in Regex.Matches(
                segment ?? string.Empty,
                "\\\"metaId\\\"\\s*:\\s*(?<id>\\d+).*?\\\"src\\\"\\s*:\\s*\\\"(?<src>[^\\\"]+)\\\".*?\\\"alt\\\"\\s*:\\s*\\\"(?<name>[^\\\"]+)\\\"",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var item = BuildItem(match.Groups["id"].Value, match.Groups["name"].Value, match.Groups["src"].Value);
                if (!IsUsefulItem(item, true) || ContainsItem(output, item)) continue;
                output.Add(item);
                if (output.Count >= maxItems) break;
            }
            return output;
        }

        private static List<MayhemBuildItem> ExtractHtmlItems(string segment, int maxItems, bool requireItemUrl)
        {
            var output = new List<MayhemBuildItem>();
            CollectHtmlImages(output, segment, maxItems, requireItemUrl,
                "<img\\b[^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']+)[\\\"'][^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*>");
            if (output.Count < maxItems)
            {
                CollectHtmlImages(output, segment, maxItems, requireItemUrl,
                    "<img\\b[^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']+)[\\\"'][^>]*>");
            }
            return output;
        }

        private static void CollectHtmlImages(
            List<MayhemBuildItem> output,
            string segment,
            int maxItems,
            bool requireItemUrl,
            string pattern)
        {
            foreach (Match match in Regex.Matches(segment ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var item = BuildItem(null, match.Groups["name"].Value, match.Groups["src"].Value);
                if (!IsUsefulItem(item, requireItemUrl) || ContainsItem(output, item)) continue;
                output.Add(item);
                if (output.Count >= maxItems) return;
            }
        }

        private static List<MayhemBuildItem> ExtractSummonerSpells(string html)
        {
            var output = new List<MayhemBuildItem>();
            var segment = SegmentAfter(html, new[] { "SummonerSpells Table", "Summoner Spells", "召唤师技能" }, 7000);
            if (string.IsNullOrWhiteSpace(segment)) return output;

            foreach (var item in ExtractHtmlItems(segment, 12, false))
            {
                if (item.IconUrl.IndexOf("/spell/Summoner", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (ContainsItem(output, item)) continue;
                output.Add(item);
                if (output.Count >= 2) break;
            }
            return output;
        }

        private static List<MayhemSkillPriority> ExtractSkillPriority(string html)
        {
            var output = new List<MayhemSkillPriority>();
            var segment = SegmentAfter(html, new[] { "SkillOrder Table", "Skill Order", "技能加点" }, 19000);
            if (string.IsNullOrWhiteSpace(segment)) return output;

            AddSkillMatches(output, segment,
                "<img\\b[^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']*/spell/[^\\\"']+)[\\\"'][^>]*>.*?<strong[^>]*>\\s*(?<key>[QWER])\\s*</strong>");
            if (output.Count < 3)
            {
                AddSkillMatches(output, segment,
                    "<img\\b[^>]*src\\s*=\\s*[\\\"'](?<src>[^\\\"']*/spell/[^\\\"']+)[\\\"'][^>]*alt\\s*=\\s*[\\\"'](?<name>[^\\\"']+)[\\\"'][^>]*>.*?<strong[^>]*>\\s*(?<key>[QWER])\\s*</strong>");
            }
            return output.Take(3).ToList();
        }

        private static void AddSkillMatches(List<MayhemSkillPriority> output, string segment, string pattern)
        {
            foreach (Match match in Regex.Matches(segment ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var key = (match.Groups["key"].Value ?? string.Empty).Trim().ToUpperInvariant();
                if (key == "R" || (key != "Q" && key != "W" && key != "E")) continue;
                if (output.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
                output.Add(new MayhemSkillPriority
                {
                    Key = key,
                    Name = CleanValue(match.Groups["name"].Value),
                    IconUrl = CleanUrl(match.Groups["src"].Value)
                });
                if (output.Count >= 3) return;
            }
        }

        private static string SegmentAfter(string html, string[] markers, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            var start = -1;
            foreach (var marker in markers)
            {
                var index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && (start < 0 || index < start)) start = index;
            }
            if (start < 0) return null;
            return html.Substring(start, Math.Min(maxLength, html.Length - start));
        }

        private static MayhemBuildItem BuildItem(string id, string name, string url)
        {
            return new MayhemBuildItem
            {
                Id = CleanValue(id),
                Name = CleanValue(name),
                IconUrl = CleanUrl(url)
            };
        }

        private static bool IsUsefulItem(MayhemBuildItem item, bool requireItemUrl)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.IconUrl)) return false;
            if (requireItemUrl && item.IconUrl.IndexOf("item", StringComparison.OrdinalIgnoreCase) < 0) return false;
            var lower = item.Name.ToLowerInvariant();
            return !lower.Contains("logo") && !lower.Contains("advert") && !lower.Contains("op.gg");
        }

        private static bool ContainsItem(IEnumerable<MayhemBuildItem> values, MayhemBuildItem item)
        {
            return values.Any(value =>
                (!string.IsNullOrWhiteSpace(item.Id) && string.Equals(value.Id, item.Id, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(item.IconUrl) && string.Equals(value.IconUrl, item.IconUrl, StringComparison.OrdinalIgnoreCase)));
        }

        private static void ProjectLegacyBuild(MayhemChampionResult result)
        {
            if (result == null || result.CoreBuilds.Count > 0 || result.CoreItems.Count == 0) return;
            var items = new List<MayhemBuildItem>();
            for (var i = 0; i < result.CoreItems.Count && i < 5; i++)
            {
                items.Add(new MayhemBuildItem
                {
                    Name = result.CoreItems[i],
                    IconUrl = i < result.CoreItemIconUrls.Count ? result.CoreItemIconUrls[i] : null
                });
            }
            if (items.Count > 0) result.CoreBuilds.Add(new MayhemBuildPath { Rank = 1, Items = items });
        }

        private static void EnsureFallbackSkillPriority(MayhemChampionResult result)
        {
            if (result == null || result.SkillPriority.Count >= 3 || string.IsNullOrWhiteSpace(result.SkillOrder)) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(result.SkillOrder.ToUpperInvariant(), "[QWER]"))
            {
                var key = match.Value;
                if (key == "R" || !seen.Add(key)) continue;
                string icon;
                result.SkillIconUrls.TryGetValue(key, out icon);
                result.SkillPriority.Add(new MayhemSkillPriority { Key = key, Name = key, IconUrl = icon });
                if (result.SkillPriority.Count >= 3) break;
            }
        }

        private static bool HasDetailedBuild(MayhemChampionResult result)
        {
            return result.CoreBuilds.Count > 0 || result.StarterItems.Count > 0 || result.BootItems.Count > 0 ||
                   result.SummonerSpells.Count > 0 || result.SkillPriority.Count > 0;
        }

        private static string CleanValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : WebUtility.HtmlDecode(value).Trim();
        }

        private static string CleanUrl(string value)
        {
            var clean = CleanValue(value);
            if (string.IsNullOrWhiteSpace(clean)) return null;
            clean = clean.Replace("\\/", "/");
            var imageIndex = clean.IndexOf("?image=", StringComparison.OrdinalIgnoreCase);
            return imageIndex > 0 ? clean.Substring(0, imageIndex) : clean;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
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
    }
}
