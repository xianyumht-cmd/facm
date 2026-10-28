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
    internal sealed class LeagueHistoryComparisonValue
    {
        internal string Key;
        internal double? Value;
        internal int Known, Total;
        internal bool Percent;
    }

    internal static class LeagueHistoryChampionComparison
    {
        private static bool Known(double? value)
        {
            return value.HasValue && value.Value >= 0 && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        internal static double? Cs(LeaguePlayerMatchSummary match)
        {
            return match.MetricBaselines != null && match.MetricBaselines.ContainsKey("cs")
                ? (double?)match.CreepScore : null;
        }

        internal static double? Relative(LeaguePlayerMatchSummary match,
            Func<LeaguePlayerMatchSummary, double?> read, bool team, bool maximum)
        {
            var personal = read(match);
            var roster = match.ParticipantRoster;
            if (!Known(personal) || roster == null || roster.Length == 0 ||
                roster.Any(p => p == null || p.ParticipantId <= 0) ||
                roster.Select(p => p.ParticipantId).Distinct().Count() != roster.Length ||
                roster.Count(p => p.ParticipantId == match.ParticipantId) != 1) return null;
            var cherry = LeagueCherryTeams.IsCherry(match.GameMode);
            var group = LeagueCherryTeams.GroupId(match, cherry);
            if (team && (group <= 0 || roster.Any(p => LeagueCherryTeams.GroupId(p, cherry) <= 0))) return null;
            var members = roster.Where(p => !team || LeagueCherryTeams.GroupId(p, cherry) == group).ToArray();
            var values = members.Select(read).ToArray();
            if (values.Length == 0 || values.Any(v => !Known(v))) return null;
            var denominator = maximum ? values.Max(v => v.Value) : values.Sum(v => v.Value);
            if (!Known(denominator)) return null;
            return personal.Value / (denominator == 0 ? 1 : denominator);
        }

        // Source-pinned coefficient of variation: population deviation of per-game KDA divided by its mean.
        internal static double? KdaVariation(IReadOnlyList<LeaguePlayerMatchSummary> matches)
        {
            if (matches == null || matches.Count == 0) return null;
            var values = matches.Select(m => (m.Kills + (double)m.Assists) / Math.Max(1, m.Deaths)).ToArray();
            var mean = values.Average();
            if (mean == 0) return null;
            return Math.Sqrt(values.Average(v => (v - mean) * (v - mean))) / mean;
        }

        internal static double? KillDamageEfficiency(LeaguePlayerMatchSummary match)
        {
            if (match == null || !match.StatisticsAvailable || match.Kills < 0 ||
                !Known(match.DamageDealtToChampions)) return null;
            var roster = match.ParticipantRoster;
            if (roster == null || roster.Length == 0 ||
                roster.Any(p => p == null || p.ParticipantId <= 0) ||
                roster.Select(p => p.ParticipantId).Distinct().Count() != roster.Length ||
                roster.Count(p => p.ParticipantId == match.ParticipantId) != 1) return null;
            var cherry = LeagueCherryTeams.IsCherry(match.GameMode);
            var group = LeagueCherryTeams.GroupId(match, cherry);
            if (group <= 0 || roster.Any(p => LeagueCherryTeams.GroupId(p, cherry) <= 0)) return null;
            var team = roster.Where(p => LeagueCherryTeams.GroupId(p, cherry) == group).ToArray();
            if (team.Length == 0 || team.Any(p => !p.StatisticsAvailable || p.Kills < 0 ||
                !Known(p.DamageDealtToChampions))) return null;
            var teamKills = team.Sum(p => (double)p.Kills);
            var teamDamage = team.Sum(p => p.DamageDealtToChampions.Value);
            if (teamKills == 0 || teamDamage == 0) return 1d;
            if (match.DamageDealtToChampions.Value == 0) return null;
            var efficiency = (match.Kills / teamKills) / (match.DamageDealtToChampions.Value / teamDamage);
            return Known(efficiency) ? efficiency : (double?)null;
        }

        internal static List<LeagueHistoryComparisonValue> Calculate(
            IEnumerable<LeaguePlayerMatchSummary> matches, int championId)
        {
            var rows = LeaguePlayerHistoryAnalysis.LoadedMatches(matches).Where(m =>
                (championId == 0 || m.ChampionId == championId) &&
                (string.IsNullOrWhiteSpace(m.GameType) || string.Equals(m.GameType, "MATCHED_GAME", StringComparison.OrdinalIgnoreCase)) &&
                (LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ||
                 LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss)).ToList();
            var result = new List<LeagueHistoryComparisonValue>();
            Action<string, Func<LeaguePlayerMatchSummary, double?>, bool, bool> add = (key, read, percent, all) =>
            {
                var values = rows.Select(read).Where(Known).Select(v => v.Value).ToList();
                result.Add(new LeagueHistoryComparisonValue { Key = key, Percent = percent,
                    Known = values.Count, Total = rows.Count,
                    Value = values.Count == 0 || (all && values.Count != rows.Count)
                        ? (double?)null : values.Average() });
            };
            var score = LeagueHistoryAkariScore.Calculate(rows);
            result.Add(new LeagueHistoryComparisonValue { Key = UiTextKeys.LeaguePlayerCompareScore,
                Value = score?.Total, Known = score?.Games ?? 0, Total = rows.Count });
            result.Add(new LeagueHistoryComparisonValue { Key = UiTextKeys.LeaguePlayerCompareKda,
                Value = rows.Count == 0 ? (double?)null :
                    rows.Sum(m => (double)m.Kills + m.Assists) / Math.Max(1d, rows.Sum(m => (double)m.Deaths)),
                Known = rows.Count, Total = rows.Count });
            result.Add(new LeagueHistoryComparisonValue { Key = UiTextKeys.LeaguePlayerCompareKdaVariation,
                Value = KdaVariation(rows), Known = rows.Count, Total = rows.Count });
            add(UiTextKeys.LeaguePlayerCompareWinRate, m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win ? 1d : 0d, true, false);
            add(UiTextKeys.LeaguePlayerCompareParticipation, m => m.KillParticipation, true, false);
            add(UiTextKeys.LeaguePlayerCompareVision, m => m.VisionScore, false, false);
            add(UiTextKeys.LeaguePlayerCompareVisionShare, m => Relative(m, p => p.VisionScore, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareDamageMinute, m => m.GameDurationSeconds > 0
                ? m.DamageDealtToChampions * 60d / m.GameDurationSeconds : null, false, false);
            add(UiTextKeys.LeaguePlayerCompareDamageShare, m => Relative(m, p => p.DamageDealtToChampions, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareDamageTeamMax, m => Relative(m, p => p.DamageDealtToChampions, true, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareDamageGameMax, m => Relative(m, p => p.DamageDealtToChampions, false, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareTakenShare, m => Relative(m, p => p.DamageTaken, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareTakenTeamMax, m => Relative(m, p => p.DamageTaken, true, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareTakenGameMax, m => Relative(m, p => p.DamageTaken, false, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareDamageGold, m => m.DamageGoldEfficiency, true, false);
            add(UiTextKeys.LeaguePlayerCompareKillDamageEfficiency, KillDamageEfficiency, false, false);
            add(UiTextKeys.LeaguePlayerCompareSoloKills, m => m.SoloKills, false, true);
            add(UiTextKeys.LeaguePlayerComparePings, m => m.TotalPings, false, true);
            add(UiTextKeys.LeaguePlayerCompareMissingPings, m => m.EnemyMissingPings, false, true);
            add(UiTextKeys.LeaguePlayerCompareGoldShare, m => Relative(m, p => p.GoldEarned, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareGoldTeamMax, m => Relative(m, p => p.GoldEarned, true, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareGoldGameMax, m => Relative(m, p => p.GoldEarned, false, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareCsMinute, m => m.GameDurationSeconds > 0
                ? Cs(m) * 60d / m.GameDurationSeconds : null, false, false);
            add(UiTextKeys.LeaguePlayerCompareCsShare, m => Relative(m, Cs, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareCsTeamMax, m => Relative(m, Cs, true, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareCsGameMax, m => Relative(m, Cs, false, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareTowerShare, m => Relative(m, p => p.DamageToTowers, true, false), true, false);
            add(UiTextKeys.LeaguePlayerCompareTowerTeamMax, m => Relative(m, p => p.DamageToTowers, true, true), true, false);
            add(UiTextKeys.LeaguePlayerCompareTowerGameMax, m => Relative(m, p => p.DamageToTowers, false, true), true, false);
            return result;
        }

        internal static string Format(LeagueHistoryComparisonValue value)
        {
            return (value.Value.HasValue ? (value.Value.Value * (value.Percent ? 100d : 1d))
                .ToString("0.00", CultureInfo.CurrentCulture) + (value.Percent ? "%" : "") : "--") +
                " (" + value.Known + "/" + value.Total + ")";
        }
    }

    internal sealed class LeagueHistoryChampionComparisonView : UserControl
    {
        private readonly UiTextCatalog _ui;
        private readonly ComboBox _left, _right;
        private readonly ListView _values;
        private List<LeaguePlayerMatchSummary> _matches = new List<LeaguePlayerMatchSummary>();
        private bool _rebuilding;

        private sealed class Choice
        {
            internal int Id;
            internal string Name;
            public override string ToString() { return Name; }
        }

        internal LeagueHistoryChampionComparisonView(UiTextCatalog ui)
        {
            _ui = ui;
            Dock = DockStyle.Fill;
            BackColor = FacmDesignSystem.Canvas;
            _left = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = ui.Get(UiTextKeys.LeaguePlayerCompareLeft) };
            _right = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = ui.Get(UiTextKeys.LeaguePlayerCompareRight) };
            var selectors = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 2,
                Padding = new Padding(4) };
            selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            selectors.Controls.Add(_left, 0, 0); selectors.Controls.Add(_right, 1, 0);
            var hint = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4),
                Text = ui.Get(UiTextKeys.LeaguePlayerCompareScope), ForeColor = FacmDesignSystem.Text };
            _values = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, BackColor = FacmDesignSystem.Surface,
                ForeColor = FacmDesignSystem.Text };
            _values.Columns.Add(ui.Get(UiTextKeys.LeaguePlayerCompareMetric), 180);
            _values.Columns.Add(ui.Get(UiTextKeys.LeaguePlayerCompareLeft), 145);
            _values.Columns.Add(ui.Get(UiTextKeys.LeaguePlayerCompareRight), 145);
            Controls.Add(_values); Controls.Add(selectors); Controls.Add(hint);
            _left.SelectedIndexChanged += (sender, args) => RenderRows();
            _right.SelectedIndexChanged += (sender, args) => RenderRows();
        }

        internal void RefreshData(IEnumerable<LeaguePlayerMatchSummary> matches)
        {
            var leftId = (_left.SelectedItem as Choice)?.Id ?? 0;
            var rightId = (_right.SelectedItem as Choice)?.Id ?? -1;
            _matches = LeaguePlayerHistoryAnalysis.LoadedMatches(matches);
            var analysis = LeaguePlayerHistoryAnalysis.Calculate(_matches);
            var choices = new List<Choice> { new Choice { Id = 0,
                Name = _ui.Get(UiTextKeys.LeaguePlayerAnalysisHeroAll) } };
            choices.AddRange(analysis.AllChampions.Select(c => new Choice { Id = c.ChampionId,
                Name = (string.IsNullOrWhiteSpace(c.ChampionName) ? "#" + c.ChampionId : c.ChampionName) +
                    " (#" + c.ChampionId.ToString(CultureInfo.InvariantCulture) + ")" }));
            _rebuilding = true;
            try
            {
                _left.Items.Clear(); _right.Items.Clear();
                foreach (var choice in choices) { _left.Items.Add(choice); _right.Items.Add(choice); }
                _left.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Id == leftId));
                var right = choices.FindIndex(c => c.Id == rightId);
                _right.SelectedIndex = right < 0 ? Math.Min(1, choices.Count - 1) : right;
            }
            finally { _rebuilding = false; }
            RenderRows();
        }

        private void RenderRows()
        {
            if (_rebuilding || !(_left.SelectedItem is Choice left) || !(_right.SelectedItem is Choice right)) return;
            var first = LeagueHistoryChampionComparison.Calculate(_matches, left.Id);
            var second = LeagueHistoryChampionComparison.Calculate(_matches, right.Id);
            _values.BeginUpdate();
            try
            {
                _values.Items.Clear();
                _values.Columns[1].Text = left.Name; _values.Columns[2].Text = right.Name;
                for (var i = 0; i < first.Count; i++)
                    _values.Items.Add(new ListViewItem(new[] { _ui.Get(first[i].Key),
                        LeagueHistoryChampionComparison.Format(first[i]), LeagueHistoryChampionComparison.Format(second[i]) }));
            }
            finally { _values.EndUpdate(); }
        }
    }
}
