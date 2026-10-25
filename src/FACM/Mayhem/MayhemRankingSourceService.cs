using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace FACM.Mayhem
{
    internal sealed class MayhemRankingSnapshot
    {
        internal string Source { get; set; }
        internal string Patch { get; set; }
        internal List<MayhemTopChampion> TopTen { get; set; }
        // Optional full ranking, never synthesized from the ten summary rows.
        internal List<MayhemTopChampion> AllChampions { get; set; }
    }

    internal static class MayhemRankingSourceService
    {
        internal static string ReadPatch(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            var match = Regex.Match(html,
                @"(?:Patch|版本)\s*[:：]?\s*(?<v>(?:16|26)\.\d{1,2})",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(html,
                    @"(?<v>(?:16|26)\.\d{1,2})\s*版本",
                    RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["v"].Value : null;
        }

        internal static MayhemRankingSnapshot Select(params MayhemRankingSnapshot[] candidates)
        {
            return (candidates ?? new MayhemRankingSnapshot[0])
                .Where(IsComplete)
                .OrderByDescending(x => PatchOrder(x.Patch))
                .ThenBy(x => SourcePriority(x.Source))
                .FirstOrDefault();
        }

        internal static bool SamePatch(string first, string second)
        {
            return PatchOrder(first) > 0 && PatchOrder(first) == PatchOrder(second);
        }

        internal static bool IsComplete(MayhemRankingSnapshot snapshot)
        {
            if (snapshot == null || PatchOrder(snapshot.Patch) <= 0 ||
                snapshot.TopTen == null || snapshot.TopTen.Count != 10) return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < 10; index++)
            {
                var row = snapshot.TopTen[index];
                if (row == null || row.Rank != index + 1 ||
                    string.IsNullOrWhiteSpace(row.Name) ||
                    !row.WinRate.HasValue || row.WinRate.Value < 0 || row.WinRate.Value > 100 ||
                    !seen.Add(ChampionAliases.Normalize(row.Name)))
                    return false;
            }
            return true;
        }

        // An expanded list must belong to the same source/patch and agree with TOP10.
        // Invalid or partial lists are ignored instead of inventing champion strength.
        internal static bool IsExpandedValid(MayhemRankingSnapshot snapshot)
        {
            if (!IsComplete(snapshot) || snapshot.AllChampions == null ||
                snapshot.AllChampions.Count < 11 || snapshot.AllChampions.Count > 200) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < snapshot.AllChampions.Count; i++)
            {
                var row = snapshot.AllChampions[i];
                if (row == null || row.Rank != i + 1 || string.IsNullOrWhiteSpace(row.Name) ||
                    !row.WinRate.HasValue || row.WinRate.Value < 0 || row.WinRate.Value > 100)
                    return false;
                var identity = ChampionAliases.Normalize(
                    string.IsNullOrWhiteSpace(row.Slug) ? row.Name : row.Slug);
                if (identity.Length == 0 || !seen.Add(identity)) return false;
                if (i >= 10) continue;
                var top = snapshot.TopTen[i];
                var topIdentity = ChampionAliases.Normalize(
                    string.IsNullOrWhiteSpace(top.Slug) ? top.Name : top.Slug);
                if (topIdentity != identity || Math.Abs(top.WinRate.Value - row.WinRate.Value) > 0.01)
                    return false;
            }
            return true;
        }

        internal static MayhemTopChampion FindRank(MayhemRankingSnapshot snapshot, string name)
        {
            if (!IsComplete(snapshot) || string.IsNullOrWhiteSpace(name)) return null;
            var rows = IsExpandedValid(snapshot) ? snapshot.AllChampions : snapshot.TopTen;
            string slug;
            if (!ChampionAliases.TryResolve(name, out slug)) slug = ChampionAliases.Slugify(name);
            var normalizedSlug = ChampionAliases.Normalize(slug);
            var normalizedName = ChampionAliases.Normalize(name);
            return rows.FirstOrDefault(row => row != null &&
                (ChampionAliases.Normalize(row.Slug) == normalizedSlug ||
                 ChampionAliases.Normalize(row.Name) == normalizedName));
        }

        internal static MayhemRankingSnapshot ParseAramgg(string html)
        {
            var result = new MayhemRankingSnapshot { Source = "ARAMGG", Patch = ReadPatch(html),
                TopTen = new List<MayhemTopChampion>() };
            if (string.IsNullOrWhiteSpace(html)) return result;

            var tables = Regex.Matches(html, @"<table\b[^>]*>(?<body>.*?)</table>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match table in tables)
            {
                var header = CleanText(table.Groups["body"].Value);
                if (header.IndexOf("Win Rate", StringComparison.OrdinalIgnoreCase) < 0 ||
                    header.IndexOf("Champion", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var rows = new List<MayhemTopChampion>();
                foreach (Match row in Regex.Matches(table.Groups["body"].Value,
                    @"<tr\b[^>]*>(?<body>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    var cells = Regex.Matches(row.Groups["body"].Value,
                        @"<td\b[^>]*>(?<body>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (cells.Count < 4) continue;
                    int rank;
                    var rankText = CleanText(cells[0].Groups["body"].Value);
                    if (!int.TryParse(rankText, NumberStyles.Integer, CultureInfo.InvariantCulture, out rank) ||
                        rank != rows.Count + 1 || rank > 200) continue;
                    var nameHtml = cells[1].Groups["body"].Value;
                    var link = Regex.Match(nameHtml, @"<a\b[^>]*>(?<name>.*?)</a>",
                        RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    var name = CleanText(link.Success ? link.Groups["name"].Value : nameHtml);
                    var winText = CleanText(cells[2].Groups["body"].Value);
                    var win = Regex.Match(winText, @"(?<rate>\d{1,2}(?:\.\d+)?)\s*%");
                    double rate;
                    if (string.IsNullOrWhiteSpace(name) || !win.Success ||
                        !double.TryParse(win.Groups["rate"].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out rate)) continue;
                    var tier = CleanText(cells[3].Groups["body"].Value);
                    rows.Add(new MayhemTopChampion
                    {
                        Rank = rank, Name = name, Slug = ChampionAliases.Slugify(name),
                        WinRate = rate, Tier = tier
                    });
                    if (rows.Count == 200) break;
                }
                if (rows.Count > (result.AllChampions == null ? result.TopTen.Count : result.AllChampions.Count))
                {
                    result.TopTen = rows.Take(10).ToList();
                    result.AllChampions = rows.Count > 10 ? rows : null;
                }
                if (rows.Count == 200) break;
            }
            return result;
        }

        internal static void ValidateForSmokeTest()
        {
            var rows = new List<MayhemTopChampion>();
            var fixture = "<h1>Patch 26.20</h1><table><thead><tr><th>Rank</th>" +
                "<th>Champion</th><th>Win Rate</th><th>Tier</th></tr></thead><tbody>";
            for (var i = 1; i <= 17; i++)
            {
                var name = "Champion" + i;
                if (i <= 10)
                    rows.Add(new MayhemTopChampion { Rank = i, Name = name,
                        Slug = ChampionAliases.Slugify(name), WinRate = 51 + i, Tier = "S" });
                fixture += "<tr><td>" + i + "</td><td><a href='/en/champion-stats/" + i +
                    "'>" + name + "</a></td><td>" + (51 + i) + ".25%</td><td>S</td></tr>";
            }
            fixture += "</tbody></table>";
            var parsed = ParseAramgg(fixture);
            if (!IsComplete(parsed) || !IsExpandedValid(parsed) ||
                FindRank(parsed, "Champion16").Rank != 16 ||
                parsed.TopTen[9].Name != "Champion10" ||
                !SamePatch(parsed.Patch, "16.20"))
                throw new InvalidOperationException("ARAMGG ranking parser or patch aliases changed.");

            var older = new MayhemRankingSnapshot { Source = "Hexdata", Patch = "16.19", TopTen = rows };
            if (Select(older, parsed) != parsed || Select(older, parsed) == older)
                throw new InvalidOperationException("Older CN ranking displaced a newer complete snapshot.");

            var incomplete = new MayhemRankingSnapshot { Source = "Hexdata", Patch = "16.21",
                TopTen = rows.Take(9).ToList() };
            if (Select(incomplete, parsed) != parsed)
                throw new InvalidOperationException("Incomplete newer ranking passed source selection.");
            var fullRows = Enumerable.Range(1, 24).Select(i => new MayhemTopChampion {
                Rank = i, Name = "Hero" + i, Slug = "hero" + i,
                WinRate = 60 - (i * 0.1), Tier = i <= 10 ? "A" : null
            }).ToList();
            var expanded = new MayhemRankingSnapshot {
                Source = "ARAMGG", Patch = "26.21",
                TopTen = fullRows.Take(10).ToList(), AllChampions = fullRows
            };
            if (!IsExpandedValid(expanded) || FindRank(expanded, "Hero19").Rank != 19 ||
                FindRank(expanded, "not-an-actual-champion") != null)
                throw new InvalidOperationException("Complete roster projection failed.");
            expanded.AllChampions[11] = expanded.AllChampions[0];
            if (IsExpandedValid(expanded) || FindRank(expanded, "Hero19") != null)
                throw new InvalidOperationException("Mismatched full roster bypassed validation.");

            rows[9] = rows[0];
            if (IsComplete(older))
                throw new InvalidOperationException("Duplicate champion ranking was accepted.");
        }

        private static int PatchOrder(string patch)
        {
            Version version;
            if (!Version.TryParse(patch, out version) ||
                (version.Major != 16 && version.Major != 26) ||
                version.Minor < 0 || version.Minor > 30)
                return 0;
            return version.Minor + 1;
        }

        private static int SourcePriority(string name)
        {
            if (name == "Hexdata") return 0;
            if (name == "ARAMGG") return 1;
            if (name == "ARAMMayhem") return 2;
            return 3;
        }

        private static string CleanText(string html)
        {
            var stripped = Regex.Replace(html ?? string.Empty, @"<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(stripped), @"\s+", " ").Trim();
        }
    }
}
