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
            Action<LeaguePlayerMatchParticipant> showSelection = player =>
            {
                ShowPlayerIcons(player, catalog, iconStrip, loadAsset, t);
                if (player == null)
                {
                    selectedDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelectionHint);
                    runeDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterRuneSelectionHint);
                    return;
                }
                runeDetails.Text = FormatParticipantRuneDetails(player, catalog, t);
                var champion = string.IsNullOrWhiteSpace(player.ChampionName)
                    ? player.ChampionId.ToString() : player.ChampionName + " (#" + player.ChampionId + ")";
                var style = FormatNamedIds(new[] { player.PrimaryPerkStyleId, player.SecondaryPerkStyleId },
                    catalog == null ? null : catalog.PerkStyles);
                selectedDetails.Text = t(LeagueEfficiencyUiTextKeys.DynamicEncounterScoreboardSelection)
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
                item.SubItems.Add(player.CreepScore.ToString());
                item.SubItems.Add(player.DamageToChampions.ToString("N0"));
                item.SubItems.Add(player.GoldEarned.ToString("N0"));
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
            layout.Controls.Add(selectedDetails, 0, 4);
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
