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
            Width = 680; Height = 550; MinimumSize = new Size(470, 400);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
                RowCount = 8, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
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
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftPrimary) }, 0, 2);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftSecondary) }, 1, 2);
            var primary = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            var secondary = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var selector in new[] { primary, secondary })
            {
                selector.Items.Add("—");
                foreach (var role in positions.Skip(1))
                    selector.Items.Add(LeaguePlayerHistoryAnalysis.PositionName(ui, role));
                selector.SelectedIndex = 0;
                selector.Enabled = snapshot.HistoricalGameMode != "CHERRY";
            }
            primary.SelectedIndex = Math.Max(0,
                Array.IndexOf(positions, player.HistoricalPrimaryPosition ?? ""));
            secondary.SelectedIndex = Math.Max(0,
                Array.IndexOf(positions, player.HistoricalSecondaryPosition ?? ""));
            layout.Controls.Add(primary, 0, 3);
            layout.Controls.Add(secondary, 1, 3);

            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftSwapTeam) }, 0, 4);
            var swapCandidates = snapshot.Players.Where(p => p?.Player != null &&
                p.Player.PuuId != player.Player.PuuId &&
                p.Player.PuuId != snapshot.SelfPuuid &&
                p.HistoricalTeamId != player.HistoricalTeamId).ToArray();
            var swap = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            swap.Items.Add("—");
            foreach (var candidate in swapCandidates)
                swap.Items.Add((candidate.Player.GameName ?? ("#" + candidate.Player.CellId)) +
                    " · #" + candidate.Player.CellId);
            swap.SelectedIndex = 0;
            swap.Enabled = player.Player.PuuId != snapshot.SelfPuuid && swapCandidates.Length > 0;
            layout.Controls.Add(swap, 0, 5);
            var swapApply = new Button { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftSwapApply),
                Enabled = swap.Enabled };
            swapApply.Click += (sender, args) =>
            {
                if (swap.SelectedIndex < 1 || !isCurrentSession())
                {
                    swapApply.Enabled = false;
                    return;
                }
                if (!LeagueHistoricalMatchDryRun.TrySwapTeams(snapshot, player.Player.PuuId,
                    swapCandidates[swap.SelectedIndex - 1].Player.PuuId))
                {
                    MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            layout.Controls.Add(swapApply, 1, 5);

            var recent = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text };
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftGame), 145);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 150);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftKda), 160);
            recent.Columns.Add(t(LeagueEfficiencyUiTextKeys.HistoricalDraftRecent), 120);
            var recentMatches = LeagueHistoricalMatchDryRun.VerifiedRecentMatches(player);
            foreach (var match in recentMatches)
            {
                var row = new ListViewItem(match.GameId.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(string.IsNullOrWhiteSpace(match.ChampionName)
                    ? "#" + match.ChampionId : match.ChampionName);
                row.SubItems.Add(match.StatisticsAvailable && match.ParticipantResolved
                    ? match.Kills + " / " + match.Deaths + " / " + match.Assists : "—");
                row.SubItems.Add(t(LeaguePlayerHistoryAnalysis.Outcome(match) == LeagueHistoryOutcome.Win
                    ? LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon
                    : LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost));
                row.Tag = match;
                recent.Items.Add(row);
            }
            recent.DoubleClick += (sender, args) =>
            {
                if (recent.SelectedItems.Count != 1) return;
                var match = recent.SelectedItems[0].Tag as LeaguePlayerMatchSummary;
                if (match == null || match.ChampionId < 1 || match.ChampionId > 9999) return;
                champion.Value = match.ChampionId;
                var index = Array.IndexOf(positions, match.Position ?? "");
                if (snapshot.HistoricalGameMode == "CLASSIC" && index >= 0)
                    position.SelectedIndex = index;
            };
            layout.Controls.Add(recent, 0, 6);
            layout.SetColumnSpan(recent, 2);

            var reset = new Button { Width = 145, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftReset) };
            reset.Click += (sender, args) =>
            {
                if (player.HistoricalOriginalChampionId < 1 ||
                    player.HistoricalOriginalChampionId > 9999) return;
                champion.Value = player.HistoricalOriginalChampionId;
                position.SelectedIndex = Math.Max(0,
                    Array.IndexOf(positions, player.HistoricalOriginalPosition ?? ""));
                primary.SelectedIndex = 0;
                secondary.SelectedIndex = 0;
            };
            var restoreTeams = new Button { Width = 150, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftRestoreAll) };
            restoreTeams.Click += (sender, args) =>
            {
                if (!isCurrentSession() || !LeagueHistoricalMatchDryRun.TryRestoreOriginalDraft(snapshot))
                {
                    restoreTeams.Enabled = false;
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            var apply = new Button { Width = 155, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftApply) };
            var cancel = new Button { Width = 95, Height = 30,
                DialogResult = DialogResult.Cancel,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            apply.Click += (sender, args) =>
            {
                var restoredOriginal = (int)champion.Value == player.HistoricalOriginalChampionId &&
                    positions[position.SelectedIndex] == player.HistoricalOriginalPosition;
                var selectedName = restoredOriginal ? player.HistoricalOriginalChampionName :
                    recentMatches.FirstOrDefault(m => m.ChampionId == (int)champion.Value)?.ChampionName;
                if (!isCurrentSession())
                {
                    apply.Enabled = false;
                    return;
                }
                if (!LeagueHistoricalMatchDryRun.TryEditSelection(snapshot, player.Player.PuuId,
                        (int)champion.Value, positions[position.SelectedIndex], selectedName,
                        positions[primary.SelectedIndex], positions[secondary.SelectedIndex]))
                {
                    MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                WrapContents = false, AutoScroll = true };
            actions.Controls.Add(reset); actions.Controls.Add(restoreTeams);
            actions.Controls.Add(apply); actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 7);
            layout.SetColumnSpan(actions, 2);
            AcceptButton = apply; CancelButton = cancel;
            Controls.Add(layout);
        }
    }
    internal sealed class LeagueHistoricalRosterEditorDialog : Form
    {
        internal LeagueHistoricalRosterEditorDialog(UiTextCatalog ui,
            LeagueDynamicMessageSnapshot snapshot, Func<bool> isCurrentSession)
        {
            if (ui == null || snapshot == null || isCurrentSession == null)
                throw new ArgumentException("A verified historical lineup is required.");
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftEdit) + " · " +
                t(LeagueEfficiencyUiTextKeys.HistoricalDryRunTitle);
            Width = 950; Height = 570; MinimumSize = new Size(660, 420);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 2, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var grid = new DataGridView {
                Dock = DockStyle.Fill, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, MultiSelect = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            grid.DataError += (sender, args) => { args.ThrowException = false; };
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = "player", HeaderText = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer),
                ReadOnly = true, FillWeight = 140
            });
            grid.Columns.Add(new DataGridViewComboBoxColumn {
                Name = "team", HeaderText = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam),
                DataSource = snapshot.Players.Select(p => p.HistoricalOriginalTeamId)
                    .Distinct().OrderBy(id => id).ToArray(), FillWeight = 60
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = "champion", HeaderText = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion),
                FillWeight = 75
            });
            var roles = snapshot.HistoricalGameMode == "CHERRY" ? new[] { "" } :
                new[] { "", "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
            foreach (var name in new[] { "position", "primary", "secondary" })
                grid.Columns.Add(new DataGridViewComboBoxColumn {
                    Name = name, HeaderText = name == "position"
                        ? t(LeagueEfficiencyUiTextKeys.DynamicPositions)
                        : t(name == "primary" ? LeagueEfficiencyUiTextKeys.HistoricalDraftPrimary :
                            LeagueEfficiencyUiTextKeys.HistoricalDraftSecondary),
                    DataSource = roles.ToArray(), FillWeight = 80
                });
            foreach (var player in snapshot.Players.OrderBy(p => p.HistoricalTeamId)
                .ThenBy(p => p.Player.CellId))
            {
                var index = grid.Rows.Add(player.Player.GameName ?? ("#" + player.Player.CellId),
                    player.HistoricalTeamId, player.Player.ChampionId.ToString(CultureInfo.InvariantCulture),
                    player.Player.Position ?? "", player.HistoricalPrimaryPosition ?? "",
                    player.HistoricalSecondaryPosition ?? "");
                grid.Rows[index].Tag = player.Player.PuuId;
            }
            layout.Controls.Add(grid, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Width = 110, Height = 30, DialogResult = DialogResult.Cancel,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            var apply = new Button { Width = 150, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftApply) };
            apply.Click += (sender, args) =>
            {
                if (!isCurrentSession()) { apply.Enabled = false; Close(); return; }
                grid.EndEdit();
                var changes = new System.Collections.Generic.List<LeagueHistoricalRosterSelection>();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    int team, champion;
                    if (!int.TryParse(Convert.ToString(row.Cells["team"].Value,
                            CultureInfo.InvariantCulture), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out team) ||
                        !int.TryParse(Convert.ToString(row.Cells["champion"].Value,
                            CultureInfo.InvariantCulture), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out champion))
                    {
                        MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    changes.Add(new LeagueHistoricalRosterSelection {
                        PuuId = row.Tag as string, TeamId = team, ChampionId = champion,
                        Position = row.Cells["position"].Value as string,
                        PrimaryPosition = row.Cells["primary"].Value as string,
                        SecondaryPosition = row.Cells["secondary"].Value as string
                    });
                }
                if (!isCurrentSession() ||
                    !LeagueHistoricalMatchDryRun.TryApplyRosterDraft(snapshot, changes))
                {
                    MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            };
            actions.Controls.Add(cancel); actions.Controls.Add(apply);
            layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
            Activated += (sender, args) => { if (!isCurrentSession()) Close(); };
        }
    }

}
