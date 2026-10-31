using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Theming;

namespace FACM.League
{
    // EOG is a partial read-only fallback, not a fabricated SUMMARY scoreboard.
    internal sealed class LeagueSgpEogOnlyDialog : Form
    {
        internal LeagueSgpEogOnlyDialog(UiTextCatalog ui, LeagueEncounterMatchDetailResult detail)
        {
            if (detail == null || detail.EogOnly == null || detail.Match == null ||
                detail.EogOnly.GameId != detail.Match.GameId ||
                detail.EogOnly.Players == null || detail.EogOnly.Players.Count == 0)
                throw new ArgumentException("Verified EOG details are required.", nameof(detail));

            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = "SGP EOG";
            ClientSize = new Size(1040, 570);
            MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var mode = string.IsNullOrWhiteSpace(detail.EogOnly.GameMode)
                ? detail.EogOnly.QueueId.ToString(CultureInfo.InvariantCulture)
                : detail.EogOnly.GameMode;
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "SGP EOG · " + detail.Match.GameId.ToString(CultureInfo.InvariantCulture) +
                    " · " + mode }, 0, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical, SplitterDistance = 720,
                Panel1MinSize = 350, Panel2MinSize = 170 };
            var players = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, MultiSelect = false, HideSelection = false,
                GridLines = true, ShowGroups = true };
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam), 70);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer), 155);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 85);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicPositions), 75);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSpells), 80);
            players.Columns.Add("K/D/A", 85);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamage), 95);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardGold), 85);
            players.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardResult), 70);
            split.Panel1.Controls.Add(players);

            var stats = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true };
            stats.Columns.Add("SGP Stat", 190);
            stats.Columns.Add("Value", 105);
            split.Panel2.Controls.Add(stats);
            foreach (var group in detail.EogOnly.Players.GroupBy(p => p.TeamId).OrderBy(g => g.Key))
            {
                var complete = group.All(p => p.Kills.HasValue && p.Deaths.HasValue && p.Assists.HasValue);
                var totals = complete ? " · K/D/A " +
                    group.Sum(p => (long)p.Kills.Value).ToString(CultureInfo.InvariantCulture) + "/" +
                    group.Sum(p => (long)p.Deaths.Value).ToString(CultureInfo.InvariantCulture) + "/" +
                    group.Sum(p => (long)p.Assists.Value).ToString(CultureInfo.InvariantCulture) : "";
                var team = new ListViewGroup(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam) + " " + group.Key + totals);
                players.Groups.Add(team);
                foreach (var player in group)
                {
                    var kda = FormatMetric(player.Kills) + "/" + FormatMetric(player.Deaths) +
                        "/" + FormatMetric(player.Assists);
                    var item = new ListViewItem(new[] {
                        player.TeamId.ToString(CultureInfo.InvariantCulture),
                        string.IsNullOrWhiteSpace(player.Name) ? "—" : player.Name,
                        player.ChampionId.ToString(CultureInfo.InvariantCulture),
                        string.IsNullOrWhiteSpace(player.Position) ? "—" : player.Position,
                        player.Spell1Id > 0 && player.Spell2Id > 0
                            ? player.Spell1Id + " / " + player.Spell2Id : "—",
                        kda, FormatMetric(player.Damage), FormatMetric(player.Gold),
                        player.Win.HasValue ? (player.Win.Value
                            ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon)
                            : t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost)) : "—"
                    });
                    item.Group = team;
                    item.Tag = player;
                    players.Items.Add(item);
                }
            }
            players.SelectedIndexChanged += (sender, args) =>
            {
                if (!IsCurrent(detail)) { Close(); return; }
                stats.BeginUpdate();
                try
                {
                    stats.Items.Clear();
                    var selected = players.SelectedItems.Count == 1
                        ? players.SelectedItems[0].Tag as LeagueSgpEogPlayer : null;
                    if (selected == null || selected.Statistics == null) return;
                    foreach (var entry in selected.Statistics.OrderBy(p => p.Key,
                        StringComparer.OrdinalIgnoreCase))
                        stats.Items.Add(new ListViewItem(new[] {
                            entry.Key, FormatStatistic(entry.Value) }));
                }
                finally { stats.EndUpdate(); }
            };
            layout.Controls.Add(split, 0, 1);
            var close = new Button { Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel),
                Width = 100, Height = 30, Anchor = AnchorStyles.Right };
            close.Click += (sender, args) => Close();
            layout.Controls.Add(close, 0, 2);
            Controls.Add(layout);
            CancelButton = close;
            Shown += (sender, args) => { if (!IsCurrent(detail)) Close(); };
            Activated += (sender, args) => { if (!IsCurrent(detail)) Close(); };
        }

        private static bool IsCurrent(LeagueEncounterMatchDetailResult detail)
        {
            return detail.IsCurrentSession == null || detail.IsCurrentSession();
        }

        internal static string FormatMetric(int? value)
        {
            return value.HasValue ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";
        }

        internal static string FormatStatistic(double value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }
    }
}
