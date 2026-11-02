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
                p.HistoricalTeamId != player.HistoricalTeamId).ToArray();
            var swap = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            swap.Items.Add("—");
            foreach (var candidate in swapCandidates)
                swap.Items.Add((candidate.Player.GameName ?? ("#" + candidate.Player.CellId)) +
                    " · #" + candidate.Player.CellId);
            swap.SelectedIndex = 0;
            swap.Enabled = swapCandidates.Length > 0;
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
            var move = new Button { Width = 148, Height = 34,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftMoveTeam),
                Enabled = snapshot.HistoricalGameMode == "CLASSIC" &&
                    snapshot.Players.Count(p => p.HistoricalTeamId == player.HistoricalTeamId) > 1 };
            move.Click += (sender, args) =>
            {
                if (!isCurrentSession())
                {
                    move.Enabled = false;
                    return;
                }
                var destination = player.HistoricalTeamId == 100 ? 200 : 100;
                if (!LeagueHistoricalMatchDryRun.TryMoveTeam(snapshot, player.Player.PuuId, destination))
                {
                    MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            swapApply.Width = 148;
            var teamActions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                WrapContents = false, AutoScroll = true };
            teamActions.Controls.Add(swapApply);
            teamActions.Controls.Add(move);
            layout.Controls.Add(teamActions, 1, 5);

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

    internal sealed class LeagueHistoricalAddPlayerDialog : Form
    {
        internal string PlayerName { get; private set; }
        internal int TeamId { get; private set; }
        internal int ChampionId { get; private set; }

        internal LeagueHistoricalAddPlayerDialog(UiTextCatalog ui, int defaultTeam)
        {
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftEdit) + " · + " +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer);
            Width = 390; Height = 260; MinimumSize = new Size(330, 235);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 2, RowCount = 4, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            for (var i = 0; i < 3; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            var name = new TextBox { Dock = DockStyle.Fill, MaxLength = 48 };
            var team = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList };
            team.Items.Add(100); team.Items.Add(200);
            team.SelectedItem = defaultTeam == 200 ? 200 : 100;
            var champion = new NumericUpDown { Dock = DockStyle.Fill,
                Minimum = 1, Maximum = 9999, Value = 1 };
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer) }, 0, 0);
            layout.Controls.Add(name, 1, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam) }, 0, 1);
            layout.Controls.Add(team, 1, 1);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion) }, 0, 2);
            layout.Controls.Add(champion, 1, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Width = 90, DialogResult = DialogResult.Cancel,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            var apply = new Button { Width = 110,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftApply) };
            apply.Click += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text)) return;
                PlayerName = name.Text.Trim();
                TeamId = (int)team.SelectedItem;
                ChampionId = (int)champion.Value;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
        }
    }

    internal sealed class LeagueHistoricalArenaPairDialog : Form
    {
        internal string FirstName { get; private set; }
        internal string SecondName { get; private set; }
        internal int FirstChampion { get; private set; }
        internal int SecondChampion { get; private set; }

        internal LeagueHistoricalArenaPairDialog(UiTextCatalog ui)
        {
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftAddArenaPair);
            Width = 430; Height = 285; MinimumSize = new Size(370, 250);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 2, RowCount = 5, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            for (var i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            var first = new TextBox { Dock = DockStyle.Fill, MaxLength = 48 };
            var second = new TextBox { Dock = DockStyle.Fill, MaxLength = 48 };
            var firstChampion = new NumericUpDown { Dock = DockStyle.Fill,
                Minimum = 1, Maximum = 9999, Value = 1 };
            var secondChampion = new NumericUpDown { Dock = DockStyle.Fill,
                Minimum = 1, Maximum = 9999, Value = 1 };
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftPairFirst) }, 0, 0);
            layout.Controls.Add(first, 1, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion) }, 0, 1);
            layout.Controls.Add(firstChampion, 1, 1);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftPairSecond) }, 0, 2);
            layout.Controls.Add(second, 1, 2);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion) }, 0, 3);
            layout.Controls.Add(secondChampion, 1, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Width = 90, DialogResult = DialogResult.Cancel,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            var apply = new Button { Width = 110,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftApply) };
            apply.Click += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(first.Text) ||
                    string.IsNullOrWhiteSpace(second.Text)) return;
                FirstName = first.Text.Trim(); SecondName = second.Text.Trim();
                FirstChampion = (int)firstChampion.Value;
                SecondChampion = (int)secondChampion.Value;
                DialogResult = DialogResult.OK; Close();
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
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
                RowCount = 3, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
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
                ReadOnly = false, FillWeight = 140,
                ToolTipText = t(LeagueEfficiencyUiTextKeys.HistoricalDraftPlayerSlotHint)
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
                grid.Rows[index].Cells["player"].Tag =
                    player.Player.GameName ?? ("#" + player.Player.CellId);
            }
            layout.Controls.Add(grid, 0, 0);
            var targetRow = new FlowLayoutPanel { Dock = DockStyle.Fill,
                WrapContents = false, AutoScroll = true };
            targetRow.Controls.Add(new Label {
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftTarget),
                AutoSize = true, Margin = new Padding(4, 8, 12, 0)
            });
            var target = new ComboBox { Width = 410, DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Value" };
            var targetChoices = snapshot.Players.OrderBy(p => p.HistoricalTeamId)
                .ThenBy(p => p.Player.CellId).ToArray();
            for (var index = 0; index < targetChoices.Length; index++)
            {
                var choice = targetChoices[index];
                target.Items.Add(new System.Collections.Generic.KeyValuePair<string, string>(
                    choice.Player.PuuId,
                    (choice.Player.GameName ?? ("#" + choice.Player.CellId)) +
                    " (#" + choice.Player.CellId + ")"));
                if (choice.Player.PuuId == snapshot.SelfPuuid) target.SelectedIndex = index;
            }
            targetRow.Controls.Add(target);
            targetRow.Controls.Add(new Label {
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterQueue) + " ID",
                AutoSize = true, Margin = new Padding(8, 8, 4, 0)
            });
            var queueId = new NumericUpDown {
                Minimum = 0, Maximum = 100000, Width = 100,
                Value = Math.Max(0, Math.Min(100000, snapshot.HistoricalQueueId)),
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterQueue) + " ID"
            };
            targetRow.Controls.Add(queueId);
            layout.Controls.Add(targetRow, 0, 1);
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
                        DraftName = string.Equals(Convert.ToString(row.Cells["player"].Value,
                            CultureInfo.InvariantCulture), row.Cells["player"].Tag as string,
                            StringComparison.Ordinal) ? null : Convert.ToString(
                            row.Cells["player"].Value, CultureInfo.InvariantCulture),
                        Position = row.Cells["position"].Value as string,
                        PrimaryPosition = row.Cells["primary"].Value as string,
                        SecondaryPosition = row.Cells["secondary"].Value as string
                    });
                }
                var targetChoice = target.SelectedItem;
                var selectedTarget = targetChoice is System.Collections.Generic.KeyValuePair<string, string>
                    ? ((System.Collections.Generic.KeyValuePair<string, string>)targetChoice).Key : null;
                if (!isCurrentSession() ||
                    !LeagueHistoricalMatchDryRun.TryApplyRosterDraft(snapshot, changes, selectedTarget,
                        (int)queueId.Value))
                {
                    MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable), Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            };
            var restore = new Button { Width = 160, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftRestoreAll) };
            restore.Click += (sender, args) =>
            {
                if (!isCurrentSession() || !LeagueHistoricalMatchDryRun.TryRestoreOriginalDraft(snapshot))
                {
                    restore.Enabled = false;
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            };
            restore.Enabled = !snapshot.HistoricalStandalone;
            actions.Controls.Add(cancel); actions.Controls.Add(apply); actions.Controls.Add(restore);

            if (snapshot.HistoricalGameMode == "CLASSIC")
            {
                var blank = new Button { Width = 150, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftStartBlank) };
                blank.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    if (!LeagueHistoricalMatchDryRun.TryStartBlankRoster(snapshot,
                        t(LeagueEfficiencyUiTextKeys.HistoricalDraftBlankBlue),
                        t(LeagueEfficiencyUiTextKeys.HistoricalDraftBlankRed)))
                    {
                        MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                actions.Controls.Add(blank);
                var addPlayer = new Button { Width = 118, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftAddPlayer) };
                addPlayer.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    var teamId = snapshot.Players.Count(p => p.HistoricalTeamId == 100) <=
                        snapshot.Players.Count(p => p.HistoricalTeamId == 200) ? 100 : 200;
                    using (var dialog = new LeagueHistoricalAddPlayerDialog(ui, teamId))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        if (!isCurrentSession() ||
                            !LeagueHistoricalMatchDryRun.TryAddSyntheticPlayer(snapshot,
                                dialog.PlayerName, dialog.TeamId, dialog.ChampionId))
                        {
                            MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                var removePlayer = new Button { Width = 118, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftRemovePlayer) };
                removePlayer.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    var id = grid.CurrentRow?.Tag as string;
                    if (!LeagueHistoricalMatchDryRun.TryRemoveDraftPlayer(snapshot, id))
                    {
                        MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                actions.Controls.Add(removePlayer);
                actions.Controls.Add(addPlayer);
            }
            else if (snapshot.HistoricalGameMode == "CHERRY")
            {
                var blankArena = new Button { Width = 150, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftStartBlank) };
                blankArena.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    if (!LeagueHistoricalMatchDryRun.TryStartBlankRoster(snapshot,
                        t(LeagueEfficiencyUiTextKeys.HistoricalDraftBlankArenaOne),
                        t(LeagueEfficiencyUiTextKeys.HistoricalDraftBlankArenaTwo)))
                    {
                        MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                actions.Controls.Add(blankArena);
                var addPair = new Button { Width = 148, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftAddArenaPair) };
                addPair.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    using (var dialog = new LeagueHistoricalArenaPairDialog(ui))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        if (!isCurrentSession() ||
                            !LeagueHistoricalMatchDryRun.TryAddSyntheticArenaPair(snapshot,
                                dialog.FirstName, dialog.FirstChampion,
                                dialog.SecondName, dialog.SecondChampion))
                        {
                            MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                var removePair = new Button { Width = 158, Height = 30,
                    Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftRemoveArenaPair) };
                removePair.Click += (sender, args) =>
                {
                    if (!isCurrentSession()) { Close(); return; }
                    var id = grid.CurrentRow?.Tag as string;
                    var row = snapshot.Players.SingleOrDefault(p => p.Player.PuuId == id);
                    if (row == null ||
                        !LeagueHistoricalMatchDryRun.TryRemoveArenaPair(snapshot, row.HistoricalTeamId))
                    {
                        MessageBox.Show(this, t(LeagueEfficiencyUiTextKeys.DynamicUnavailable),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    DialogResult = DialogResult.OK; Close();
                };
                actions.Controls.Add(removePair);
                actions.Controls.Add(addPair);
            }
            layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
            Activated += (sender, args) => { if (!isCurrentSession()) Close(); };
        }
    }

    // Standalone, memory-only draft workbench. Never binds a client session or sends a message.
    internal sealed class LeagueStandaloneDraftDialog : Form
    {
        private LeagueDynamicMessageSnapshot _draft;

        internal LeagueStandaloneDraftDialog(UiTextCatalog ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.HistoricalDryRunTitle);
            Width = 920; Height = 640; MinimumSize = new Size(600, 420);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
                WrapContents = false, AutoScroll = true };
            var mode = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            mode.Items.Add("CLASSIC");
            mode.Items.Add("CHERRY");
            mode.SelectedIndex = 0;
            var create = new Button { AutoSize = true, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftStartBlank) };
            var edit = new Button { AutoSize = true, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftEdit) };
            var target = new ComboBox { Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList };
            target.Items.Add(t(LeagueEfficiencyUiTextKeys.DynamicFriendly));
            target.Items.Add(t(LeagueEfficiencyUiTextKeys.DynamicEnemy));
            target.Items.Add(t(LeagueEfficiencyUiTextKeys.DynamicAll));
            target.SelectedIndex = 2;
            actions.Controls.Add(mode); actions.Controls.Add(create); actions.Controls.Add(edit);
            actions.Controls.Add(new Label { AutoSize = true,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftTarget),
                Margin = new Padding(8, 8, 4, 0) });
            actions.Controls.Add(target);
            layout.Controls.Add(actions, 0, 0);
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam), 120);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer), 260);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 130);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicPositions), 160);
            layout.Controls.Add(rows, 0, 1);
            var preview = new TextBox { Dock = DockStyle.Fill, ReadOnly = true,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            layout.Controls.Add(preview, 0, 2);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = t(LeagueEfficiencyUiTextKeys.HistoricalDraftHint),
                AutoEllipsis = true }, 0, 3);
            Action refresh = () =>
            {
                rows.BeginUpdate();
                try
                {
                    rows.Items.Clear();
                    foreach (var player in LeagueHistoricalMatchDryRun.DraftDisplayPlayers(_draft))
                    {
                        rows.Items.Add(new ListViewItem(new[] {
                            _draft.HistoricalGameMode == "CHERRY"
                                ? "#" + player.HistoricalTeamId.ToString(CultureInfo.InvariantCulture)
                                : t(player.Friendly ? LeagueEfficiencyUiTextKeys.DynamicFriendly :
                                    LeagueEfficiencyUiTextKeys.DynamicEnemy),
                            player.Player.GameName ?? "",
                            player.ChampionName ?? ("#" + player.Player.ChampionId),
                            player.Player.Position ?? ""
                        }));
                    }
                }
                finally { rows.EndUpdate(); }
                var selectedTarget = new[] { "friendly", "enemy", "all" }[
                    Math.Max(0, target.SelectedIndex)];
                preview.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterQueue) + " ID: " +
                    _draft.HistoricalQueueId.ToString(CultureInfo.InvariantCulture) +
                    Environment.NewLine + string.Join(Environment.NewLine,
                    LeagueDynamicMessageFormatter.Build(_draft,
                        new LeagueDynamicMessageOptions { Target = selectedTarget,
                            NameStrategy = "championNameWithName" }));
            };
            Action createDraft = () =>
            {
                var cherry = mode.SelectedIndex == 1;
                _draft = LeagueHistoricalMatchDryRun.BuildStandalone(
                    cherry ? "CHERRY" : "CLASSIC",
                    t(cherry ? LeagueEfficiencyUiTextKeys.HistoricalDraftBlankArenaOne :
                        LeagueEfficiencyUiTextKeys.HistoricalDraftBlankBlue),
                    t(cherry ? LeagueEfficiencyUiTextKeys.HistoricalDraftBlankArenaTwo :
                        LeagueEfficiencyUiTextKeys.HistoricalDraftBlankRed));
                if (_draft == null)
                {
                    preview.Text = t(LeagueEfficiencyUiTextKeys.DynamicUnavailable);
                    return;
                }
                refresh();
            };
            create.Click += (sender, args) => createDraft();
            target.SelectedIndexChanged += (sender, args) =>
            {
                if (_draft != null) refresh();
            };
            edit.Click += (sender, args) =>
            {
                if (_draft == null) return;
                using (var editor = new LeagueHistoricalRosterEditorDialog(
                    ui, _draft, () => !IsDisposed && !Disposing))
                    if (editor.ShowDialog(this) == DialogResult.OK) refresh();
            };
            Controls.Add(layout);
            createDraft();
        }
    }

}
