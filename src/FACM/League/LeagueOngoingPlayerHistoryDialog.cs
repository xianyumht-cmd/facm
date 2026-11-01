using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Bounded, local player-card history; the existing dynamic-message owner supplies all data.
    internal sealed class LeagueOngoingPlayerHistoryDialog : Form
    {
        internal LeagueOngoingPlayerHistoryDialog(UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessagePlayer player,
            Func<bool> isCurrent)
        {
            if (ui == null || snapshot == null || player?.Player == null ||
                isCurrent == null || !snapshot.Players.Contains(player) ||
                !LeagueDynamicMessageService.SafePuuid(player.Player.PuuId))
                throw new ArgumentException("A current player-card snapshot is required.");
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.OngoingRecentHistory) + " · " +
                (player.Player.GameName ?? ("#" + player.Player.CellId));
            Width = 720; Height = 490; MinimumSize = new Size(480, 350);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 2, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var matches = LeagueHistoricalMatchDryRun.CardRecentMatches(player);
            var wins = matches.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Win);
            var losses = matches.Count(m => LeaguePlayerHistoryAnalysis.Outcome(m) == LeagueHistoryOutcome.Loss);
            var summary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = t(LeagueEfficiencyUiTextKeys.OngoingRecentSummary)
                    .Replace("{0}", matches.Count.ToString(CultureInfo.CurrentCulture))
                    .Replace("{1}", wins.ToString(CultureInfo.CurrentCulture))
                    .Replace("{2}", losses.ToString(CultureInfo.CurrentCulture)) };
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text };
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftGame), 120);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 165);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) + " / " +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost), 100);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftKda), 150);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicPositions), 115);
            foreach (var match in matches)
            {
                var outcome = LeaguePlayerHistoryAnalysis.Outcome(match);
                var row = new ListViewItem(match.GameId.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.ChampionName)
                    ? "#" + match.ChampionId : match.ChampionName);
                row.SubItems.Add(match.GameMode == "CHERRY" &&
                    LeagueCherryTeams.ValidPlacement(match.SubteamPlacement).HasValue
                    ? t(LeagueEfficiencyUiTextKeys.OngoingRecentPlacement).Replace("{0}",
                        match.SubteamPlacement.Value.ToString(CultureInfo.CurrentCulture))
                    : t(outcome == LeagueHistoryOutcome.Win
                        ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon :
                        outcome == LeagueHistoryOutcome.Loss
                        ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost :
                        outcome == LeagueHistoryOutcome.Remake
                        ? LeagueEfficiencyUiTextKeys.OngoingRecentRemake :
                        LeagueEfficiencyUiTextKeys.OngoingRecentAbort));
                row.SubItems.Add(match.StatisticsAvailable && match.ParticipantResolved
                    ? match.Kills + " / " + match.Deaths + " / " + match.Assists : "—");
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.Position) ? "—" : match.Position);
                rows.Items.Add(row);
            }
            layout.Controls.Add(summary, 0, 0);
            layout.Controls.Add(rows, 0, 1);
            Controls.Add(layout);
            Shown += (sender, args) => { if (!isCurrent()) Close(); };
            Activated += (sender, args) => { if (!isCurrent()) Close(); };
        }
    }
}
