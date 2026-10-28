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

    internal sealed class LeaguePlayerHistoryAnalysis
    {
        internal int Loaded, Wins, Losses, Remakes, Aborts, Unknowns, Other;
        internal int Streak;
        internal LeagueHistoryOutcome StreakOutcome;
        internal double? Kills, Deaths, Assists, CsPerMinute, DamageShare, GoldShare;
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
            result.Kills = Mean(played.Select(m => (double?)m.Kills));
            result.Deaths = Mean(played.Select(m => (double?)m.Deaths));
            result.Assists = Mean(played.Select(m => (double?)m.Assists));
            result.CsPerMinute = Mean(played.Where(m => m.GameDurationSeconds > 0)
                .Select(m => (double?)m.CreepScore * 60d / m.GameDurationSeconds));
            result.DamageShare = Mean(played.Select(m => m.ChampionDamageShare));
            result.GoldShare = Mean(played.Select(m => m.GoldShare));
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
