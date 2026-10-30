using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueEncounterMatchScoreboardDialog : Form
    {
        private static readonly SemaphoreSlim IconRequestGate = new SemaphoreSlim(3, 3);
        private static readonly Regex RuneVariables = new Regex(@"@eogvar([123])@",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex RuneTags = new Regex(@"<[^>]{0,100}>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<Bitmap> _ownedIcons = new List<Bitmap>();
        private readonly ToolTip _tooltips = new ToolTip();
        private readonly ToolTip _teamTooltips = new ToolTip();
        private CancellationTokenSource _selectionCancellation;
        private int _selectionVersion;

        internal LeagueEncounterMatchScoreboardDialog(UiTextCatalog ui, LeagueEncounterMatchDetailResult detail,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset,
            Func<LeaguePlayerMatchSummary, CancellationToken, Task<LeagueEncounterReplayResult>> replay = null)
        {
            if (detail == null || detail.Status != LeagueEncounterMatchDetailStatus.Ready ||
                detail.Match == null || detail.Participants == null)
                throw new ArgumentException("Match details are not available.", nameof(detail));

            Func<string, string> t = key => LeagueEfficiencyText.Get(ui, key);
            Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTitle);
            Width = 960; Height = 685; MinimumSize = new Size(680, 510);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 6 };
            var isCherry = LeagueCherryTeams.IsCherry(detail.Match.GameMode);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, isCherry ? 164 : 106));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
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

            var teams = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
                AutoScroll = true, BackColor = FacmDesignSystem.Canvas };
            teams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            teams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var scores = detail.Teams;
            var orderedScores = OrderedScoreboardTeams(scores, isCherry);
            layout.Controls.Add(teams, 0, 1);

            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTitle) };
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTeam), isCherry ? 95 : 70);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer), 165);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardChampion), 125);
            rows.Columns.Add("K/D/A", 90);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardCs), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamage), 95);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardGold), 85);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardVision), 65);
            rows.Columns.Add(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardResult), 70);
            rows.ShowGroups = true;
            var participantGroups = new Dictionary<int, ListViewGroup>();
            if (scores != null)
            {
                foreach (var team in orderedScores)
                {
                    var groupName = isCherry
                        ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterCherryTeam)
                            .Replace("{0}", team.TeamId.ToString()) + " · " + FormatCherryPlacement(team.SubteamPlacement, t)
                        : team.TeamId == 100 ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBlue) :
                            team.TeamId == 200 ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamRed) :
                                t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamOther)
                                    .Replace("{0}", team.TeamId.ToString());
                    var group = new ListViewGroup(groupName);
                    rows.Groups.Add(group);
                    participantGroups[team.TeamId] = group;
                }
            }
            ListViewGroup unknownGroup = null;

            var selectedDetails = new TextBox { Dock = DockStyle.Fill, ReadOnly = true,
                Multiline = true, ScrollBars = ScrollBars.Vertical, TabStop = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint) };
            var damageStrip = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            damageStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            damageStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            damageStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var outgoingBar = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0),
                BackColor = FacmDesignSystem.CanvasRaised };
            var incomingBar = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0),
                BackColor = FacmDesignSystem.CanvasRaised };
            outgoingBar.AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDealtBar);
            incomingBar.AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenBar);
            damageStrip.Controls.Add(outgoingBar, 0, 0);
            damageStrip.Controls.Add(incomingBar, 1, 0);
            var selectionLayout = new TableLayoutPanel { Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tagStrip = new LeagueMatchTagStrip(t);
            selectionLayout.Controls.Add(damageStrip, 0, 0);
            selectionLayout.Controls.Add(tagStrip, 0, 1);
            selectionLayout.Controls.Add(selectedDetails, 0, 2);
            var maxOutgoing = detail.Participants.Where(p => p != null && p.DamageAvailable &&
                p.DamageToChampions >= 0).Select(p => (int?)p.DamageToChampions).DefaultIfEmpty().Max();
            var maxIncoming = detail.Participants.Where(p => p != null && p.TotalDamageTaken.HasValue &&
                p.TotalDamageTaken.Value >= 0).Select(p => p.TotalDamageTaken).DefaultIfEmpty().Max();
            LeaguePlayerMatchParticipant damageSelection = null;
            outgoingBar.Paint += (sender, e) => PaintDamageBar(e.Graphics, outgoingBar.ClientRectangle,
                damageSelection, false, maxOutgoing, t);
            incomingBar.Paint += (sender, e) => PaintDamageBar(e.Graphics, incomingBar.ClientRectangle,
                damageSelection, true, maxIncoming, t);
            var runeDetails = new TextBox { Dock = DockStyle.Fill, ReadOnly = true,
                Multiline = true, ScrollBars = ScrollBars.Vertical, TabStop = true,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneSelectionHint),
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneDetailsTab) };
            var runePlayers = new ListBox { Dock = DockStyle.Fill,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                IntegralHeight = false, AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardPlayer) };
            var iconStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                AutoScroll = true, BackColor = FacmDesignSystem.CanvasRaised };
            var catalog = detail.BuildCatalog;
            var radar = new LeagueMatchRadarPanel(detail.Participants, detail.Teams, isCherry, t);
            Action<LeaguePlayerMatchParticipant> showSelection = player =>
            {
                damageSelection = player;
                outgoingBar.Invalidate();
                incomingBar.Invalidate();
                radar.UpdateSelection(player);
                _tooltips.SetToolTip(outgoingBar, FormatDamageTooltip(player, false, maxOutgoing, t));
                _tooltips.SetToolTip(incomingBar, FormatDamageTooltip(player, true, maxIncoming, t));
                ShowPlayerIcons(player, catalog, iconStrip, loadAsset, t);
                if (player == null)
                {
                    tagStrip.SetTags(null);
                    selectedDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint);
                    runeDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneSelectionHint);
                    return;
                }
                runeDetails.Text = FormatParticipantRuneDetails(player, catalog, t);
                var champion = string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName + " (#" + player.ChampionId + ")";
                var style = FormatNamedIds(new[] { player.PrimaryPerkStyleId, player.SecondaryPerkStyleId },
                    catalog == null ? null : catalog.PerkStyles);
                var orderedTags = BuildCombinedMatchTagEntries(player, detail.Participants, isCherry, t,
                    match.MapId, match.GameDurationSeconds);
                tagStrip.SetTags(orderedTags.Select(tag => tag.Value));
                selectedDetails.Text = FormatParticipantRates(player, scores, isCherry,
                    match.GameDurationSeconds, t) + "\r\n" +
                    FormatParticipantDamageDetails(player, t) + "\r\n" +
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelection)
                    .Replace("{0}", string.IsNullOrWhiteSpace(player.PlayerName)
                        ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown) : player.PlayerName)
                    .Replace("{1}", champion)
                    .Replace("{2}", FormatNamedIds(player.SpellIds, catalog == null ? null : catalog.Spells))
                    .Replace("{3}", FormatNamedIds(player.ItemIds, catalog == null ? null : catalog.Items))
                    .Replace("{4}", FormatNamedIds(player.PerkIds, catalog == null ? null : catalog.Perks))
                    .Replace("{5}", style)
                    .Replace("{6}", FormatAugments(player.AugmentIds, catalog, t))
                    .Replace("{7}", FormatBoundItem(player.RoleBoundItemId,
                        catalog == null ? null : catalog.Items));
            };
            if (orderedScores.Count > 0)
            {
                teams.RowCount = (orderedScores.Count + 1) / 2;
                for (var i = 0; i < teams.RowCount; i++)
                    teams.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
                for (var i = 0; i < orderedScores.Count; i++)
                {
                    var team = orderedScores[i];
                    var text = FormatTeamScore(team, t);
                    var fullText = FormatTeamDetails(team, detail.ChampionNames, t);
                    var card = new Button { Dock = DockStyle.Fill, Text = text + "\r\n" +
                        t(team.IsCherry ? LeagueEfficiencyUiTextKeys.DynamicEncounterCherryInspect :
                            LeagueEfficiencyUiTextKeys.DynamicEncounterTeamInspect),
                        BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                        FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(9, 5, 9, 3), AutoEllipsis = true,
                        AccessibleName = text, AccessibleDescription = fullText,
                        Cursor = Cursors.Hand, Margin = new Padding(3) };
                    card.FlatAppearance.BorderSize = 0;
                    card.Click += (sender, e) =>
                    {
                        if (rows.SelectedItems.Count > 0) rows.SelectedItems[0].Selected = false;
                        else showSelection(null);
                        selectedDetails.Text = fullText;
                    };
                    teams.Controls.Add(card, i % 2, i / 2);
                    _teamTooltips.SetToolTip(card, fullText);
                }
            }
            rows.SelectedIndexChanged += (sender, e) =>
            {
                var selected = rows.SelectedIndices.Count == 1 ? rows.SelectedIndices[0] : -1;
                if (runePlayers.SelectedIndex != selected) runePlayers.SelectedIndex = selected;
                showSelection(selected >= 0 ? rows.Items[selected].Tag as LeaguePlayerMatchParticipant : null);
            };
            runePlayers.SelectedIndexChanged += (sender, e) =>
            {
                var selected = runePlayers.SelectedIndex;
                if (selected >= 0 && selected < rows.Items.Count && !rows.Items[selected].Selected)
                    rows.Items[selected].Selected = true;
            };

            ListViewItem targetItem = null;
            foreach (var player in OrderScoreboardParticipants(detail.Participants, orderedScores, isCherry))
            {
                var name = string.IsNullOrWhiteSpace(player.PlayerName)
                    ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown)
                    : player.PlayerName;
                if (player.IsTarget) name = "★ " + name;
                var teamId = LeagueCherryTeams.GroupId(player, isCherry);
                var item = new ListViewItem(isCherry
                    ? (teamId > 0 ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterCherryTeam)
                        .Replace("{0}", teamId.ToString()) : "—") : player.TeamId.ToString());
                item.SubItems.Add(name);
                item.SubItems.Add(string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName);
                item.SubItems.Add(player.Kills + "/" + player.Deaths + "/" + player.Assists);
                item.SubItems.Add(player.CreepScoreAvailable ? player.CreepScore.ToString() : "—");
                item.SubItems.Add(player.DamageAvailable ? player.DamageToChampions.ToString("N0") : "—");
                item.SubItems.Add(player.GoldAvailable ? player.GoldEarned.ToString("N0") : "—");
                item.SubItems.Add(player.VisionScore.ToString());
                item.SubItems.Add(isCherry ? FormatCherryPlacement(player.SubteamPlacement, t) :
                    player.Win ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) :
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost));
                item.Tag = player;
                ListViewGroup participantGroup;
                if (teamId > 0 && participantGroups.TryGetValue(teamId, out participantGroup))
                    item.Group = participantGroup;
                else
                {
                    if (unknownGroup == null)
                    {
                        unknownGroup = new ListViewGroup(t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknown));
                        rows.Groups.Add(unknownGroup);
                    }
                    item.Group = unknownGroup;
                }
                if (player.IsTarget)
                {
                    item.BackColor = FacmDesignSystem.Canvas;
                    targetItem = item;
                }
                rows.Items.Add(item);
                runePlayers.Items.Add(name + " · " + (string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName));
            }
            var matchTabs = new TabControl { Dock = DockStyle.Fill };
            var participantsTab = new TabPage(ui.Get(UiTextKeys.LeagueTimelineParticipants));
            participantsTab.Controls.Add(rows);
            matchTabs.TabPages.Add(participantsTab);
            var runesTab = new TabPage(t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneDetailsTab));
            var runeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            runeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            runeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            runeLayout.Controls.Add(runePlayers, 0, 0);
            runeLayout.Controls.Add(runeDetails, 1, 0);
            runesTab.Controls.Add(runeLayout);
            matchTabs.TabPages.Add(runesTab);
            var radarTab = new TabPage(t(LeagueEfficiencyUiTextKeys.DynamicEncounterRadarTitle));
            radarTab.Controls.Add(radar);
            matchTabs.TabPages.Add(radarTab);
            var timelineTab = new TabPage(ui.Get(UiTextKeys.LeagueTimelineTitle));
            var timeline = new LeagueMatchTimelinePanel(ui, detail, _lifetime.Token, loadAsset);
            timelineTab.Controls.Add(timeline);
            matchTabs.TabPages.Add(timelineTab);
            matchTabs.SelectedIndexChanged += async (sender, e) =>
            {
                if (matchTabs.SelectedTab == timelineTab) await timeline.EnsureLoadedAsync();
            };
            layout.Controls.Add(matchTabs, 0, 2);
            layout.Controls.Add(iconStrip, 0, 3);
            layout.Controls.Add(selectionLayout, 0, 4);
            if (targetItem != null)
            {
                targetItem.Selected = true;
                showSelection(targetItem.Tag as LeaguePlayerMatchParticipant);
            }

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            var replayStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplay) };
            footer.Controls.Add(replayStatus, 0, 0);
            var replayButton = new Button { Dock = DockStyle.Top, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplay), Enabled = replay != null };
            replayButton.Click += async (sender, e) =>
            {
                if (replay == null || _lifetime.IsCancellationRequested) return;
                replayButton.Enabled = false;
                replayStatus.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayChecking);
                try
                {
                    var result = await replay(match, _lifetime.Token);
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    replayStatus.Text = ReplayStatusText(result, t);
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    if (!IsDisposed && !_lifetime.IsCancellationRequested)
                        replayStatus.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayUnavailable);
                }
                finally
                {
                    if (!IsDisposed && !_lifetime.IsCancellationRequested) replayButton.Enabled = true;
                }
            };
            footer.Controls.Add(replayButton, 1, 0);
            var close = new Button { Width = 90, Height = 30,
                Text = t(LeagueEfficiencyUiTextKeys.DynamicTagCancel) };
            close.Click += (sender, e) => Close();
            footer.Controls.Add(close, 2, 0);
            layout.Controls.Add(footer, 0, 5);
            Controls.Add(layout);
            CancelButton = close;
            FormClosed += (sender, e) =>
            {
                _lifetime.Cancel();
                _selectionCancellation?.Cancel();
                _selectionCancellation?.Dispose();
                foreach (var bitmap in _ownedIcons) bitmap.Dispose();
                _ownedIcons.Clear();
                _tooltips.Dispose();
                _teamTooltips.Dispose();
                _lifetime.Dispose();
            };
        }

        internal static List<LeagueMatchTeamScore> OrderedScoreboardTeams(
            IEnumerable<LeagueMatchTeamScore> teams, bool cherry)
        {
            return (teams ?? Enumerable.Empty<LeagueMatchTeamScore>())
                .Where(team => team != null && team.TeamId > 0)
                .OrderBy(team => cherry ? team.SubteamPlacement ?? int.MaxValue : team.TeamId)
                .ThenBy(team => team.TeamId).ToList();
        }

        internal static string FormatParticipantRates(LeaguePlayerMatchParticipant player,
            IEnumerable<LeagueMatchTeamScore> teams, bool cherry, int gameDurationSeconds,
            Func<string, string> t)
        {
            var groupId = player == null ? 0 : LeagueCherryTeams.GroupId(player, cherry);
            var matching = groupId > 0 && teams != null
                ? teams.Where(team => team != null && team.TeamId == groupId).Take(2).ToList()
                : new List<LeagueMatchTeamScore>();
            var team = matching.Count == 1 ? matching[0] : null;
            var scoreValid = player != null && player.Kills >= 0 && player.Assists >= 0 && player.Deaths >= 0;
            var kda = scoreValid ? (double?)(player.Kills + (double)player.Assists) /
                Math.Max(1, player.Deaths) : null;
            var participation = scoreValid && team != null && team.Kills >= 0
                ? (double?)(player.Kills + (double)player.Assists) / Math.Max(1L, team.Kills) * 100 : null;
            var damageShare = player != null && player.DamageAvailable && player.DamageToChampions >= 0 &&
                team != null && team.DamageToChampions.HasValue && team.DamageToChampions.Value >= 0
                    ? (double?)player.DamageToChampions / Math.Max(1L, team.DamageToChampions.Value) * 100 : null;
            var minutes = gameDurationSeconds > 0 ? gameDurationSeconds / 60d : 0;
            var csPerMinute = player != null && player.CreepScoreAvailable && player.CreepScore >= 0 && minutes > 0
                ? (double?)player.CreepScore / minutes : null;
            var goldPerMinute = player != null && player.GoldAvailable && player.GoldEarned >= 0 && minutes > 0
                ? (double?)player.GoldEarned / minutes : null;
            Func<double?, string, string> number = (value, format) => value.HasValue &&
                !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                    ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
            Func<double?, string> percent = value => value.HasValue &&
                !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                    ? Math.Round(value.Value, 0, MidpointRounding.AwayFromZero)
                        .ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
            return t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardRates)
                .Replace("{0}", number(kda, "0.00"))
                .Replace("{1}", percent(participation))
                .Replace("{2}", percent(damageShare))
                .Replace("{3}", number(csPerMinute, "0.0"))
                .Replace("{4}", number(goldPerMinute, "0.0"));
        }

        internal static string FormatParticipantDamageDetails(LeaguePlayerMatchParticipant player,
            Func<string, string> t)
        {
            Func<int?, string> count = value => value.HasValue && value.Value >= 0
                ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";
            var dealt = player != null && player.DamageAvailable && player.DamageToChampions >= 0
                ? (int?)player.DamageToChampions : null;
            return t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamageSplit)
                .Replace("{0}", count(dealt))
                .Replace("{1}", count(player == null ? null : player.PhysicalDamageToChampions))
                .Replace("{2}", count(player == null ? null : player.MagicDamageToChampions))
                .Replace("{3}", count(player == null ? null : player.TrueDamageToChampions)) + "\r\n" +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenSplit)
                    .Replace("{0}", count(player == null ? null : player.TotalDamageTaken))
                    .Replace("{1}", count(player == null ? null : player.PhysicalDamageTaken))
                    .Replace("{2}", count(player == null ? null : player.MagicDamageTaken))
                    .Replace("{3}", count(player == null ? null : player.TrueDamageTaken));
        }

        internal static string FormatMultikillTags(LeaguePlayerMatchParticipant player, Func<string, string> t)
        {
            var tags = BuildMultikillTagEntries(player, t);
            return tags.Count == 0 ? string.Empty :
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTitle)
                    .Replace("{0}", string.Join(" · ", tags.OrderByDescending(tag => tag.Key)
                        .Select(tag => tag.Value))) + "\r\n";
        }

        private static List<KeyValuePair<long, string>> BuildMultikillTagEntries(
            LeaguePlayerMatchParticipant player, Func<string, string> t)
        {
            var tags = new List<KeyValuePair<long, string>>();
            if (player == null) return tags;
            var penta = player.PentaKills;
            var quadra = player.QuadraKills.HasValue && penta.HasValue &&
                player.QuadraKills.Value >= penta.Value
                    ? (int?)(player.QuadraKills.Value - penta.Value) : null;
            var triple = player.TripleKills.HasValue && player.QuadraKills.HasValue &&
                player.TripleKills.Value >= player.QuadraKills.Value
                    ? (int?)(player.TripleKills.Value - player.QuadraKills.Value) : null;
            var doubleKills = player.DoubleKills.HasValue && player.TripleKills.HasValue &&
                player.DoubleKills.Value >= player.TripleKills.Value
                    ? (int?)(player.DoubleKills.Value - player.TripleKills.Value) : null;
            var counts = new[] { penta, quadra, triple, doubleKills };
            var keys = new[] { LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillPenta,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillQuadra,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTriple,
                LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillDouble };
            for (var index = 0; index < counts.Length; index++)
            {
                if (!counts[index].HasValue || counts[index].Value <= 0) continue;
                var label = t(keys[index]);
                if (counts[index].Value > 1)
                    label = t(LeagueEfficiencyUiTextKeys.DynamicEncounterMultikillTimes)
                        .Replace("{0}", label)
                        .Replace("{1}", counts[index].Value.ToString(CultureInfo.InvariantCulture));
                var priority = index == 0 ? 20000L : index == 1 ? 1300L :
                    index == 2 ? 300L + counts[index].Value * 15L : 100L + counts[index].Value * 10L;
                tags.Add(new KeyValuePair<long, string>(priority, label));
            }
            return tags;
        }

        internal static string FormatMatchPerformanceTags(LeaguePlayerMatchParticipant player,
            IEnumerable<LeaguePlayerMatchParticipant> participants, bool cherry, Func<string, string> t,
            int mapId = 0, int gameDurationSeconds = 0)
        {
            var tags = BuildMatchPerformanceTagEntries(player, participants, cherry, t,
                mapId, gameDurationSeconds);
            return tags.Count == 0 ? string.Empty :
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTitle)
                    .Replace("{0}", string.Join(" · ", tags.OrderByDescending(tag => tag.Key)
                        .Select(tag => tag.Value))) + "\r\n";
        }

        internal static string FormatCombinedMatchTags(LeaguePlayerMatchParticipant player,
            IEnumerable<LeaguePlayerMatchParticipant> participants, bool cherry, Func<string, string> t,
            int mapId = 0, int gameDurationSeconds = 0)
        {
            var tags = BuildCombinedMatchTagEntries(player, participants, cherry, t,
                mapId, gameDurationSeconds);
            return tags.Count == 0 ? string.Empty :
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTitle)
                    .Replace("{0}", string.Join(" · ", tags.Select(tag => tag.Value))) + "\r\n";
        }

        internal static List<KeyValuePair<long, string>> BuildCombinedMatchTagEntries(
            LeaguePlayerMatchParticipant player, IEnumerable<LeaguePlayerMatchParticipant> participants,
            bool cherry, Func<string, string> t, int mapId = 0, int gameDurationSeconds = 0)
        {
            var tags = BuildMultikillTagEntries(player, t);
            tags.AddRange(BuildMatchPerformanceTagEntries(player, participants, cherry, t,
                mapId, gameDurationSeconds).Select(tag =>
                    new KeyValuePair<long, string>(tag.Key, tag.Value)));
            return tags.OrderByDescending(tag => tag.Key).ToList();
        }

        private static List<KeyValuePair<int, string>> BuildMatchPerformanceTagEntries(
            LeaguePlayerMatchParticipant player, IEnumerable<LeaguePlayerMatchParticipant> participants,
            bool cherry, Func<string, string> t, int mapId, int gameDurationSeconds)
        {
            var tags = new List<KeyValuePair<int, string>>();
            if (player == null || participants == null) return tags;
            var all = participants.Where(p => p != null).Take(21).ToList();
            if (all.Count == 0 || all.Count > 20 || !all.Contains(player) ||
                all.Any(p => p.ParticipantId <= 0 || LeagueCherryTeams.GroupId(p, cherry) <= 0) ||
                all.Select(p => p.ParticipantId).Distinct().Count() != all.Count) return tags;
            var groupId = LeagueCherryTeams.GroupId(player, cherry);
            var group = all.Where(p => LeagueCherryTeams.GroupId(p, cherry) == groupId).ToList();
            if ((mapId == 11 || mapId == 12) && gameDurationSeconds > 0)
            {
                var durationMinutes = gameDurationSeconds / 60d;
                var nearThreshold = mapId == 11 ? 5d : 1.5d;
                var ownThreshold = mapId == 11 ? 6d : 1.5d;
                if (player.KillsNearEnemyTurret.HasValue &&
                    player.KillsNearEnemyTurret.Value > 0 &&
                    player.KillsNearEnemyTurret.Value >= durationMinutes / nearThreshold)
                {
                    var count = player.KillsNearEnemyTurret.Value;
                    var label = t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerDive);
                    if (count > 1) label += " ×" + count.ToString("N0", CultureInfo.InvariantCulture);
                    tags.Add(new KeyValuePair<int, string>(1660, label));
                }
                if (player.KillsUnderOwnTurret.HasValue &&
                    player.KillsUnderOwnTurret.Value > 0 &&
                    player.KillsUnderOwnTurret.Value >= durationMinutes / ownThreshold)
                {
                    var count = player.KillsUnderOwnTurret.Value;
                    var label = t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerUnder);
                    if (count > 1) label += " ×" + count.ToString("N0", CultureInfo.InvariantCulture);
                    tags.Add(new KeyValuePair<int, string>(1640, label));
                }
            }
            AddMatchLeaderTag(tags, player, all, group,
                p => p.DamageAvailable && p.DamageToChampions >= 0 ? (int?)p.DamageToChampions : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderDamageTeam, 1800, 1750, t);
            AddMatchLeaderTag(tags, player, all, group,
                p => p.TotalHeal.HasValue && p.TotalHeal.Value >= 0 ? p.TotalHeal : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderHealTeam, 1600, 1550, t);
            AddMatchLeaderTag(tags, player, all, group,
                p => p.TotalDamageShieldedOnTeammates,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderShieldTeam, 1500, 1450, t);
            AddMatchLeaderTag(tags, player, all, group,
                p => p.TotalDamageTaken.HasValue && p.TotalDamageTaken.Value >= 0
                    ? p.TotalDamageTaken : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTakenTeam, 1400, 1350, t);
            AddMatchLeaderTag(tags, player, all, group,
                p => p.TotalDamageToTowers,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderTowerTeam, 900, 850, t);
            AddMatchLeaderTag(tags, player, all, group,
                p => p.GoldAvailable && p.GoldEarned >= 0 ? (int?)p.GoldEarned : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderGoldTeam, 700, 650, t);
            AddMatchValueLeaderTag(tags, player, all, group,
                p => p.DamageAvailable && p.GoldAvailable &&
                    p.DamageToChampions >= 0 && p.GoldEarned >= 0
                        ? (double?)p.DamageToChampions / Math.Max(1, p.GoldEarned) : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderEfficiencyTeam,
                1800, 930, true, t);
            if (player.SoloKills.HasValue && player.SoloKills.Value >= 2)
                tags.Add(new KeyValuePair<int, string>(1700,
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderSolo)
                        .Replace("{0}", player.SoloKills.Value.ToString("N0", CultureInfo.InvariantCulture))));
            if (player.CreepScoreAvailable && player.CreepScore > 0 &&
                all.All(p => p.CreepScoreAvailable && p.CreepScore >= 0) &&
                player.CreepScore == all.Max(p => p.CreepScore))
                tags.Add(new KeyValuePair<int, string>(600,
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCs)
                        .Replace("{0}", player.CreepScore.ToString("N0", CultureInfo.InvariantCulture))));
            if (player.MaxCsAdvantageOnLaneOpponent.HasValue &&
                player.MaxCsAdvantageOnLaneOpponent.Value >= 40 &&
                !double.IsNaN(player.MaxCsAdvantageOnLaneOpponent.Value) &&
                !double.IsInfinity(player.MaxCsAdvantageOnLaneOpponent.Value))
            {
                var advantage = player.MaxCsAdvantageOnLaneOpponent.Value;
                var priority = (int)Math.Min(int.MaxValue,
                    Math.Round(750 + advantage * 10, MidpointRounding.AwayFromZero));
                tags.Add(new KeyValuePair<int, string>(priority,
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCsAdvantage)
                        .Replace("{0}", Math.Round(advantage, MidpointRounding.AwayFromZero)
                            .ToString("N0", CultureInfo.InvariantCulture))));
            }
            if (player.KnockEnemyIntoTeamAndKill.HasValue &&
                player.KnockEnemyIntoTeamAndKill.Value >= 6)
                tags.Add(new KeyValuePair<int, string>(660,
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKnockUp)
                        .Replace("{0}", player.KnockEnemyIntoTeamAndKill.Value
                            .ToString("N0", CultureInfo.InvariantCulture))));
            AddMatchValueLeaderTag(tags, player, all, group,
                p => p.Kills >= 0 ? (double?)p.Kills : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKillsTeam, 1200, 1150, false, t);
            Func<LeaguePlayerMatchParticipant, double?> participation = p =>
            {
                var teammates = all.Where(member => LeagueCherryTeams.GroupId(member, cherry) ==
                    LeagueCherryTeams.GroupId(p, cherry)).ToList();
                if (p.Kills < 0 || p.Assists < 0 || teammates.Any(member => member.Kills < 0))
                    return null;
                var teamKills = teammates.Sum(member => (long)member.Kills);
                return teamKills > 0 ? (double?)(p.Kills + (double)p.Assists) / teamKills : null;
            };
            AddMatchValueLeaderTag(tags, player, all, group, participation,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderKpTeam, 1100, 1050, true, t);
            AddMatchValueLeaderTag(tags, player, all, group,
                p => p.TimeCCingOthers.HasValue && p.TimeCCingOthers.Value >= 0
                    ? p.TimeCCingOthers : null,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcMatch,
                LeagueEfficiencyUiTextKeys.DynamicEncounterLeaderCcTeam, 1750, 1700, false, t, "#,##0.##");
            return tags;
        }

        private static void AddMatchLeaderTag(List<KeyValuePair<int, string>> tags,
            LeaguePlayerMatchParticipant player, List<LeaguePlayerMatchParticipant> all,
            List<LeaguePlayerMatchParticipant> group, Func<LeaguePlayerMatchParticipant, int?> metric,
            string matchKey, string teamKey, int matchPriority, int teamPriority, Func<string, string> t)
        {
            var own = metric(player);
            if (!own.HasValue || own.Value <= 0 || group.Count == 0) return;
            var teamValues = group.Select(metric).ToList();
            if (teamValues.Any(value => !value.HasValue || value.Value < 0)) return;
            var allValues = all.Select(metric).ToList();
            var matchBest = allValues.All(value => value.HasValue && value.Value >= 0) &&
                own.Value == allValues.Max(value => value.Value);
            var teamBest = own.Value == teamValues.Max(value => value.Value);
            if (!matchBest && !teamBest) return;
            var teamTotal = teamValues.Sum(value => (long)value.Value);
            var share = ((double)own.Value / Math.Max(1L, teamTotal) * 100)
                .ToString("0.00", CultureInfo.InvariantCulture);
            var label = t(matchBest ? matchKey : teamKey) + "（" +
                own.Value.ToString("N0", CultureInfo.InvariantCulture) + " / " + share + "%）";
            tags.Add(new KeyValuePair<int, string>(matchBest ? matchPriority : teamPriority, label));
        }

        private static bool ValidLeaderValue(double? value)
        {
            return value.HasValue && value.Value >= 0 &&
                !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
        }

        private static void AddMatchValueLeaderTag(List<KeyValuePair<int, string>> tags,
            LeaguePlayerMatchParticipant player, List<LeaguePlayerMatchParticipant> all,
            List<LeaguePlayerMatchParticipant> group, Func<LeaguePlayerMatchParticipant, double?> metric,
            string matchKey, string teamKey, int matchPriority, int teamPriority,
            bool percentage, Func<string, string> t, string valueFormat = "N0")
        {
            var own = metric(player);
            if (!own.HasValue || own.Value <= 0 || double.IsNaN(own.Value) ||
                double.IsInfinity(own.Value) || group.Count == 0) return;
            var teamValues = group.Select(metric).ToList();
            if (teamValues.Any(value => !ValidLeaderValue(value))) return;
            var allValues = all.Select(metric).ToList();
            var matchBest = allValues.All(ValidLeaderValue) &&
                own.Value == allValues.Max(value => value.Value);
            var teamBest = own.Value == teamValues.Max(value => value.Value);
            if (!matchBest && !teamBest) return;
            var valueText = percentage
                ? (own.Value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%"
                : own.Value.ToString(valueFormat, CultureInfo.InvariantCulture);
            tags.Add(new KeyValuePair<int, string>(matchBest ? matchPriority : teamPriority,
                t(matchBest ? matchKey : teamKey) + "（" + valueText + "）"));
        }

        internal static int[] DamageBarWidths(int width, int? baseline, int? physical, int? magic, int? trueDamage)
        {
            var widths = new int[3];
            if (width <= 0 || !baseline.HasValue || baseline.Value <= 0) return widths;
            var parts = new[] { physical, magic, trueDamage };
            foreach (var index in Enumerable.Range(0, 3).OrderByDescending(i => parts[i] ?? -1).ThenBy(i => i))
            {
                if (!parts[index].HasValue || parts[index].Value <= 0) continue;
                var remaining = width - widths.Sum();
                if (remaining <= 0) break;
                var scaled = (int)Math.Min(width, Math.Round((double)parts[index].Value /
                    baseline.Value * width, MidpointRounding.AwayFromZero));
                widths[index] = Math.Min(remaining, scaled);
            }
            return widths;
        }

        internal static string FormatDamageTooltip(LeaguePlayerMatchParticipant player, bool taken,
            int? baseline, Func<string, string> t)
        {
            if (player == null) return t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint);
            int? total = taken ? player.TotalDamageTaken : player.DamageAvailable && player.DamageToChampions >= 0
                ? (int?)player.DamageToChampions : null;
            int? physical = taken ? player.PhysicalDamageTaken : player.PhysicalDamageToChampions;
            int? magic = taken ? player.MagicDamageTaken : player.MagicDamageToChampions;
            int? trueDamage = taken ? player.TrueDamageTaken : player.TrueDamageToChampions;
            Func<int?, string> count = value => value.HasValue && value.Value >= 0
                ? value.Value.ToString("N0", CultureInfo.InvariantCulture) : "—";
            Func<int?, int?, string, string> percent = (numerator, denominator, format) =>
                numerator.HasValue && numerator.Value >= 0 && denominator.HasValue && denominator.Value >= 0
                    ? (numerator.Value / (double)Math.Max(1, denominator.Value) * 100)
                        .ToString(format, CultureInfo.InvariantCulture) + "%" : "—";
            return t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDamageTooltip)
                .Replace("{0}", t(taken ? LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenBar :
                    LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDealtBar))
                .Replace("{1}", count(total))
                .Replace("{2}", percent(total, baseline, "0.00"))
                .Replace("{3}", count(physical)).Replace("{4}", percent(physical, total, "0"))
                .Replace("{5}", count(magic)).Replace("{6}", percent(magic, total, "0"))
                .Replace("{7}", count(trueDamage)).Replace("{8}", percent(trueDamage, total, "0"));
        }

        internal static void PaintDamageBar(Graphics graphics, Rectangle bounds, LeaguePlayerMatchParticipant player,
            bool taken, int? baseline, Func<string, string> t)
        {
            if (bounds.Width <= 16 || bounds.Height <= 13) return;
            int? total = player == null ? null : taken ? player.TotalDamageTaken :
                player.DamageAvailable && player.DamageToChampions >= 0 ? (int?)player.DamageToChampions : null;
            var label = t(taken ? LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardTakenBar :
                LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardDealtBar) + " " +
                (total.HasValue && total.Value >= 0 ? total.Value.ToString("N0", CultureInfo.InvariantCulture) : "—");
            TextRenderer.DrawText(graphics, label, SystemFonts.MessageBoxFont,
                new Rectangle(8, 0, bounds.Width - 16, bounds.Height - 14), FacmDesignSystem.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            var bar = new Rectangle(8, bounds.Height - 12, bounds.Width - 16, 7);
            using (var background = new SolidBrush(FacmDesignSystem.CanvasRaised.GetBrightness() < 0.5f
                ? Color.FromArgb(58, 58, 58) : Color.FromArgb(229, 229, 229)))
                graphics.FillRectangle(background, bar);
            if (player == null) return;
            var widths = taken ? DamageBarWidths(bar.Width, baseline, player.PhysicalDamageTaken,
                player.MagicDamageTaken, player.TrueDamageTaken) :
                DamageBarWidths(bar.Width, baseline, player.PhysicalDamageToChampions,
                    player.MagicDamageToChampions, player.TrueDamageToChampions);
            var colors = new[] { Color.FromArgb(224, 120, 86), Color.FromArgb(91, 159, 215),
                Color.FromArgb(168, 168, 168) };
            var x = bar.X;
            foreach (var index in Enumerable.Range(0, 3).OrderByDescending(i => widths[i]).ThenBy(i => i))
            {
                if (widths[index] <= 0) continue;
                using (var brush = new SolidBrush(colors[index]))
                    graphics.FillRectangle(brush, x, bar.Y, widths[index], bar.Height);
                x += widths[index];
            }
        }

        internal static List<LeaguePlayerMatchParticipant> OrderScoreboardParticipants(
            IEnumerable<LeaguePlayerMatchParticipant> participants,
            IEnumerable<LeagueMatchTeamScore> teams, bool cherry)
        {
            var ordered = new List<LeaguePlayerMatchParticipant>();
            var source = (participants ?? Enumerable.Empty<LeaguePlayerMatchParticipant>())
                .Where(player => player != null).ToList();
            var usedGroups = new HashSet<int>();
            foreach (var team in OrderedScoreboardTeams(teams, cherry))
            {
                if (!usedGroups.Add(team.TeamId)) continue;
                foreach (var player in source)
                    if (LeagueCherryTeams.GroupId(player, cherry) == team.TeamId)
                        ordered.Add(player);
            }
            // Unverified subteam IDs must never inherit red/blue or an unrelated Cherry group.
            foreach (var player in source)
                if (!usedGroups.Contains(LeagueCherryTeams.GroupId(player, cherry))) ordered.Add(player);
            return ordered;
        }

        internal static string ReplayStatusText(LeagueEncounterReplayResult result, Func<string, string> t)
        {
            if (result == null) return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayUnavailable);
            switch (result.Status)
            {
                case LeagueEncounterReplayStatus.DownloadRequested:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayDownloadRequested);
                case LeagueEncounterReplayStatus.WatchRequested:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayWatchRequested);
                case LeagueEncounterReplayStatus.Checking:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayChecking);
                case LeagueEncounterReplayStatus.Downloading:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayDownloading)
                        .Replace("{0}", result.Progress.ToString());
                case LeagueEncounterReplayStatus.Incompatible:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayIncompatible);
                case LeagueEncounterReplayStatus.NoClient:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchNoClient);
                case LeagueEncounterReplayStatus.WrongAccount:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWrongAccount);
                default:
                    return t(LeagueEfficiencyUiTextKeys.DynamicEncounterReplayUnavailable);
            }
        }

        private static string FormatTeamScore(LeagueMatchTeamScore team, Func<string, string> t)
        {
            if (team.IsCherry)
            {
                return t(LeagueEfficiencyUiTextKeys.DynamicEncounterCherrySummary)
                    .Replace("{0}", t(LeagueEfficiencyUiTextKeys.DynamicEncounterCherryTeam)
                        .Replace("{0}", team.TeamId.ToString()))
                    .Replace("{1}", FormatCherryPlacement(team.SubteamPlacement, t))
                    .Replace("{2}", team.PlayerCount.ToString())
                    .Replace("{3}", team.Kills.ToString()).Replace("{4}", team.Deaths.ToString())
                    .Replace("{5}", team.Assists.ToString())
                    .Replace("{6}", team.DamageToChampions.HasValue ? team.DamageToChampions.Value.ToString("N0") : "—")
                    .Replace("{7}", team.GoldEarned.HasValue ? team.GoldEarned.Value.ToString("N0") : "—");
            }
            var teamName = team.TeamId == 100 ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBlue) :
                team.TeamId == 200 ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamRed) :
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamOther).Replace("{0}", team.TeamId.ToString());
            var result = !team.Win.HasValue ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknownResult) :
                team.Win.Value ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchWon) :
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterMatchLost);
            Func<int?, string> count = value => value.HasValue ? value.Value.ToString() : "—";
            return t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamSummary)
                .Replace("{0}", teamName).Replace("{1}", result)
                .Replace("{2}", team.PlayerCount.ToString())
                .Replace("{3}", team.Kills.ToString()).Replace("{4}", team.Deaths.ToString())
                .Replace("{5}", team.Assists.ToString())
                .Replace("{6}", team.DamageToChampions.HasValue ? team.DamageToChampions.Value.ToString("N0") : "—")
                .Replace("{7}", team.GoldEarned.HasValue ? team.GoldEarned.Value.ToString("N0") : "—")
                .Replace("{8}", count(team.BaronKills)).Replace("{9}", count(team.DragonKills))
                .Replace("{10}", count(team.TowerKills)).Replace("{11}", count(team.RiftHeraldKills))
                .Replace("{12}", count(team.InhibitorKills)).Replace("{13}", count(team.HordeKills));
        }

        internal static string FormatTeamDetails(LeagueMatchTeamScore team,
            IDictionary<int, string> championNames, Func<string, string> t)
        {
            if (team.IsCherry) return FormatTeamScore(team, t);
            string bans;
            if (team.Bans == null) bans = t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknown);
            else if (team.Bans.Count == 0) bans = t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamNone);
            else
            {
                var names = new List<string>();
                foreach (var ban in team.Bans)
                {
                    string name;
                    var display = championNames != null && championNames.TryGetValue(ban.ChampionId, out name) &&
                        !string.IsNullOrWhiteSpace(name) ? name + " (#" + ban.ChampionId + ")" : "#" + ban.ChampionId;
                    if (ban.PickTurn.HasValue)
                        display += t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBanTurn)
                            .Replace("{0}", ban.PickTurn.Value.ToString());
                    names.Add(display);
                }
                bans = string.Join(" / ", names);
            }
            Func<bool?, string> flag = value => !value.HasValue
                ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknown)
                : value.Value ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamYes)
                    : t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamNo);
            return FormatTeamScore(team, t) + "\r\n" +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamBans).Replace("{0}", bans) + "\r\n" +
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamFirsts)
                    .Replace("{0}", flag(team.FirstBaron)).Replace("{1}", flag(team.FirstDragon))
                    .Replace("{2}", flag(team.FirstTower)).Replace("{3}", flag(team.FirstInhibitor));
        }

        internal static string FormatCherryPlacement(int? placement, Func<string, string> t)
        {
            return placement.HasValue && placement.Value > 0
                ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterCherryPlacement)
                    .Replace("{0}", placement.Value.ToString()) :
                t(LeagueEfficiencyUiTextKeys.DynamicEncounterTeamUnknown);
        }

        private void ShowPlayerIcons(LeaguePlayerMatchParticipant player, LeagueBuildAdvisorCatalog catalog,
            FlowLayoutPanel strip, Func<string, CancellationToken, Task<Bitmap>> loadAsset,
            Func<string, string> t)
        {
            _selectionVersion++;
            _selectionCancellation?.Cancel();
            _selectionCancellation?.Dispose();
            _selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _tooltips.RemoveAll();
            while (strip.Controls.Count > 0)
            {
                var control = strip.Controls[0];
                strip.Controls.RemoveAt(0);
                control.Dispose();
            }
            foreach (var bitmap in _ownedIcons) bitmap.Dispose();
            _ownedIcons.Clear();
            if (player == null || catalog == null || loadAsset == null) return;
            AddIconGroup(strip, t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsItems),
                player.ItemIds, catalog.Items, catalog.ItemIcons, loadAsset);
            if (player.RoleBoundItemId.GetValueOrDefault() > 0 &&
                (player.ItemIds == null || Array.IndexOf(player.ItemIds, player.RoleBoundItemId.Value) < 0))
                AddIconGroup(strip, t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsBoundItem),
                    new[] { player.RoleBoundItemId.Value }, catalog.Items, catalog.ItemIcons, loadAsset);
            AddIconGroup(strip, t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSpells),
                player.SpellIds, catalog.Spells, catalog.SpellIcons, loadAsset);
            AddIconGroup(strip, FormatPerkGroupHeading(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsPrimary),
                    player.PrimaryPerkStyleId, catalog.PerkStyles),
                PerkSlots(player.PerkIds, 0, 4), catalog.Perks, catalog.PerkIcons, loadAsset);
            AddIconGroup(strip, FormatPerkGroupHeading(
                    t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSecondary),
                    player.SecondaryPerkStyleId, catalog.PerkStyles),
                PerkSlots(player.PerkIds, 4, 2), catalog.Perks, catalog.PerkIcons, loadAsset);
            if (player.StatPerkIds != null)
                AddIconGroup(strip, t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneStatShards),
                    Array.ConvertAll(player.StatPerkIds, value => value.GetValueOrDefault()),
                    catalog.Perks, catalog.PerkIcons, loadAsset);
            AddIconGroup(strip, t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsAugments),
                player.AugmentIds, BuildAugmentLabels(catalog, t), catalog.AugmentIcons, loadAsset);
        }

        private static int[] PerkSlots(int[] perks, int offset, int count)
        {
            if (perks == null || perks.Length <= offset) return null;
            var group = new int[Math.Min(count, perks.Length - offset)];
            Array.Copy(perks, offset, group, 0, group.Length);
            return group;
        }

        internal static string FormatPerkGroupHeading(string heading, int styleId, IDictionary<int, string> styles)
        {
            if (styleId <= 0) return heading;
            string name;
            return heading + " · " + (styles != null && styles.TryGetValue(styleId, out name) &&
                !string.IsNullOrWhiteSpace(name) ? name : "#" + styleId);
        }

        internal static string FormatAugments(int[] ids, LeagueBuildAdvisorCatalog catalog,
            Func<string, string> t = null)
        {
            if (ids == null) return "—";
            var labels = new List<string>();
            foreach (var id in ids)
            {
                if (id > 0) labels.Add(FormatAugmentLabel(id, catalog, t));
            }
            return labels.Count == 0 ? "—" : string.Join(" / ", labels);
        }

        private static IDictionary<int, string> BuildAugmentLabels(LeagueBuildAdvisorCatalog catalog,
            Func<string, string> t)
        {
            var labels = new Dictionary<int, string>();
            if (catalog == null) return labels;
            foreach (var id in catalog.Augments.Keys)
                labels[id] = FormatAugmentLabel(id, catalog, t);
            return labels;
        }

        private static string FormatAugmentLabel(int id, LeagueBuildAdvisorCatalog catalog, Func<string, string> t)
        {
            string name;
            if (catalog == null || !catalog.Augments.TryGetValue(id, out name) ||
                string.IsNullOrWhiteSpace(name)) return "#" + id;
            string rarity;
            if (catalog.AugmentRarities.TryGetValue(id, out rarity))
            {
                var localize = t ?? (key => LeagueEfficiencyText.Get(null, key));
                switch (rarity)
                {
                    case "kPrismatic": return localize(LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentPrismatic) + " · " + name;
                    case "kGold": return localize(LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentGold) + " · " + name;
                    case "kSilver": return localize(LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentSilver) + " · " + name;
                    case "kBronze": return localize(LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentBronze) + " · " + name;
                    case "kEventChoice": return localize(LeagueEfficiencyUiTextKeys.DynamicEncounterAugmentEvent) + " · " + name;
                }
            }
            return name;
        }

        internal static string FormatParticipantRuneDetails(LeaguePlayerMatchParticipant player,
            LeagueBuildAdvisorCatalog catalog, Func<string, string> t)
        {
            if (player == null) return t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneSelectionHint);
            var output = new StringBuilder(750);
            output.AppendLine(string.IsNullOrWhiteSpace(player.PlayerName)
                ? t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardUnknown) : player.PlayerName);
            if (player.StatPerkIds != null)
            {
                var stats = new List<string>();
                foreach (var id in player.StatPerkIds)
                {
                    string name;
                    stats.Add(!id.HasValue || id.Value <= 0 ? "—" : catalog != null &&
                        catalog.Perks.TryGetValue(id.Value, out name) && !string.IsNullOrWhiteSpace(name)
                        ? name : "#" + id.Value);
                }
                output.AppendLine(t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneStatShards) + "：" +
                    string.Join(" / ", stats));
            }
            var labels = new[] {
                FormatPerkGroupHeading(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsPrimary),
                    player.PrimaryPerkStyleId, catalog == null ? null : catalog.PerkStyles),
                FormatPerkGroupHeading(t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardIconsSecondary),
                    player.SecondaryPerkStyleId, catalog == null ? null : catalog.PerkStyles)
            };
            for (var group = 0; group < 2; group++)
            {
                output.AppendLine().AppendLine(labels[group]);
                var start = group == 0 ? 0 : 4;
                var count = group == 0 ? 4 : 2;
                var found = false;
                for (var slot = start; slot < start + count; slot++)
                {
                    var id = player.PerkIds != null && slot < player.PerkIds.Length ? player.PerkIds[slot] : 0;
                    if (id <= 0) continue;
                    found = true;
                    string name;
                    output.Append("• ").AppendLine(catalog != null && catalog.Perks.TryGetValue(id, out name) &&
                        !string.IsNullOrWhiteSpace(name) ? name : "#" + id);
                    string[] descriptions;
                    if (catalog == null || !catalog.PerkEndOfGameDescriptions.TryGetValue(id, out descriptions)) continue;
                    var values = player.PerkEndOfGameValues != null && slot < player.PerkEndOfGameValues.Length
                        ? player.PerkEndOfGameValues[slot] : null;
                    foreach (var description in descriptions)
                    {
                        if (string.IsNullOrWhiteSpace(description)) continue;
                        var valueText = RuneVariables.Replace(description, match =>
                        {
                            var index = match.Groups[1].Value[0] - '1';
                            return values != null && index < values.Length && values[index].HasValue
                                ? values[index].Value.ToString("G", CultureInfo.InvariantCulture) : "—";
                        });
                        valueText = WebUtility.HtmlDecode(RuneTags.Replace(valueText, "")).Trim();
                        if (valueText.Length > 0) output.Append("    ").AppendLine(valueText);
                    }
                }
                if (!found) output.AppendLine("—");
            }
            return output.ToString();
        }

        private void AddIconGroup(FlowLayoutPanel strip, string label, int[] ids,
            IDictionary<int, string> names, IDictionary<int, string> icons,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset)
        {
            if (ids == null || Array.TrueForAll(ids, id => id <= 0)) return;
            strip.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(8, 12, 3, 0) });
            foreach (var id in ids)
            {
                if (id <= 0) continue;
                string name;
                if (names == null || !names.TryGetValue(id, out name) || string.IsNullOrWhiteSpace(name))
                    name = "#" + id;
                var token = new Panel { Size = new Size(29, 29), Margin = new Padding(2, 6, 1, 1),
                    BackColor = FacmDesignSystem.Canvas };
                var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
                var fallback = new Label { Dock = DockStyle.Fill, Text = id.ToString(),
                    AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter };
                token.Controls.Add(picture); token.Controls.Add(fallback);
                fallback.BringToFront();
                _tooltips.SetToolTip(token, name); _tooltips.SetToolTip(picture, name);
                _tooltips.SetToolTip(fallback, name);
                strip.Controls.Add(token);
                string reference;
                if (icons != null && icons.TryGetValue(id, out reference) &&
                    !string.IsNullOrWhiteSpace(reference))
                    _ = LoadIconAsync(picture, fallback, reference, loadAsset,
                        _selectionVersion, _selectionCancellation.Token);
            }
        }

        private async Task LoadIconAsync(PictureBox picture, Label fallback, string reference,
            Func<string, CancellationToken, Task<Bitmap>> loadAsset, int generation, CancellationToken token)
        {
            Bitmap bitmap = null;
            try
            {
                await IconRequestGate.WaitAsync(token);
                try { bitmap = await loadAsset(reference, token); }
                finally { IconRequestGate.Release(); }
                if (bitmap == null) return;
                if (token.IsCancellationRequested || IsDisposed || picture.IsDisposed ||
                    generation != _selectionVersion) return;
                picture.Image = bitmap;
                _ownedIcons.Add(bitmap);
                bitmap = null;
                if (!fallback.IsDisposed) fallback.Visible = false;
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
            finally { bitmap?.Dispose(); }
        }

        private static string FormatNamedIds(int[] values, IDictionary<int, string> names)
        {
            if (values == null) return "—";
            var labels = new List<string>();
            foreach (var id in values)
            {
                if (id <= 0) continue;
                string name;
                labels.Add(names != null && names.TryGetValue(id, out name) && !string.IsNullOrWhiteSpace(name)
                    ? name : "#" + id);
            }
            return labels.Count == 0 ? "—" : string.Join(" / ", labels);
        }

        internal static string FormatBoundItem(int? value, IDictionary<int, string> names)
        {
            return value.HasValue && value.Value > 0 ?
                FormatNamedIds(new[] { value.Value }, names) : "—";
        }

        private static string FormatIds(int[] values, string empty)
        {
            if (values == null) return empty;
            var list = Array.FindAll(values, value => value > 0);
            return list.Length == 0 ? empty : string.Join(" / ", list);
        }
    }
}
