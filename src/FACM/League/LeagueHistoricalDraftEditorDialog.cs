using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    // Edits only the loaded historical preview; no gameflow or LCU write owner.
    internal sealed class LeagueHistoricalDraftEditorDialog : Form
    {
        internal LeagueHistoricalDraftEditorDialog(UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, LeagueDynamicMessagePlayer player,
            Func<bool> isCurrentSession)
        {
            if (ui == null || snapshot == null || player?.Player == null || isCurrentSession == null)
                throw new ArgumentException("A verified historical preview is required.");
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftEdit) + " · " +
                (player.Player.GameName ?? ("#" + player.Player.CellId));
            Width = 640; Height = 480; MinimumSize = new Size(470, 350);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
                RowCount = 4, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            layout.Controls.Add(new Label { Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion),
                Dock = DockStyle.Fill }, 0, 0);
            layout.Controls.Add(new Label { Text = t(LeagueEfficiencyUiTextKeys.DynamicPositions),
                Dock = DockStyle.Fill }, 1, 0);
            var champion = new NumericUpDown { Minimum = 1, Maximum = 9999,
                Value = Math.Max(1, Math.Min(9999, player.Player.ChampionId)),
                Dock = DockStyle.Fill };
            layout.Controls.Add(champion, 0, 1);
            var positions = new[] { "", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            var position = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill };
            position.Items.Add("—");
            foreach (var role in positions.Skip(1))
                position.Items.Add(LeaguePlayerHistoryAnalysis.PositionName(ui, role));
            position.SelectedIndex = Math.Max(0, Array.IndexOf(positions, player.Player.Position ?? ""));
            if (snapshot.HistoricalGameMode == "CHERRY")
            {
                position.SelectedIndex = 0;
                position.Enabled = false;
            }
            layout.Controls.Add(position, 1, 1);

            var recent = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text };
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftGame), 145);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 150);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftKda), 160);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftRecent), 120);
            foreach (var match in LeagueHistoricalMatchDryRun.VerifiedRecentMatches(player))
            {
                var row = new ListViewItem(match.GameId.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.ChampionName)
                    ? "#" + match.ChampionId : match.ChampionName);
                row.SubItems.Add(match.StatisticsAvailable && match.ParticipantResolved
                    ? match.Kills + " / " + match.Deaths + " / " + match.Assists : "—");
                row.SubItems.Add(t(LeaguePlayerHistoryAnalysis.Outcome(match) == LeagueHistoryOutcome.Win
                    ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon
                    : LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost));
                recent.Items.Add(row);
            }
            layout.Controls.Add(recent, 0, 2);
            layout.SetColumnSpan(recent, 2);

            var apply = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftApply) };
            var cancel = new Button { Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicCancel) };
            apply.Click += (sender, args) =>
            {
                if (!isCurrentSession() ||
                    !LeagueHistoricalMatchDryRun.TryEditSelection(snapshot, player.Player.PuuId,
                        (int)champion.Value, positions[position.SelectedIndex]))
                {
                    apply.Enabled = false;
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            layout.Controls.Add(apply, 0, 3);
            layout.Controls.Add(cancel, 1, 3);
            AcceptButton = apply; CancelButton = cancel;
            Controls.Add(layout);
        }
    }
}
