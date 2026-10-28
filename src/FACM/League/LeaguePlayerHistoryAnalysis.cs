using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
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

        internal static LeaguePlayerHistoryAnalysis Calculate(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            var result = new LeaguePlayerHistoryAnalysis();
            if (matches == null) return result;
            var rows = matches.Where(m => m != null)
                .GroupBy(m => (m.SgpServerId ?? "") + ":" + m.GameId.ToString(CultureInfo.InvariantCulture),
                    StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(m => m.StatisticsAvailable && m.ParticipantResolved).First())
                .OrderByDescending(m => m.GameCreationLocal).ToList();
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
            result.Positions = played.Select(m => new { Match = m, Position = NormalizePosition(m.Position) })
                .Where(row => row.Position != null).GroupBy(row => row.Position, StringComparer.Ordinal)
                .Select(g => new LeagueHistoryPositionStat {
                    Position = g.Key, Games = g.Count(),
                    Wins = g.Count(row => Outcome(row.Match) == LeagueHistoryOutcome.Win),
                    Kills = g.Sum(row => row.Match.Kills), Deaths = g.Sum(row => row.Match.Deaths),
                    Assists = g.Sum(row => row.Match.Assists)
                }).OrderByDescending(s => s.Games).ThenBy(s => s.Position, StringComparer.Ordinal).ToList();
            var dated = played.Where(m => m.GameCreationLocal != DateTime.MinValue)
                .OrderByDescending(m => m.GameCreationLocal).ToList();
            if (dated.Count >= 10)
            {
                result.RecentFiveWinRate = dated.Take(5).Count(m => Outcome(m) == LeagueHistoryOutcome.Win) * 20d;
                result.PreviousFiveWinRate = dated.Skip(5).Take(5)
                    .Count(m => Outcome(m) == LeagueHistoryOutcome.Win) * 20d;
            }
            result.Champions = played.Where(m => m.ChampionId > 0).GroupBy(m => m.ChampionId)
                .Select(g => new LeaguePlayerChampionStat {
                    ChampionId = g.Key, ChampionName = g.Select(m => m.ChampionName)
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Games = g.Count(), Wins = g.Count(m => Outcome(m) == LeagueHistoryOutcome.Win),
                    Kills = g.Sum(m => m.Kills), Deaths = g.Sum(m => m.Deaths),
                    Assists = g.Sum(m => m.Assists)
                }).OrderByDescending(c => c.Games).ThenBy(c => c.ChampionId).Take(5).ToList();
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

        internal static void Show(Form owner, UiTextCatalog ui, IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            var a = Calculate(matches);
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
            if (a.AkariScore == null)
                lines.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariUnavailable));
            else
            {
                var score = a.AkariScore;
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisAkariTotal),
                    score.Total.ToString("0.00", CultureInfo.CurrentCulture), score.Games,
                    a.Wins + a.Losses, score.Extraordinary ?
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
            lines.Add(string.Empty);
            lines.Add(ui.Get(UiTextKeys.LeaguePlayerAnalysisPositions));
            foreach (var role in a.Positions)
            {
                var name = role.Position == "TOP" ? UiTextKeys.LeaguePlayerAnalysisPositionTop :
                    role.Position == "JUNGLE" ? UiTextKeys.LeaguePlayerAnalysisPositionJungle :
                    role.Position == "MIDDLE" ? UiTextKeys.LeaguePlayerAnalysisPositionMiddle :
                    role.Position == "BOTTOM" ? UiTextKeys.LeaguePlayerAnalysisPositionBottom :
                    UiTextKeys.LeaguePlayerAnalysisPositionUtility;
                lines.Add(string.Format(CultureInfo.CurrentCulture,
                    ui.Get(UiTextKeys.LeaguePlayerAnalysisPositionRow),
                    ui.Get(name), role.Games, role.WinRate.ToString("0", CultureInfo.CurrentCulture),
                    role.Kills, role.Deaths, role.Assists));
            }
            using (var dialog = new Form {
                Text = ui.Get(UiTextKeys.LeaguePlayerAnalysisAction),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                MinimumSize = new Size(420, 330), Size = new Size(520, 430),
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text
            })
            {
                dialog.Controls.Add(new TextBox {
                    Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                    BackColor = FacmDesignSystem.Surface, ForeColor = FacmDesignSystem.Text,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f),
                    Text = string.Join(Environment.NewLine, lines)
                });
                dialog.ShowDialog(owner);
            }
        }
    }
}
