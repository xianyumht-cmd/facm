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
            Width = 960; Height = 545; MinimumSize = new Size(680, 400);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
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
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 125);
            rows.Columns.Add("K/D/A", 90);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardCs), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamage), 95);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardGold), 85);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardVision), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardResult), 70);

            var selectedDetails = new TextBox { Dock = DockStyle.Fill, ReadOnly = true,
                Multiline = true, ScrollBars = ScrollBars.Vertical, TabStop = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint) };
            Action<LeaguePlayerMatchParticipant> showSelection = player =>
            {
                if (player == null)
                {
                    selectedDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint);
                    return;
                }
                var champion = string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName + " (#" + player.ChampionId + ")";
                var style = FormatIds(new[] { player.PrimaryPerkStyleId, player.SecondaryPerkStyleId },
                    "—");
                selectedDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelection)
                    .Replace("{0}", string.IsNullOrWhiteSpace(player.PlayerName)
                        ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown) : player.PlayerName)
                    .Replace("{1}", champion)
                    .Replace("{2}", FormatIds(player.SpellIds, "—"))
                    .Replace("{3}", FormatIds(player.ItemIds, "—"))
                    .Replace("{4}", FormatIds(player.PerkIds, "—"))
                    .Replace("{5}", style)
                    .Replace("{6}", FormatIds(player.AugmentIds, "—"));
            };
            rows.SelectedIndexChanged += (sender, e) =>
                showSelection(rows.SelectedItems.Count == 1
                    ? rows.SelectedItems[0].Tag as LeaguePlayerMatchParticipant : null);

            ListViewItem targetItem = null;
            foreach (var player in detail.Participants)
            {
                var name = string.IsNullOrWhiteSpace(player.PlayerName)
                    ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown)
                    : player.PlayerName;
                if (player.IsTarget) name = "★ " + name;
                var item = new ListViewItem(player.TeamId.ToString());
                item.SubItems.Add(name);
                item.SubItems.Add(string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName);
                item.SubItems.Add(player.Kills + "/" + player.Deaths + "/" + player.Assists);
                item.SubItems.Add(player.CreepScore.ToString());
                item.SubItems.Add(player.DamageToChampions.ToString("N0"));
                item.SubItems.Add(player.GoldEarned.ToString("N0"));
                item.SubItems.Add(player.VisionScore.ToString());
                item.SubItems.Add(player.Win ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) :
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost));
                item.Tag = player;
                if (player.IsTarget)
                {
                    item.BackColor = FacmDesignSystem.Canvas;
                    targetItem = item;
                }
                rows.Items.Add(item);
            }
            layout.Controls.Add(rows, 0, 1);
            layout.Controls.Add(selectedDetails, 0, 2);
            if (targetItem != null)
            {
                targetItem.Selected = true;
                showSelection(targetItem.Tag as LeaguePlayerMatchParticipant);
            }

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Width = 90, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            close.Click += (sender, e) => Close();
            footer.Controls.Add(close);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            CancelButton = close;
        }

        private static string FormatIds(int[] values, string empty)
        {
            if (values == null) return empty;
            var list = Array.FindAll(values, value => value > 0);
            return list.Length == 0 ? empty : string.Join(" / ", list);
        }
    }
}
