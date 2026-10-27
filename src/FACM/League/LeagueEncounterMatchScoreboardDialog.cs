using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueEncounterMatchScoreboardDialog : Form
    {
        internal LeagueEncounterMatchScoreboardDialog(UiTextCatalog ui, LeagueEncounterMatchDetailResult detail)
        {
            if (detail == null || detail.Status != LeagueEncounterMatchDetailStatus.Ready ||
                detail.Match == null || detail.Participants == null)
                throw new ArgumentException("Match details are not available.", nameof(detail));

            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTitle);
            Width = 960; Height = 475; MinimumSize = new Size(680, 335);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

            var match = detail.Match;
            var duration = TimeSpan.FromSeconds(Math.Max(0, match.GameDurationSeconds));
            var summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSummary)
                    .Replace("{0}", match.GameId.ToString())
                    .Replace("{1}", string.IsNullOrWhiteSpace(match.GameMode) ? match.QueueId.ToString() : match.GameMode)
                    .Replace("{2}", ((int)duration.TotalMinutes).ToString() + ":" + duration.Seconds.ToString("D2")),
                AutoEllipsis = true };
            layout.Controls.Add(summary, 0, 0);

            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTitle) };
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam), 70);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer), 165);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 65);
            rows.Columns.Add("K/D/A", 90);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardCs), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamage), 95);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardGold), 85);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardVision), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardResult), 70);

            foreach (var player in detail.Participants)
            {
                var name = string.IsNullOrWhiteSpace(player.PlayerName)
                    ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown)
                    : player.PlayerName;
                if (player.IsTarget) name = "★ " + name;
                var item = new ListViewItem(player.TeamId.ToString());
                item.SubItems.Add(name);
                item.SubItems.Add(player.ChampionId.ToString());
                item.SubItems.Add(player.Kills + "/" + player.Deaths + "/" + player.Assists);
                item.SubItems.Add(player.CreepScore.ToString());
                item.SubItems.Add(player.DamageToChampions.ToString("N0"));
                item.SubItems.Add(player.GoldEarned.ToString("N0"));
                item.SubItems.Add(player.VisionScore.ToString());
                item.SubItems.Add(player.Win ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) :
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost));
                if (player.IsTarget) item.BackColor = FacmDesignSystem.Canvas;
                rows.Items.Add(item);
            }
            layout.Controls.Add(rows, 0, 1);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Width = 90, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            close.Click += (sender, e) => Close();
            footer.Controls.Add(close);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            CancelButton = close;
        }
    }
}
