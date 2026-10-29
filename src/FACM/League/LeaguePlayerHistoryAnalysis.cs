using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal enum LeagueHistoryOutcome { Unknown, Win, Loss, Remake, Abort }

    internal sealed class LeagueHistoryPositionStat
    {
        internal string Position;
        internal int Games, Wins, Kills, Deaths, Assists;
        internal double WinRate { get { return Games > 0 ? Wins * 100d / Games : 0d; } }
    }

    internal sealed class LeagueHistoryChampionBreakdown
    {
        internal int NormalGames, NormalWins, CherryGames, CherryWins, UnknownModeGames;
        internal int CherryFirstPlaces, CherryKnownPlacements, CherryPlacementSum;
        internal int CherryTopHalfFinishes, CherryKnownTopHalfGames;
        internal double? CsPerMinute, DamageShare, DamageTakenShare, GoldShare, KillParticipation;
        internal List<LeagueHistoryPositionStat> Positions = new List<LeagueHistoryPositionStat>();
        internal double? AverageCherryPlacement { get { return CherryKnownPlacements > 0
            ? (double?)CherryPlacementSum / CherryKnownPlacements : null; } }
    }


    // Pinned Akari 1.5.1 aggregate scoring: no score unless all nine components
    // have evidence from the selected participant and a complete team roster.
    internal sealed class LeagueHistoryAkariScore
    {
        internal int Games;
        internal double Kda, WinRate, Damage, DamageTaken, Healing, Cs, Gold, Participation, Vision;
        internal double Total { get { return Kda + WinRate + Damage + DamageTaken + Healing +
            Cs + Gold + Participation + Vision; } }
        internal bool Outstanding { get { return Games >= 5 && Total >= 6.5; } }
        internal bool Extraordinary { get { return Games >= 8 && Total >= 8; } }

        private static double Linear(double value, double min, double max, double weight)
        {
            return Math.Max(0d, Math.Min(1d, (value - min) / (max - min))) * weight;
        }

        private static double Contribution(double personal, double team, int teamCount)
        {
            return teamCount <= 1 ? 0d : personal / Math.Max(1d, team) * teamCount;
        }

        private static bool Valid(double? value)
        {
            return value.HasValue && value.Value >= 0d &&
                !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        internal static LeagueHistoryAkariScore Calculate(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            var included = new List<LeaguePlayerMatchSummary>();
            var sums = new double[7];
            foreach (var match in matches ?? Enumerable.Empty<LeaguePlayerMatchSummary>())
            {
                if (LeaguePlayerHistoryAnalysis.Outcome(match) != LeagueHistoryOutcome.Win &&
                    LeaguePlayerHistoryAnalysis.Outcome(match) != LeagueHistoryOutcome.Loss)
                    continue;
                if (match.ParticipantId <= 0 || match.GameDurationSeconds <= 0 ||
                    match.ParticipantRoster == null || match.ParticipantRoster.Length == 0) continue;
                var roster = match.ParticipantRoster;
                var hasSubteam = match.PlayerSubteamId.HasValue && match.PlayerSubteamId.Value > 0;
                if (!hasSubteam && (match.TeamId <= 0 || roster.Any(p => p != null &&
                    p.PlayerSubteamId.HasValue && p.PlayerSubteamId.Value > 0))) continue;
                var self = roster.FirstOrDefault(p => p != null && p.ParticipantId == match.ParticipantId);
                if (self == null || (hasSubteam ? self.PlayerSubteamId != match.PlayerSubteamId :
                    self.TeamId != match.TeamId) || roster.Count(p => p != null &&
                    p.ParticipantId == match.ParticipantId) != 1) continue;
                var members = roster.Where(p => p != null && (hasSubteam ?
                    p.PlayerSubteamId == match.PlayerSubteamId : p.TeamId == match.TeamId)).ToList();
                if (members.Count == 0 || members.Count > 5 ||
                    !members.Contains(self) || members.Any(p => !p.StatisticsAvailable ||
                    !Valid(p.DamageDealtToChampions) || !Valid(p.DamageTaken) ||
                    !Valid(p.GoldEarned) || !Valid(p.VisionScore)) ||
                    !Valid(self.TotalHeal))
                    continue;
                double damage = members.Sum(p => p.DamageDealtToChampions.Value);
                double taken = members.Sum(p => p.DamageTaken.Value);
                double gold = members.Sum(p => p.GoldEarned.Value);
                double vision = members.Sum(p => p.VisionScore.Value);
                var n = members.Count;
                var participation = (self.Kills + self.Assists) /
                    (double)Math.Max(1, members.Sum(p => p.Kills));
                var csPerMinute = match.CreepScore * 60d / match.GameDurationSeconds;
                var healing = self.TotalHeal.Value / Math.Max(1d, taken / n);
                var individual = new[]
                {
                    Linear(Contribution(self.DamageDealtToChampions.Value, damage, n), 1d, 2d, 3d),
                    Linear(Contribution(self.DamageTaken.Value, taken, n), 1d, 2d, 2d),
                    Linear(healing, .2d, n == 1 ? 1d : 1.4d, 2d),
                    Linear(csPerMinute, 5d, 10d, 2d),
                    Linear(Contribution(self.GoldEarned.Value, gold, n), 1d, 1.5d, 2d),
                    Linear(participation, .3d, 1d, 2d),
                    Linear(Contribution(self.VisionScore.Value, vision, n), 1d, 2d, 2d)
                };
                for (int i = 0; i < sums.Length; i++) sums[i] += individual[i];
                included.Add(match);
            }
            if (included.Count == 0) return null;
            var score = new LeagueHistoryAkariScore { Games = included.Count };
            var count = (double)included.Count;
            score.Damage = sums[0] / count;
            score.DamageTaken = sums[1] / count;
            score.Healing = sums[2] / count;
            score.Cs = sums[3] / count;
            score.Gold = sums[4] / count;
            score.Participation = sums[5] / count;
            score.Vision = sums[6] / count;
            double kda = (included.Sum(p => p.Kills) + included.Sum(p => p.Assists)) /
                (double)Math.Max(1, included.Sum(p => p.Deaths));
            score.Kda = Math.Min(1d, Math.Sqrt(Math.Max(0d, kda - 2d)) * 3d / 7d);
            score.WinRate = Linear(included.Count(p =>
                LeaguePlayerHistoryAnalysis.Outcome(p) == LeagueHistoryOutcome.Win) / count, .5d, 1d, 1d);
            return score;
        }
    }

    internal sealed class LeaguePlayerHistoryAnalysis
    {
        internal int Loaded, Wins, Losses, Remakes, Aborts, Unknowns, Other;
        internal int Streak;
        internal LeagueHistoryOutcome StreakOutcome;
        internal double? Kills, Deaths, Assists, CsPerMinute, DamageShare, GoldShare;
        internal double? KillParticipation, DamageTakenShare;
        internal double? RecentFiveWinRate, PreviousFiveWinRate;
        internal LeagueHistoryAkariScore AkariScore;
        internal List<LeagueHistoryPositionStat> Positions = new List<LeagueHistoryPositionStat>();
        internal List<LeaguePlayerChampionStat> Champions = new List<LeaguePlayerChampionStat>();
        internal List<LeaguePlayerChampionStat> AllChampions = new List<LeaguePlayerChampionStat>();
        internal Dictionary<int, LeagueHistoryAkariScore> ChampionScores =
            new Dictionary<int, LeagueHistoryAkariScore>();
        internal Dictionary<int, LeagueHistoryChampionBreakdown> ChampionBreakdowns =
            new Dictionary<int, LeagueHistoryChampionBreakdown>();

        internal static LeagueHistoryOutcome Outcome(LeaguePlayerMatchSummary m)
        {
            if (m == null || !m.ParticipantResolved)
                return LeagueHistoryOutcome.Unknown;
            var result = (m.WinResult ?? "").Trim().ToLowerInvariant();
            if (result == "remake") return LeagueHistoryOutcome.Remake;
            if (result == "abort") return LeagueHistoryOutcome.Abort;
            if (!m.StatisticsAvailable) return LeagueHistoryOutcome.Unknown;
            if (result == "win") return LeagueHistoryOutcome.Win;
            if (result == "loss") return LeagueHistoryOutcome.Loss;
            return LeagueHistoryOutcome.Unknown;
        }

        internal static List<LeaguePlayerMatchSummary> LoadedMatches(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            if (matches == null) return new List<LeaguePlayerMatchSummary>();
            return matches.Where(m => m != null)
                .GroupBy(m => (m.SgpServerId ?? "") + ":" + m.GameId.ToString(CultureInfo.InvariantCulture),
                    StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(m => m.StatisticsAvailable && m.ParticipantResolved).First())
                .OrderByDescending(m => m.GameCreationLocal).ToList();
        }

        internal static LeaguePlayerHistoryAnalysis Calculate(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            var result = new LeaguePlayerHistoryAnalysis();
            if (matches == null) return result;
            var rows = LoadedMatches(matches);
            result.Loaded = rows.Count;
            var played = new List<LeaguePlayerMatchSummary>();
            foreach (var m in rows)
            {
                if (!string.IsNullOrWhiteSpace(m.GameType) &&
                    !string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase))
                {
                    result.Other++;
                    continue;
                }
                switch (Outcome(m))
                {
                    case LeagueHistoryOutcome.Win: result.Wins++; played.Add(m); break;
                    case LeagueHistoryOutcome.Loss: result.Losses++; played.Add(m); break;
                    case LeagueHistoryOutcome.Remake: result.Remakes++; break;
                    case LeagueHistoryOutcome.Abort: result.Aborts++; break;
                    default: result.Unknowns++; break;
                }
            }
            result.AkariScore = LeagueHistoryAkariScore.Calculate(played);
            result.Kills = Mean(played.Select(m => (double?)m.Kills));
            result.Deaths = Mean(played.Select(m => (double?)m.Deaths));
            result.Assists = Mean(played.Select(m => (double?)m.Assists));
            result.CsPerMinute = Mean(played.Where(m => m.GameDurationSeconds > 0)
                .Select(m => (double?)m.CreepScore * 60d / m.GameDurationSeconds));
            result.DamageShare = Mean(played.Select(m => m.ChampionDamageShare));
            result.GoldShare = Mean(played.Select(m => m.GoldShare));
            result.KillParticipation = Mean(played.Select(m => m.KillParticipation));
            result.DamageTakenShare = Mean(played.Select(m => m.DamageTakenShare));
            result.Positions = BuildPositions(played);
            var dated = played.Where(m => m.GameCreationLocal != DateTime.MinValue)
                .OrderByDescending(m => m.GameCreationLocal).ToList();
            if (dated.Count >= 10)
            {
                result.RecentFiveWinRate = dated.Take(5).Count(m => Outcome(m) == LeagueHistoryOutcome.Win) * 20d;
                result.PreviousFiveWinRate = dated.Skip(5).Take(5)
                    .Count(m => Outcome(m) == LeagueHistoryOutcome.Win) * 20d;
            }
            var championGroups = played.Where(m => m.ChampionId > 0).GroupBy(m => m.ChampionId).ToList();
            foreach (var group in championGroups)
            {
                result.ChampionScores[group.Key] = LeagueHistoryAkariScore.Calculate(group);
                var breakdown = new LeagueHistoryChampionBreakdown {
                    Positions = BuildPositions(group),
                    CsPerMinute = Mean(group.Where(m => m.GameDurationSeconds > 0)
                        .Select(m => (double?)m.CreepScore * 60d / m.GameDurationSeconds)),
                    DamageShare = Mean(group.Select(m => m.ChampionDamageShare)),
                    DamageTakenShare = Mean(group.Select(m => m.DamageTakenShare)),
                    GoldShare = Mean(group.Select(m => m.GoldShare)),
                    KillParticipation = Mean(group.Select(m => m.KillParticipation))
                };
                foreach (var match in group)
                {
                    if (string.IsNullOrWhiteSpace(match.GameMode)) breakdown.UnknownModeGames++;
                    else if (string.Equals(match.GameMode, "CHERRY", StringComparison.OrdinalIgnoreCase))
                    {
                        breakdown.CherryGames++;
                        if (Outcome(match) == LeagueHistoryOutcome.Win) breakdown.CherryWins++;
                        var placement = match.SubteamPlacement.GetValueOrDefault();
                        if (placement > 0)
                        {
                            breakdown.CherryKnownPlacements++;
                            breakdown.CherryPlacementSum += placement;
                            if (placement == 1) breakdown.CherryFirstPlaces++;
                            var teamCount = ReliableCherryTeamCount(match);
                            if (teamCount.HasValue)
                            {
                                breakdown.CherryKnownTopHalfGames++;
                                if (placement <= teamCount.Value / 2) breakdown.CherryTopHalfFinishes++;
                            }
                        }
                    }
                    else
                    {
                        breakdown.NormalGames++;
                        if (Outcome(match) == LeagueHistoryOutcome.Win) breakdown.NormalWins++;
                    }
                }
                result.ChampionBreakdowns[group.Key] = breakdown;
            }
            result.AllChampions = championGroups.Select(g => new LeaguePlayerChampionStat {
                    ChampionId = g.Key, ChampionName = g.Select(m => m.ChampionName)
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Games = g.Count(), Wins = g.Count(m => Outcome(m) == LeagueHistoryOutcome.Win),
                    Kills = g.Sum(m => m.Kills), Deaths = g.Sum(m => m.Deaths),
                    Assists = g.Sum(m => m.Assists)
                }).OrderByDescending(c => c.Games).ThenBy(c => c.ChampionId).ToList();
            result.Champions = result.AllChampions.Take(5).ToList();
            foreach (var m in rows)
            {
                if (m.GameCreationLocal == DateTime.MinValue) break;
                var outcome = Outcome(m);
                if (outcome != LeagueHistoryOutcome.Win && outcome != LeagueHistoryOutcome.Loss) break;
                if (result.Streak == 0) result.StreakOutcome = outcome;
                if (result.StreakOutcome != outcome) break;
                result.Streak++;
            }
            return result;
        }

        private static List<LeagueHistoryPositionStat> BuildPositions(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            return matches.Select(m => new { Match = m, Position = NormalizePosition(m.Position) })
                .Where(row => row.Position != null).GroupBy(row => row.Position, StringComparer.Ordinal)
                .Select(g => new LeagueHistoryPositionStat {
                    Position = g.Key, Games = g.Count(),
                    Wins = g.Count(row => Outcome(row.Match) == LeagueHistoryOutcome.Win),
                    Kills = g.Sum(row => row.Match.Kills), Deaths = g.Sum(row => row.Match.Deaths),
                    Assists = g.Sum(row => row.Match.Assists)
                }).OrderByDescending(s => s.Games).ThenBy(s => s.Position, StringComparer.Ordinal).ToList();
        }

        private static int? ReliableCherryTeamCount(LeaguePlayerMatchSummary match)
        {
            var roster = match.ParticipantRoster;
            if (!match.RosterIdentitiesComplete || roster == null || roster.Length < 4 ||
                roster.Any(p => p == null || !p.PlayerSubteamId.HasValue || p.PlayerSubteamId.Value <= 0 ||
                    !p.SubteamPlacement.HasValue || p.SubteamPlacement.Value <= 0)) return null;
            var groups = roster.GroupBy(p => p.PlayerSubteamId.Value).ToList();
            if (groups.Count < 2 || !groups.Any(g => g.Key == match.PlayerSubteamId) ||
                groups.Any(g => g.Select(p => p.SubteamPlacement.Value).Distinct().Count() != 1)) return null;
            var self = roster.Where(p => p.ParticipantId == match.ParticipantId).ToList();
            if (self.Count != 1 || self[0].PlayerSubteamId != match.PlayerSubteamId ||
                self[0].SubteamPlacement != match.SubteamPlacement) return null;
            return groups.Count;
        }

        private static string NormalizePosition(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "TOP": return "TOP";
                case "JUNGLE": return "JUNGLE";
                case "MIDDLE":
                case "MID": return "MIDDLE";
                case "BOTTOM":
                case "BOT": return "BOTTOM";
                case "UTILITY":
                case "SUPPORT": return "UTILITY";
                default: return null;
            }
        }

        private static double? Mean(IEnumerable<double?> values)
        {
            var known = values.Where(v => v.HasValue && !double.IsNaN(v.Value) &&
                !double.IsInfinity(v.Value)).Select(v => v.Value).ToList();
            return known.Count == 0 ? (double?)null : known.Average();
        }

        internal static string OutcomeText(UiTextCatalog ui, LeaguePlayerMatchSummary match)
        {
            switch (Outcome(match))
            {
                case LeagueHistoryOutcome.Win: return ui.Get(UiTextKeys.LeaguePlayerWin);
                case LeagueHistoryOutcome.Loss: return ui.Get(UiTextKeys.LeaguePlayerLoss);
                case LeagueHistoryOutcome.Remake: return ui.Get(UiTextKeys.LeaguePlayerRemake);
                case LeagueHistoryOutcome.Abort: return ui.Get(UiTextKeys.LeaguePlayerAbort);
                default: return ui.Get(UiTextKeys.LeaguePlayerUnknown);
            }
        }

        private static string Number(double? v, bool fraction = false)
        {
            return v.HasValue ? (v.Value * (fraction ? 100d : 1d))
                .ToString("0.0", CultureInfo.CurrentCulture) + (fraction ? "%" : "") : "--";
        }

        private static string PositionName(UiTextCatalog ui, string position)
        {
            var key = position == "TOP" ? UiTextKeys.LeaguePlayerAnalysisPositionTop :
                position == "JUNGLE" ? UiTextKeys.LeaguePlayerAnalysisPositionJungle :
                position == "MIDDLE" ? UiTextKeys.LeaguePlayerAnalysisPositionMiddle :
                position == "BOTTOM" ? UiTextKeys.LeaguePlayerAnalysisPositionBottom :
                UiTextKeys.LeaguePlayerAnalysisPositionUtility;
            return ui.Get(key);
        }

        private static void AppendRating(List<string> lines, UiTextCatalog ui,
            LeagueHistoryAkariScore score, int eligibleGames)
        {
            if (score == null)
            {
                lines.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariUnavailable));
                return;
            }
            lines.Add(string.Format(CultureInfo.CurrentCulture,
                ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariTotal),
                score.Total.ToString("0.00", CultureInfo.CurrentCulture), score.Games,
                eligibleGames, score.Extraordinary ?
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariExtraordinary) :
                    score.Outstanding ? ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariOutstanding) :
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariNormal)));
            var keys = new[] { UiTextKeys.LeaguePlayerAnalysisAkariKda,
                UiTextKeys.LeaguePlayerAnalysisAkariWinRate, UiTextKeys.LeaguePlayerAnalysisAkariDamage,
                UiTextKeys.LeaguePlayerAnalysisAkariDamageTaken, UiTextKeys.LeaguePlayerAnalysisAkariHealing,
                UiTextKeys.LeaguePlayerAnalysisAkariCs, UiTextKeys.LeaguePlayerAnalysisAkariGold,
                UiTextKeys.LeaguePlayerAnalysisAkariParticipation,
                UiTextKeys.LeaguePlayerAnalysisAkariVision };
            var values = new[] { score.Kda, score.WinRate, score.Damage,
                score.DamageTaken, score.Healing, score.Cs, score.Gold,
                score.Participation, score.Vision };
            var maxima = new[] { 1, 1, 3, 2, 2, 2, 2, 2, 2 };
            for (var i = 0; i < keys.Length; i++)
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariPart),
                    ui.Get(keys[i]), values[i].ToString("0.00", CultureInfo.CurrentCulture), maxima[i]));
        }

        private static List<string> ReportLines(UiTextCatalog ui, LeaguePlayerHistoryAnalysis a)
        {
            var lines = new List<string> {
                ui.Get(UiTextKeys.LeaguePlayerAnalysisScope), "",
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisTotals),
                    a.Loaded, a.Wins, a.Losses, a.Remakes, a.Aborts, a.Unknowns, a.Other),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisWinRate),
                    a.Wins + a.Losses == 0 ? "--" :
                    (100d * a.Wins / (a.Wins + a.Losses)).ToString("0.0", CultureInfo.CurrentCulture) + "%"),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisKda),
                    Number(a.Kills), Number(a.Deaths), Number(a.Assists)),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisCs),
                    Number(a.CsPerMinute)),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisShares),
                    Number(a.DamageShare, true), Number(a.GoldShare, true)),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisExtraShares),
                    Number(a.KillParticipation, true), Number(a.DamageTakenShare, true)),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisRecentCompare),
                    Number(a.RecentFiveWinRate.HasValue ? a.RecentFiveWinRate / 100d : null, true),
                    Number(a.PreviousFiveWinRate.HasValue ? a.PreviousFiveWinRate / 100d : null, true)),
                string.Format(CultureInfo.CurrentCulture, ui.Get(UiTextKeys.LeaguePlayerAnalysisStreak),
                    a.Streak == 0 ? "--" :
                    ui.Get(a.StreakOutcome == LeagueHistoryOutcome.Win ? UiTextKeys.LeaguePlayerWin :
                        UiTextKeys.LeaguePlayerLoss) + " × " + a.Streak),
                "", ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions)
            };
            foreach (var c in a.Champions)
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisChampionRow),
                    string.IsNullOrWhiteSpace(c.ChampionName) ? "#" + c.ChampionId : c.ChampionName,
                    c.Games, c.WinRate.ToString("0", CultureInfo.CurrentCulture)));
            lines.Add(string.Empty);
            lines.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariTitle));
            AppendRating(lines, ui, a.AkariScore, a.Wins + a.Losses);
            lines.Add(string.Empty);
            lines.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisPositions));
            foreach (var role in a.Positions)
            {
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisPositionRow),
                    PositionName(ui, role.Position), role.Games, role.WinRate.ToString("0", CultureInfo.CurrentCulture),
                    role.Kills, role.Deaths, role.Assists));
            }
            return lines;
        }

        internal static async Task ShowAsync(Form owner, UiTextCatalog ui,
            IEnumerable<LeaguePlayerMatchSummary> matches, LeaguePlayerDataService service,
            string selfPuuid, string targetPuuid, LeagueClientSession session, CancellationToken parentToken)
        {
            var snapshot = (matches ?? Enumerable.Empty<LeaguePlayerMatchSummary>()).Take(1000).ToList();
            var a = Calculate(snapshot);
            var lines = ReportLines(ui, a);
            var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var jungleAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var jungleSamples = new List<LeagueJungleHistorySample>();
            Task pending = Task.CompletedTask;
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken))
            using (var dialog = new Form {
                Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisAction),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimumSize = new Size(420, 330), Size = new Size(520, 430),
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text
            })
            {
                var tabs = new TabControl { Dock = DockStyle.Fill };
                var reportTab = new TabPage(ui.Get(UiTextKeys.LeaguePlayerAnalysisAction));
                var comparisonTab = new TabPage(ui.Get(UiTextKeys.LeaguePlayerCompareTitle));
                var jungleTab = new TabPage(ui.Get(UiTextKeys.LeagueJungleHistoryTitle));
                var comparison = new LeagueHistoryChampionComparisonView(ui);
                comparisonTab.Controls.Add(comparison);
                tabs.TabPages.Add(reportTab);
                tabs.TabPages.Add(comparisonTab);
                tabs.TabPages.Add(jungleTab);
                dialog.Controls.Add(tabs);
                comparison.RefreshData(snapshot);
                var jungleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
                jungleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
                jungleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                var jungleSelector = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text };
                var jungleReport = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f) };
                var jungleViews = new TabControl { Dock = DockStyle.Fill };
                var jungleTextPage = new TabPage(ui.Get(UiTextKeys.LeagueJungleHistoryReportTab));
                var jungleMapPage = new TabPage(ui.Get(UiTextKeys.LeagueJungleMapTitle));
                jungleTextPage.Controls.Add(jungleReport);
                jungleViews.TabPages.Add(jungleTextPage);
                jungleViews.TabPages.Add(jungleMapPage);
                var mapLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
                mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
                mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                var mapStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = FacmDesignSystem.Text };
                var mapHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3,
                    RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
                mapHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                mapHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
                mapHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var mapSideSelector = new ComboBox { Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                    AccessibleName = ui.Get(UiTextKeys.LeagueJungleMapSideFilter) };
                mapSideSelector.Items.Add(ui.Get(UiTextKeys.LeagueJungleMapAllSides));
                mapSideSelector.Items.Add(ui.Get(UiTextKeys.LeagueJungleBlueSide));
                mapSideSelector.Items.Add(ui.Get(UiTextKeys.LeagueJungleRedSide));
                mapSideSelector.SelectedIndex = 0;
                var heatmapToggle = new CheckBox { AutoSize = true, Dock = DockStyle.Fill,
                    Text = ui.Get(UiTextKeys.LeagueJungleMapHeatmapToggle),
                    AccessibleName = ui.Get(UiTextKeys.LeagueJungleMapHeatmapToggle) };
                mapHeader.Controls.Add(mapStatus, 0, 0);
                mapHeader.Controls.Add(mapSideSelector, 1, 0);
                mapHeader.Controls.Add(heatmapToggle, 2, 0);
                mapLayout.Controls.Add(mapHeader, 0, 0);
                jungleMapPage.Controls.Add(mapLayout);
                LeagueEventMap mapView = null;
                Action refreshJungle = () =>
                {
                    var selected = jungleSelector.SelectedIndex <= 0 ? 0 :
                        ((KeyValuePair<int, string>)jungleSelector.SelectedItem).Key;
                    var coverage = LeagueJungleHistoryAnalysis.Coverage(snapshot, targetPuuid,
                        jungleAttempted, selected);
                    jungleReport.Text = LeagueJungleHistoryAnalysis.Report(ui, jungleSamples,
                        coverage.Item1, coverage.Item2, selected);
                    if (mapView != null)
                    {
                        var side = mapSideSelector.SelectedIndex == 1 ? 100 :
                            mapSideSelector.SelectedIndex == 2 ? 200 : 0;
                        var mapSamples = LeagueJungleHistoryAnalysis.MapSamples(jungleSamples, selected, side);
                        var projection = LeagueJungleHistoryAggregate.Calculate(mapSamples);
                        mapView.SetPointCloud(projection?.MinutePositions, projection?.GankPositions,
                            projection?.Level3KillPositions, projection?.Level4KillPositions,
                            projection?.MinuteLanes, projection?.GankLanes);
                        mapStatus.Text = string.Format(CultureInfo.CurrentCulture,
                            ui.Get(UiTextKeys.LeagueJungleMapCoverage),
                            projection?.MinutePositions.Count ?? 0, projection?.GankPositions.Count ?? 0,
                            projection?.Level3KillPositions.Count ?? 0, projection?.Level4KillPositions.Count ?? 0,
                            (projection?.OmittedGankMapPoints ?? 0) + (projection?.OmittedEarlyMapPoints ?? 0));
                    }
                };
                heatmapToggle.CheckedChanged += (sender, args) => mapView?.SetHeatmapMode(heatmapToggle.Checked);
                mapSideSelector.SelectedIndexChanged += (sender, args) => refreshJungle();
                jungleSelector.SelectedIndexChanged += (sender, args) => refreshJungle();
                jungleViews.SelectedIndexChanged += (sender, args) =>
                {
                    if (jungleViews.SelectedTab != jungleMapPage || mapView != null || dialog.IsDisposed) return;
                    mapView = new LeagueEventMap(ui, 11);
                    mapView.SetHeatmapMode(heatmapToggle.Checked);
                    mapLayout.Controls.Add(mapView, 0, 1);
                    refreshJungle();
                };
                jungleLayout.Controls.Add(jungleSelector, 0, 0);
                jungleLayout.Controls.Add(jungleViews, 0, 1);
                jungleTab.Controls.Add(jungleLayout);
                Action rebuildJungleSelector = () =>
                {
                    var previous = jungleSelector.SelectedIndex > 0 ?
                        ((KeyValuePair<int, string>)jungleSelector.SelectedItem).Key : 0;
                    jungleSelector.Items.Clear();
                    jungleSelector.Items.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroAll));
                    var selectedIndex = 0;
                    foreach (var group in LeagueJungleHistoryAnalysis.Candidates(snapshot, targetPuuid)
                        .GroupBy(s => s.ChampionId).OrderBy(g => g.Key))
                    {
                        var match = group.First();
                        jungleSelector.Items.Add(new KeyValuePair<int, string>(group.Key,
                            (string.IsNullOrWhiteSpace(match.ChampionName) ? "#" + group.Key : match.ChampionName) +
                            " (#" + group.Key.ToString(CultureInfo.InvariantCulture) + ")"));
                        if (group.Key == previous) selectedIndex = jungleSelector.Items.Count - 1;
                    }
                    jungleSelector.SelectedIndex = selectedIndex;
                };
                jungleSelector.Format += (sender, args) =>
                {
                    if (args.ListItem is KeyValuePair<int, string>)
                        args.Value = ((KeyValuePair<int, string>)args.ListItem).Value;
                };
                jungleSelector.FormattingEnabled = true;
                rebuildJungleSelector();
                var report = new TextBox {
                    Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f),
                    Text = string.Join(Environment.NewLine, lines)
                };
                {
                    var layout = new TableLayoutPanel {
                        Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                        Margin = Padding.Empty, Padding = Padding.Empty
                    };
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
                    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                    var header = new Panel { Dock = DockStyle.Fill,
                        BackColor = FacmDesignSystem.Canvas, Padding = new Padding(8, 6, 8, 4) };
                    var selector = new ComboBox {
                        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                        BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                        AccessibleName = ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroSelect)
                    };
                    Action rebuildSelector = () =>
                    {
                        selector.Items.Clear();
                        selector.Items.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroAll));
                        foreach (var champion in a.AllChampions)
                            selector.Items.Add((string.IsNullOrWhiteSpace(champion.ChampionName) ?
                                "#" + champion.ChampionId : champion.ChampionName) +
                                " (#" + champion.ChampionId.ToString(CultureInfo.InvariantCulture) + ")");
                        selector.SelectedIndex = 0;
                    };
                    selector.SelectedIndexChanged += (sender, args) =>
                    {
                        var updated = new List<string>(lines);
                        if (selector.SelectedIndex > 0 && selector.SelectedIndex <= a.AllChampions.Count)
                        {
                            var champion = a.AllChampions[selector.SelectedIndex - 1];
                            updated.Add(string.Empty);
                            updated.Add(string.Format(CultureInfo.CurrentCulture,
                                ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroRating),
                                string.IsNullOrWhiteSpace(champion.ChampionName) ?
                                    "#" + champion.ChampionId : champion.ChampionName,
                                champion.Games, champion.WinRate.ToString("0", CultureInfo.CurrentCulture)));
                            LeagueHistoryAkariScore score;
                            a.ChampionScores.TryGetValue(champion.ChampionId, out score);
                            AppendRating(updated, ui, score, champion.Games);
                            LeagueHistoryChampionBreakdown breakdown;
                            if (a.ChampionBreakdowns.TryGetValue(champion.ChampionId, out breakdown))
                            {
                                updated.Add(string.Empty);
                                updated.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroBreakdown));
                                if (breakdown.NormalGames > 0)
                                    updated.Add(string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroModeRow),
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroNormal),
                                        breakdown.NormalGames, breakdown.NormalWins));
                                if (breakdown.CherryGames > 0)
                                {
                                    updated.Add(string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroModeRow),
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroCherry),
                                        breakdown.CherryGames, breakdown.CherryWins));
                                    updated.Add(string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroCherryPlacement),
                                        breakdown.CherryKnownPlacements == 0 ? "--" :
                                            breakdown.CherryFirstPlaces + "/" + breakdown.CherryKnownPlacements,
                                        breakdown.CherryKnownTopHalfGames == 0 ? "--" :
                                            breakdown.CherryTopHalfFinishes + "/" + breakdown.CherryKnownTopHalfGames,
                                        Number(breakdown.AverageCherryPlacement)));
                                }
                                if (breakdown.UnknownModeGames > 0)
                                    updated.Add(string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroUnknownMode),
                                        breakdown.UnknownModeGames));
                                updated.Add(string.Format(CultureInfo.CurrentCulture,
                                    ui.Get(UiTextKeys.LeaguePlayerAnalysisCs), Number(breakdown.CsPerMinute)));
                                updated.Add(string.Format(CultureInfo.CurrentCulture,
                                    ui.Get(UiTextKeys.LeaguePlayerAnalysisShares),
                                    Number(breakdown.DamageShare, true), Number(breakdown.GoldShare, true)));
                                updated.Add(string.Format(CultureInfo.CurrentCulture,
                                    ui.Get(UiTextKeys.LeaguePlayerAnalysisExtraShares),
                                    Number(breakdown.KillParticipation, true), Number(breakdown.DamageTakenShare, true)));
                                foreach (var role in breakdown.Positions)
                                    updated.Add(string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisPositionRow),
                                        PositionName(ui, role.Position), role.Games,
                                        role.WinRate.ToString("0", CultureInfo.CurrentCulture),
                                        role.Kills, role.Deaths, role.Assists));
                            }
                        }
                        report.Text = string.Join(Environment.NewLine, updated);
                        report.SelectionStart = 0;
                        report.ScrollToCaret();
                    };
                    header.Controls.Add(selector);
                    var label = new Label {
                        Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroSelect),
                        Dock = DockStyle.Left, Width = 105, AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft, ForeColor = FacmDesignSystem.Text
                    };
                    header.Controls.Add(label);
                    layout.Controls.Add(header, 0, 0);
                    layout.Controls.Add(report, 0, 1);
                    reportTab.Controls.Add(layout);
                    rebuildSelector();
                    var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
                        Padding = new Padding(6), WrapContents = true };
                    var hydrate = new Button { AutoSize = true,
                        Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrate) };
                    var loadJungle = new Button { AutoSize = true,
                        Text = ui.Get(UiTextKeys.LeagueJungleHistoryLoad) };
                    var stop = new Button { AutoSize = true, Enabled = false,
                        Text = ui.Get(UiTextKeys.LeaguePlayerCollectStop) };
                    var status = new Label { AutoSize = true, MaximumSize = new Size(460, 0),
                        Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateScope) };
                    CancellationTokenSource operation = null;
                    bool stale = false;
                    Action refreshAction = () =>
                    {
                        hydrate.Enabled = !stale && service != null && operation == null &&
                            snapshot.Any(m => LeaguePlayerDataService.NeedsAnalysisDetail(m) &&
                                !attempted.Contains(LeaguePlayerDataService.HistoryDetailKey(m)));
                        loadJungle.Enabled = !stale && service != null && session != null && operation == null &&
                            LeagueJungleHistoryAnalysis.PendingBatch(snapshot, targetPuuid,
                                jungleAttempted, jungleSamples.Count).Count > 0;
                    };
                    Func<Task> hydrateAsync = async () =>
                    {
                        hydrate.Enabled = false; loadJungle.Enabled = false;
                        stop.Enabled = true;
                        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        status.Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrating);
                        try
                        {
                            var result = await service.HydrateAnalysisAsync(selfPuuid, targetPuuid,
                                session, snapshot, attempted, operation.Token);
                            if (dialog.IsDisposed || lifetime.IsCancellationRequested) return;
                            attempted.UnionWith(result.Attempted);
                            snapshot = result.Matches;
                            stale = result.AccountChanged;
                            a = Calculate(snapshot);
                            comparison.RefreshData(snapshot);
                            lines = ReportLines(ui, a);
                            rebuildSelector();
                            rebuildJungleSelector();
                            status.Text = stale ? ui.Get(UiTextKeys.LeaguePlayerRemoteAccountChanged) :
                                string.Format(CultureInfo.CurrentCulture,
                                    ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrated),
                                    result.Updated, result.Attempted.Count,
                                    result.TimedOut ? ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateTimeout) : "");
                        }
                        catch (OperationCanceledException)
                        {
                            if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                status.Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateStopped);
                        }
                        catch (Exception)
                        {
                            if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                status.Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateFailed);
                        }
                        finally
                        {
                            operation.Dispose();
                            operation = null;
                            if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                            { stop.Enabled = false; refreshAction(); }
                        }
                    };
                    Func<Task> jungleAsync = async () =>
                    {
                        hydrate.Enabled = false; loadJungle.Enabled = false;
                        stop.Enabled = true;
                        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        status.Text = ui.Get(UiTextKeys.LeagueJungleHistoryLoading);
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(48));
                            try
                            {
                                foreach (var match in LeagueJungleHistoryAnalysis.PendingBatch(snapshot,
                                    targetPuuid, jungleAttempted, jungleSamples.Count))
                                {
                                    if (jungleSamples.Count >= LeagueJungleHistoryAnalysis.MaximumSamples) break;
                                    deadline.Token.ThrowIfCancellationRequested();
                                    var timeline = await service.LoadMatchTimelineAsync(selfPuuid, match,
                                        deadline.Token, session);
                                    if (dialog.IsDisposed || lifetime.IsCancellationRequested) return;
                                    deadline.Token.ThrowIfCancellationRequested();
                                    jungleAttempted.Add(LeaguePlayerDataService.HistoryDetailKey(match));
                                    var analysis = LeagueJungleAnalysis.Calculate(timeline, match);
                                    if (analysis != null)
                                        jungleSamples.Add(new LeagueJungleHistorySample { Match = match, Analysis = analysis });
                                }
                                if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                    status.Text = string.Format(CultureInfo.CurrentCulture,
                                        ui.Get(UiTextKeys.LeagueJungleHistoryDone),
                                        jungleSamples.Count, jungleAttempted.Count);
                            }
                            catch (OperationCanceledException)
                            {
                                if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                    status.Text = deadline.IsCancellationRequested && !operation.IsCancellationRequested ?
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateTimeout) :
                                        ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateStopped);
                            }
                            catch (Exception)
                            {
                                if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                    status.Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisHydrateFailed);
                            }
                            finally
                            {
                                if (!dialog.IsDisposed && !lifetime.IsCancellationRequested) rebuildJungleSelector();
                                operation.Dispose();
                                operation = null;
                                if (!dialog.IsDisposed && !lifetime.IsCancellationRequested)
                                { stop.Enabled = false; refreshAction(); }
                            }
                        }
                    };
                    hydrate.Click += (sender, args) => { if (operation == null) pending = hydrateAsync(); };
                    loadJungle.Click += (sender, args) =>
                    {
                        if (operation != null) return;
                        tabs.SelectedTab = jungleTab;
                        pending = jungleAsync();
                    };
                    stop.Click += (sender, args) => operation?.Cancel();
                    dialog.FormClosed += (sender, args) => lifetime.Cancel();
                    actions.Controls.Add(hydrate);
                    actions.Controls.Add(loadJungle);
                    actions.Controls.Add(stop);
                    actions.Controls.Add(status);
                    dialog.Controls.Add(actions);
                    actions.BringToFront();
                    refreshAction();
                }
                dialog.ShowDialog(owner);
                lifetime.Cancel();
                await pending;
            }
        }
    }
}
