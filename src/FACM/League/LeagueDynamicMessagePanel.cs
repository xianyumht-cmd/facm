using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueDynamicMessagePanel : UserControl
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly CancellationTokenSource _lifetime;
        private readonly LeagueDynamicMessageOptions _options;
        private readonly ComboBox _target, _names;
        private readonly CheckedListBox _players, _groups, _jungle;
        private readonly ContextMenuStrip _tagMenu;
        private readonly ContextMenuStrip _exchangeMenu;
        private readonly TabControl _selectionTabs;
        private readonly TextBox _preview;
        private readonly Label _status;
        private readonly Button _load, _loadJungle, _send;
        private readonly Button[] _shortcutButtons = new Button[3];
        private LeagueDynamicMessageSnapshot _snapshot;
        private bool _binding;
        private bool _busy;

        internal LeagueDynamicMessagePanel(LeagueEfficiencyModule module, UiTextCatalog ui, CancellationToken token)
        {
            _module = module; _ui = ui;
            _options = LeagueDynamicMessageOptions.Parse(module.DynamicMessageOptionsJson);
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Dock = DockStyle.Fill; BackColor = FacmDesignSystem.Canvas; ForeColor = FacmDesignSystem.Text;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12), AutoScroll = true };
            foreach (var height in new[] { 38, 40, 115, 42 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            _target = Choice(new[] { T(LeagueEfficiencyUiTextKeys.DynamicFriendly), T(LeagueEfficiencyUiTextKeys.DynamicEnemy), T(LeagueEfficiencyUiTextKeys.DynamicAll) });
            _target.SelectedIndex = Array.IndexOf(new[] { "friendly", "enemy", "all" }, _options.Target);
            _names = Choice(new[] { T(LeagueEfficiencyUiTextKeys.DynamicNames), T(LeagueEfficiencyUiTextKeys.DynamicChampionNames), T(LeagueEfficiencyUiTextKeys.DynamicCombinedNames) });
            _names.SelectedIndex = Array.IndexOf(new[] { "preferName", "preferChampionName", "championNameWithName" }, _options.NameStrategy);
            choices.Controls.Add(_target); choices.Controls.Add(_names);
            choices.Controls.Add(new Label { AutoSize = true,
                Text = T(LeagueEfficiencyUiTextKeys.OngoingHistoryCount),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 7, 0, 0) });
            var historyCount = new NumericUpDown { Width = 58, Minimum = 2,
                Maximum = LeaguePlayerDataService.MaximumMatchCount,
                Value = _options.MatchHistoryLoadCount };
            historyCount.ValueChanged += delegate
            {
                if (_binding) return;
                _options.MatchHistoryLoadCount = (int)historyCount.Value;
                _snapshot = null;
                _binding = true;
                try { _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear(); }
                finally { _binding = false; }
                _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
                RefreshPreview();
            };
            choices.Controls.Add(historyCount);
            choices.Controls.Add(new Label { AutoSize = true,
                Text = T(LeagueEfficiencyUiTextKeys.OngoingHistoryConcurrency),
                Padding = new Padding(4, 7, 0, 0) });
            var concurrency = new NumericUpDown { Width = 48, Minimum = 1,
                Maximum = 4, Value = _options.HistoryConcurrency };
            concurrency.ValueChanged += delegate
            {
                if (_binding) return;
                _options.HistoryConcurrency = (int)concurrency.Value;
                _snapshot = null;
                _binding = true;
                try { _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear(); }
                finally { _binding = false; }
                _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
                RefreshPreview();
            };
            choices.Controls.Add(concurrency);
            choices.Controls.Add(new Label { AutoSize = true,
                Text = T(LeagueEfficiencyUiTextKeys.OngoingGameDetailsCount),
                Padding = new Padding(4, 7, 0, 0) });
            var detailsCount = new NumericUpDown { Width = 58, Minimum = 0, Maximum = 20,
                Value = _options.GameDetailsLoadCount };
            detailsCount.ValueChanged += delegate
            {
                if (_binding) return;
                _options.GameDetailsLoadCount = (int)detailsCount.Value;
            };
            choices.Controls.Add(detailsCount);
            var historyTag = Choice(new[] { T(LeagueEfficiencyUiTextKeys.OngoingHistoryAllQueues),
                T(LeagueEfficiencyUiTextKeys.OngoingHistoryCurrentQueue) });
            historyTag.SelectedIndex = _options.MatchHistoryTagPreference == "current" ? 1 : 0;
            historyTag.SelectedIndexChanged += delegate
            {
                if (_binding) return;
                _options.MatchHistoryTagPreference = historyTag.SelectedIndex == 1 ? "current" : "all";
                _snapshot = null;
                _binding = true;
                try { _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear(); }
                finally { _binding = false; }
                _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
                RefreshPreview();
            };
            choices.Controls.Add(historyTag);
            var orderModes = new[] { "default", "win-rate", "kda", "akari-score", "position", "premade-team" };
            var order = Choice(new[] { T(LeagueEfficiencyUiTextKeys.OngoingOrderDefault),
                T(LeagueEfficiencyUiTextKeys.DynamicWinRate), T(LeagueEfficiencyUiTextKeys.DynamicKda),
                T(LeagueEfficiencyUiTextKeys.OngoingAkariScore),
                T(LeagueEfficiencyUiTextKeys.DynamicPositions),
                T(LeagueEfficiencyUiTextKeys.OngoingPremadeTeam) });
            order.SelectedIndex = Math.Max(0, Array.IndexOf(orderModes, _options.OrderPlayerBy));
            order.SelectedIndexChanged += delegate
            {
                if (_binding) return;
                _options.OrderPlayerBy = orderModes[Math.Max(0, order.SelectedIndex)];
                ReorderPlayers();
            };
            choices.Controls.Add(new Label { AutoSize = true,
                Text = T(LeagueEfficiencyUiTextKeys.OngoingOrderPlayers),
                Padding = new Padding(4, 7, 0, 0) });
            choices.Controls.Add(order);
            var lobbyQuery = new CheckBox { AutoSize = true,
                Text = T(LeagueEfficiencyUiTextKeys.OngoingQueryInLobby),
                Checked = _options.QueryInLobbyPhase };
            lobbyQuery.CheckedChanged += delegate
            {
                if (_binding) return;
                _options.QueryInLobbyPhase = lobbyQuery.Checked;
                _snapshot = null;
                _binding = true;
                try { _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear(); }
                finally { _binding = false; }
                _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
                RefreshPreview();
            };
            choices.Controls.Add(lobbyQuery);
            var thresholdLabel = new Label { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.PremadeThreshold),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 7, 0, 0), Visible = false };
            var threshold = new NumericUpDown { Width = 58, Minimum = 2,
                Maximum = LeaguePlayerDataService.MaximumMatchCount, Value = _options.PremadeMatchThreshold, Visible = false };
            threshold.ValueChanged += delegate
            {
                if (_binding) return;
                _options.PremadeMatchThreshold = (int)threshold.Value;
                if (_snapshot != null)
                {
                    _binding = true;
                    try { RebuildPremadeGroups(_snapshot, true); }
                    finally { _binding = false; }
                }
                RefreshPreview();
            };
            choices.Controls.Add(thresholdLabel); choices.Controls.Add(threshold);
            var champion = new CheckBox { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicCurrentChampion), Checked = _options.ShowCurrentChampion };
            champion.CheckedChanged += delegate
            {
                if (_binding) return;
                if (IsJungle) _options.JungleShowCurrentChampion = champion.Checked;
                else _options.ShowCurrentChampion = champion.Checked;
                RefreshPreview();
            };
            choices.Controls.Add(champion); root.Controls.Add(choices, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
            _load = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicLoad), Tone = FacmButtonTone.Secondary };
            _loadJungle = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.JungleLoad),
                Enabled = false, Visible = false, Tone = FacmButtonTone.Secondary };
            _send = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicSend), Enabled = false, Tone = FacmButtonTone.Primary };
            var save = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.AdvancedSave), Tone = FacmButtonTone.Secondary };
            var tagTools = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicTagTools),
                Tone = FacmButtonTone.Secondary };
            _exchangeMenu = new ContextMenuStrip();
            _exchangeMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagBrowse)).Click += (sender, e) => BrowseTags();
            var tagVisibility = new ToolStripMenuItem(T(LeagueEfficiencyUiTextKeys.OngoingTagDisplay));
            _exchangeMenu.Items.Add(tagVisibility);
            var championUsage = new ToolStripMenuItem(_ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions));
            _exchangeMenu.Items.Add(championUsage);
            foreach (var mode in new[] { "none", "recent", "mastery" })
            {
                var selectedMode = mode;
                var label = mode == "none" ? T(LeagueEfficiencyUiTextKeys.Disabled) :
                    mode == "recent" ? _ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions) :
                    _ui.Get(UiTextKeys.LeagueProfileMastery);
                var item = new ToolStripMenuItem(label) {
                    Checked = _options.ChampionUsageMode == selectedMode
                };
                championUsage.DropDownItems.Add(item);
                item.Click += (sender, e) =>
                {
                    _options.ChampionUsageMode = selectedMode;
                    foreach (ToolStripMenuItem option in championUsage.DropDownItems)
                        option.Checked = ReferenceEquals(option, item);
                    RefreshPreview();
                };
            }
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingSelf,
                () => _options.ShowSelfTag, value => _options.ShowSelfTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingTagged,
                () => _options.ShowTaggedTag, value => _options.ShowTaggedTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingMet,
                () => _options.ShowMetTag, value => _options.ShowMetTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingPremadeTeam,
                () => _options.ShowPremadeTeamTag, value => _options.ShowPremadeTeamTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingHighWinRate,
                () => _options.ShowWinRateTeamTag, value => _options.ShowWinRateTeamTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingWinStreak,
                () => _options.ShowWinningStreakTag, value => _options.ShowWinningStreakTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingLossStreak,
                () => _options.ShowLosingStreakTag, value => _options.ShowLosingStreakTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingMixedFlash,
                () => _options.ShowSuspiciousFlashPositionTag, value => _options.ShowSuspiciousFlashPositionTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicSoloKills,
                () => _options.ShowSoloKillsTag, value => _options.ShowSoloKillsTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicDamage,
                () => _options.ShowAverageTeamDamageTag, value => _options.ShowAverageTeamDamageTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicTaken,
                () => _options.ShowAverageTeamDamageTakenTag, value => _options.ShowAverageTeamDamageTakenTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicGold,
                () => _options.ShowAverageTeamGoldTag, value => _options.ShowAverageTeamGoldTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicCs,
                () => _options.ShowAverageCsPerMinuteTag, value => _options.ShowAverageCsPerMinuteTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicEfficiency,
                () => _options.ShowAverageDamageGoldEfficiencyTag, value => _options.ShowAverageDamageGoldEfficiencyTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingKillDamageTag,
                () => _options.ShowAverageKillDamageEfficiencyTag,
                value => _options.ShowAverageKillDamageEfficiencyTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingMissingPings,
                () => _options.ShowAverageEnemyMissingPingsTag, value => _options.ShowAverageEnemyMissingPingsTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.DynamicVision,
                () => _options.ShowAverageVisionScoreTag, value => _options.ShowAverageVisionScoreTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingGreatPerformance,
                () => _options.ShowGreatPerformanceTag, value => _options.ShowGreatPerformanceTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingAkariScore,
                () => _options.ShowAkariScoreTag, value => _options.ShowAkariScoreTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingPrivacyTag,
                () => _options.ShowPrivacyTag, value => _options.ShowPrivacyTag = value);
            AddTagChoice(tagVisibility, LeagueEfficiencyUiTextKeys.OngoingEasyGankTag,
                () => _options.ShowEasyGankTag, value => _options.ShowEasyGankTag = value);
            _exchangeMenu.Items.Add(new ToolStripSeparator());
            _exchangeMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.OngoingEasyGankAnalyze))
                .Click += async (sender, e) => await AnalyzeEasyGankAsync();
            _exchangeMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.OngoingPrivacyAnalyze))
                .Click += async (sender, e) => await AnalyzePrivacyAsync();
            _exchangeMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagImport)).Click += (sender, e) => ImportTags();
            _exchangeMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagExport)).Click += (sender, e) => ExportTags();
            tagTools.Click += (sender, e) => _exchangeMenu.Show(tagTools, new Point(0, tagTools.Height));
            _load.Click += async delegate { await LoadAsync(); };
            _loadJungle.Click += async delegate { await LoadJungleAsync(); };
            _send.Click += async delegate { await SendAsync(); };
            save.Click += delegate
            {
                string error;
                _status.Text = _module.TrySaveDynamicMessageOptions(_options, out error) ? T(LeagueEfficiencyUiTextKeys.DynamicSaved) : error;
            };
            actions.Controls.Add(_load); actions.Controls.Add(_loadJungle); actions.Controls.Add(save);
            actions.Controls.Add(tagTools); actions.Controls.Add(_send);
            root.Controls.Add(actions, 0, 1);
            var metrics = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
            var properties = new[] { "WinRate", "Kda", "SoloKills", "VisionScore", "ChampionDamageShare", "DamageTakenShare",
                "GoldShare", "CsPerMinute", "KillParticipation", "DamageGoldEfficiency", "MainChampions", "MainPositions" };
            var keys = new[] { LeagueEfficiencyUiTextKeys.DynamicWinRate, LeagueEfficiencyUiTextKeys.DynamicKda,
                LeagueEfficiencyUiTextKeys.DynamicSoloKills, LeagueEfficiencyUiTextKeys.DynamicVision, LeagueEfficiencyUiTextKeys.DynamicDamage,
                LeagueEfficiencyUiTextKeys.DynamicTaken, LeagueEfficiencyUiTextKeys.DynamicGold, LeagueEfficiencyUiTextKeys.DynamicCs,
                LeagueEfficiencyUiTextKeys.DynamicParticipation, LeagueEfficiencyUiTextKeys.DynamicEfficiency,
                LeagueEfficiencyUiTextKeys.DynamicChampions, LeagueEfficiencyUiTextKeys.DynamicPositions };
            for (var index = 0; index < properties.Length; index++)
            {
                var property = typeof(LeagueDynamicMessageOptions).GetProperty(properties[index]);
                var check = new CheckBox { AutoSize = true, Text = T(keys[index]), Checked = (bool)property.GetValue(_options, null) };
                check.CheckedChanged += delegate { property.SetValue(_options, check.Checked, null); RefreshPreview(); };
                metrics.Controls.Add(check);
            }
            var metricsHost = new Panel { Dock = DockStyle.Fill };
            metricsHost.Controls.Add(metrics);
            var jungleMetrics = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Visible = false };
            var jungleProperties = new[] { "JungleActivityPreference", "JungleFirstClearDistribution", "JungleEarlyGank",
                "JungleDragonControl", "JungleMonsterControl", "JungleMainChampions" };
            var jungleKeys = new[] { LeagueEfficiencyUiTextKeys.JungleActivity, LeagueEfficiencyUiTextKeys.JungleFirstClear,
                LeagueEfficiencyUiTextKeys.JungleEarlyGank, LeagueEfficiencyUiTextKeys.JungleDragon,
                LeagueEfficiencyUiTextKeys.JungleMonsters, LeagueEfficiencyUiTextKeys.JungleChampions };
            for (var index = 0; index < jungleProperties.Length; index++)
            {
                var property = typeof(LeagueDynamicMessageOptions).GetProperty(jungleProperties[index]);
                var check = new CheckBox { AutoSize = true, Text = T(jungleKeys[index]), Checked = (bool)property.GetValue(_options, null) };
                check.CheckedChanged += delegate { property.SetValue(_options, check.Checked, null); RefreshPreview(); };
                jungleMetrics.Controls.Add(check);
            }
            metricsHost.Controls.Add(jungleMetrics);
            root.Controls.Add(metricsHost, 0, 2);
            var shortcuts = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
            for (var index = 0; index < 3; index++)
            {
                var target = index;
                var label = new[] { T(LeagueEfficiencyUiTextKeys.DynamicFriendly), T(LeagueEfficiencyUiTextKeys.DynamicEnemy), T(LeagueEfficiencyUiTextKeys.DynamicAll) }[index];
                var button = new FacmActionButton { Width = 170, Height = 30, Tone = FacmButtonTone.Secondary };
                _shortcutButtons[index] = button;
                Action refresh = () => button.Text = label + "：" + (string.IsNullOrEmpty(Shortcut(target)) ? T(LeagueEfficiencyUiTextKeys.Disabled) : Shortcut(target));
                refresh();
                button.Click += delegate
                {
                    using (var capture = new LeagueHotkeyCaptureDialog(_ui))
                        if (capture.ShowDialog(this) == DialogResult.OK && capture.Binding != null)
                        { SetShortcut(target, capture.Binding.ToString()); refresh(); }
                };
                button.MouseUp += (sender, e) => { if (e.Button == MouseButtons.Right) { SetShortcut(target, ""); refresh(); } };
                shortcuts.Controls.Add(button);
            }
            root.Controls.Add(shortcuts, 0, 3);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _players = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text, HorizontalScrollbar = true,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicSelectPlayers) };
            _groups = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text, HorizontalScrollbar = true,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.PremadeSelectGroups) };
            _jungle = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text, HorizontalScrollbar = true,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.JungleTab) };
            var tagMenu = new ContextMenuStrip();
            _tagMenu = tagMenu;
            var editTag = tagMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagEdit));
            var removeTag = tagMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicTagRemove));
            var encountered = tagMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.DynamicEncounterHistory));
            var recentChampions = tagMenu.Items.Add(_ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions));
            var championMastery = tagMenu.Items.Add(_ui.Get(UiTextKeys.LeagueProfileMastery));
            var recentHistory = tagMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.OngoingRecentHistory));
            var enrichHistory = tagMenu.Items.Add(T(LeagueEfficiencyUiTextKeys.OngoingHydrateHistory));
            _players.ContextMenuStrip = tagMenu;
            _jungle.ContextMenuStrip = tagMenu;
            _players.MouseDown += (sender, e) => SelectContextPlayer(_players, e);
            _jungle.MouseDown += (sender, e) => SelectContextPlayer(_jungle, e);
            tagMenu.Opening += (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                e.Cancel = _busy || _snapshot == null || choice == null || choice.Player == null ||
                    choice.Player.Player == null || !LeagueDynamicMessageService.SafePuuid(choice.Player.Player.PuuId);
                var isSelf = !e.Cancel && string.Equals(choice.Player.Player.PuuId,
                    _snapshot.SelfPuuid, StringComparison.Ordinal);
                editTag.Enabled = !e.Cancel && !isSelf;
                removeTag.Enabled = !e.Cancel && !isSelf && !string.IsNullOrEmpty(choice.Tag);
                encountered.Enabled = !e.Cancel && !isSelf;
                recentChampions.Visible = _options.ChampionUsageMode == "recent";
                championMastery.Visible = _options.ChampionUsageMode == "mastery";
                recentChampions.Enabled = !e.Cancel &&
                    LeagueHistoricalMatchDryRun.RecentChampionUsage(choice.Player).Count > 0;
                championMastery.Enabled = !e.Cancel;
                recentHistory.Enabled = !e.Cancel &&
                    LeagueHistoricalMatchDryRun.CardRecentMatches(choice.Player).Count > 0;
                enrichHistory.Enabled = !e.Cancel && _options.GameDetailsLoadCount > 0 &&
                    choice.Player.History != null &&
                    choice.Player.History.Matches.Any(m =>
                        LeaguePlayerDataService.NeedsAnalysisDetail(m));
            };
            recentHistory.Click += (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                var snapshot = _snapshot;
                if (_busy || snapshot == null || choice?.Player == null ||
                    !snapshot.Players.Contains(choice.Player)) return;
                using (var dialog = new LeagueOngoingPlayerHistoryDialog(_ui, snapshot,
                    choice.Player, () => !IsDisposed && ReferenceEquals(_snapshot, snapshot),
                    (match, token) => _module.LoadDynamicPlayerMatchDetailAsync(snapshot,
                        choice.Player.Player.PuuId, match, token),
                    (asset, token) => _module.LoadEncounterAssetAsync(asset, token),
                    (startIndex, token) => _module.LoadDynamicHistoryPageAsync(snapshot,
                        choice.Player.Player.PuuId, startIndex, token)))
                    dialog.ShowDialog(this);
            };
            enrichHistory.Click += async (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                var snapshot = _snapshot;
                if (_busy || snapshot == null || choice?.Player?.Player == null ||
                    !snapshot.Players.Contains(choice.Player)) return;
                _busy = true; _load.Enabled = false; _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicLoading);
                try
                {
                    var updated = await _module.HydrateDynamicPlayerHistoryAsync(snapshot,
                        choice.Player.Player.PuuId, _lifetime.Token, _options.GameDetailsLoadCount);
                    if (IsDisposed || !ReferenceEquals(_snapshot, snapshot)) return;
                    if (updated)
                    {
                        foreach (var row in _players.Items.Cast<PlayerChoice>()
                            .Concat(_jungle.Items.Cast<PlayerChoice>())
                            .Where(p => ReferenceEquals(p.Player, choice.Player)))
                            row.RefreshSourceTags();
                        _players.Refresh(); _jungle.Refresh();
                        RefreshPreview();
                    }
                    _status.Text = T(updated ? LeagueEfficiencyUiTextKeys.DynamicReady :
                        LeagueEfficiencyUiTextKeys.DynamicPartial);
                }
                catch (OperationCanceledException) { }
                catch (Exception)
                {
                    if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable);
                }
                finally
                {
                    if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); }
                }
            };
            editTag.Click += async (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                if (choice == null) return;
                var snapshot = _snapshot;
                using (var dialog = CreateTagDialog(choice.Tag))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var input = dialog.Controls.OfType<TextBox>().First();
                    await SetPlayerTagAsync(snapshot, choice, input.Text);
                }
            };
            removeTag.Click += async (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                if (choice != null) await SetPlayerTagAsync(_snapshot, choice, string.Empty);
            };
            encountered.Click += (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                if (choice != null) ShowEncounterHistory(_snapshot, choice);
            };
            championMastery.Click += async (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                if (_busy || _snapshot == null || choice == null || choice.Player == null) return;
                var snapshot = _snapshot;
                _busy = true;
                _load.Enabled = false; _send.Enabled = false;
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicLoading);
                try
                {
                    var ready = await _module.LoadDynamicMasteryAsync(snapshot,
                        choice.Player.Player.PuuId, _lifetime.Token);
                    if (IsDisposed || !ReferenceEquals(_snapshot, snapshot)) return;
                    if (!ready || choice.Player.Mastery == null)
                    {
                        _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable);
                        return;
                    }
                    var names = LeagueHistoricalMatchDryRun.RecentChampionUsage(choice.Player)
                        .Where(c => !string.IsNullOrWhiteSpace(c.ChampionName))
                        .ToDictionary(c => c.ChampionId, c => c.ChampionName);
                    var rows = choice.Player.Mastery.Masteries.Select(row =>
                    {
                        var id = LeagueMatchTimelineParser.Number(row, "championId");
                        var level = LeagueMatchTimelineParser.Number(row, "championLevel");
                        var points = LeagueMatchTimelineParser.Number(row, "championPoints");
                        string name;
                        return (id.HasValue && id.Value > 0 && id.Value <= int.MaxValue &&
                            names.TryGetValue((int)id.Value, out name) ? name :
                            "#" + (id.HasValue ? id.Value.ToString() : "—")) +
                            " · " + _ui.Get(UiTextKeys.LeagueProfileMasteryLevel) + " " +
                            (level.HasValue ? level.Value.ToString() : "—") + " · " +
                            (points.HasValue ? points.Value.ToString("N0") : "—");
                    });
                    var content = rows.ToArray();
                    MessageBox.Show(this, content.Length == 0 ? "—" :
                            string.Join(Environment.NewLine, content),
                        _ui.Get(UiTextKeys.LeagueProfileMastery),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicReady);
                }
                catch (OperationCanceledException) { }
                catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable); }
                finally
                {
                    if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); }
                }
            };
            recentChampions.Click += (sender, e) =>
            {
                var choice = (tagMenu.SourceControl as CheckedListBox)?.SelectedItem as PlayerChoice;
                if (choice == null || _snapshot == null || _busy ||
                    !_snapshot.Players.Contains(choice.Player)) return;
                var champions = LeagueHistoricalMatchDryRun.RecentChampionUsage(choice.Player);
                if (champions.Count == 0) return;
                var lines = champions.Select(c =>
                    (string.IsNullOrWhiteSpace(c.ChampionName) ? "#" + c.ChampionId : c.ChampionName) +
                    " · " + c.Games + " · " + c.WinRate.ToString("0") + "%");
                MessageBox.Show(this, string.Join(Environment.NewLine, lines),
                    _ui.Get(UiTextKeys.LeaguePlayerAnalysisTopChampions),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _selectionTabs = new TabControl { Dock = DockStyle.Fill };
            var playerTab = new TabPage(T(LeagueEfficiencyUiTextKeys.DynamicPlayer));
            playerTab.Controls.Add(_players);
            var groupTab = new TabPage(T(LeagueEfficiencyUiTextKeys.PremadeSelectGroups));
            groupTab.Controls.Add(_groups);
            var jungleTab = new TabPage(T(LeagueEfficiencyUiTextKeys.JungleTab));
            jungleTab.Controls.Add(_jungle);
            _selectionTabs.TabPages.Add(playerTab); _selectionTabs.TabPages.Add(groupTab); _selectionTabs.TabPages.Add(jungleTab);
            _preview = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text };
            _players.ItemCheck += delegate { if (!_binding && IsHandleCreated) BeginInvoke(new Action(RefreshPreview)); };
            _groups.ItemCheck += delegate { if (!_binding && IsHandleCreated) BeginInvoke(new Action(RefreshPreview)); };
            _jungle.ItemCheck += delegate { if (!_binding && IsHandleCreated) BeginInvoke(new Action(RefreshPreview)); };
            _selectionTabs.SelectedIndexChanged += delegate
            {
                _binding = true;
                var strategy = IsPremade ? _options.PremadeNameStrategy : IsJungle ? _options.JungleNameStrategy : _options.NameStrategy;
                _names.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "preferName", "preferChampionName", "championNameWithName" }, strategy));
                champion.Checked = IsJungle ? _options.JungleShowCurrentChampion : _options.ShowCurrentChampion;
                metrics.Visible = !IsJungle;
                jungleMetrics.Visible = IsJungle;
                _loadJungle.Visible = IsJungle;
                thresholdLabel.Visible = IsPremade;
                threshold.Visible = IsPremade;
                _binding = false;
                RefreshShortcuts();
                RefreshPreview();
            };
            content.Controls.Add(_selectionTabs, 0, 0); content.Controls.Add(_preview, 1, 0); root.Controls.Add(content, 0, 4);
            _status = new Label { Dock = DockStyle.Fill, Text = T(LeagueEfficiencyUiTextKeys.DynamicShortcutHint), AutoEllipsis = true };
            root.Controls.Add(_status, 0, 5); Controls.Add(root);
            _module.DynamicMessageContextChanged += HandleContextChanged;
            _target.SelectedIndexChanged += delegate { _options.Target = new[] { "friendly", "enemy", "all" }[Math.Max(0, _target.SelectedIndex)]; RefreshPreview(); };
            _names.SelectedIndexChanged += delegate
            {
                if (_binding) return;
                var strategy = new[] { "preferName", "preferChampionName", "championNameWithName" }[Math.Max(0, _names.SelectedIndex)];
                if (IsPremade) _options.PremadeNameStrategy = strategy;
                else if (IsJungle) _options.JungleNameStrategy = strategy;
                else _options.NameStrategy = strategy;
                RefreshPreview();
            };
        }

        private bool IsPremade { get { return _selectionTabs.SelectedIndex == 1; } }
        private bool IsJungle { get { return _selectionTabs.SelectedIndex == 2; } }

        private ComboBox Choice(string[] values)
        {
            var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            choice.Items.AddRange(values); return choice;
        }

        private async Task LoadAsync()
        {
            if (_busy) return;
            var recover = _snapshot != null && DateTime.UtcNow - _snapshot.CreatedUtc < TimeSpan.FromMinutes(2) &&
                LeagueDynamicMessageService.TargetNeedsHistory(_snapshot, _options.Target);
            _busy = true;
            _load.Enabled = false; _loadJungle.Enabled = false; _send.Enabled = false;
            _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicLoading);
            try
            {
                if (recover)
                {
                    var current = _snapshot;
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(110));
                        var completed = await _module.CompleteDynamicHistoriesAsync(current,
                            LeagueDynamicMessageService.TargetPlayers(current, _options.Target), deadline.Token);
                        if (IsDisposed || !ReferenceEquals(_snapshot, current)) return;
                        if (completed)
                        {
                            _binding = true;
                            try
                            {
                                RebuildPremadeGroups(current, true);
                                foreach (var choice in _players.Items.Cast<PlayerChoice>()
                                    .Concat(_jungle.Items.Cast<PlayerChoice>())) choice.RefreshSourceTags();
                                _players.Refresh();
                                _jungle.Refresh();
                            }
                            finally { _binding = false; }
                        }
                        _status.Text = T(completed ? LeagueEfficiencyUiTextKeys.DynamicReady :
                            LeagueEfficiencyUiTextKeys.DynamicPartial);
                    }
                    return;
                }
                var requestedHistoryCount = _options.MatchHistoryLoadCount;
                var requestedTagPreference = _options.MatchHistoryTagPreference;
                var requestedLobbyQuery = _options.QueryInLobbyPhase;
                var requestedConcurrency = _options.HistoryConcurrency;
                var snapshot = await _module.LoadDynamicMessagesAsync(_lifetime.Token,
                    requestedHistoryCount, requestedTagPreference, requestedLobbyQuery, requestedConcurrency);
                if (IsDisposed || requestedHistoryCount != _options.MatchHistoryLoadCount ||
                    requestedTagPreference != _options.MatchHistoryTagPreference ||
                    requestedLobbyQuery != _options.QueryInLobbyPhase ||
                    requestedConcurrency != _options.HistoryConcurrency) return;
                _snapshot = snapshot; _binding = true; _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear();
                var selected = _module.DynamicSelection(snapshot);
                if (snapshot != null)
                {
                    IReadOnlyDictionary<string, string> tags;
                    try { tags = _module.ReadPlayerTags(snapshot); }
                    catch (System.IO.InvalidDataException) { tags = new Dictionary<string, string>(StringComparer.Ordinal); }
                    catch (System.IO.IOException) { tags = new Dictionary<string, string>(StringComparer.Ordinal); }
                    catch (System.UnauthorizedAccessException) { tags = new Dictionary<string, string>(StringComparer.Ordinal); }
                    IReadOnlyDictionary<string, int> encounters;
                    try { encounters = _module.ReadPlayerEncounterCounts(snapshot); }
                    catch (System.IO.InvalidDataException) { encounters = new Dictionary<string, int>(StringComparer.Ordinal); }
                    catch (System.IO.IOException) { encounters = new Dictionary<string, int>(StringComparer.Ordinal); }
                    catch (System.UnauthorizedAccessException) { encounters = new Dictionary<string, int>(StringComparer.Ordinal); }
                    foreach (var player in LeagueDynamicMessageService.OrderPlayers(snapshot, _options.OrderPlayerBy,
                        _options.PremadeMatchThreshold))
                    {
                        string tag;
                        int count;
                        tags.TryGetValue(player.Player.PuuId, out tag);
                        encounters.TryGetValue(player.Player.PuuId, out count);
                        var self = string.Equals(player.Player.PuuId, snapshot.SelfPuuid, StringComparison.Ordinal);
                        _players.Items.Add(new PlayerChoice(player, _options) { Tag = tag, IsSelf = self, EncounterCount = count },
                            selected == null || selected.Contains(player.Player.PuuId));
                        _jungle.Items.Add(new PlayerChoice(player, _options) { Tag = tag, IsSelf = self, EncounterCount = count }, false);
                    }
                    RebuildPremadeGroups(snapshot, false);
                }
                _binding = false; RefreshPreview();
                _status.Text = T(snapshot == null ? LeagueEfficiencyUiTextKeys.DynamicUnavailable :
                    snapshot.UnavailableIdentityCount > 0 || snapshot.Players.Any(p => p.History == null) ? LeagueEfficiencyUiTextKeys.DynamicPartial :
                    snapshot.Phase == "InProgress" ? LeagueEfficiencyUiTextKeys.DynamicGameHint : LeagueEfficiencyUiTextKeys.DynamicReady);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable); }
            finally { if (!IsDisposed) { _busy = false; _binding = false; _load.Enabled = true; RefreshPreview(); } }
        }

        private void ReorderPlayers()
        {
            if (_snapshot == null || _players == null || _jungle == null) return;
            var primary = _players.Items.Cast<PlayerChoice>().ToDictionary(
                p => p.Player.Player.PuuId, StringComparer.Ordinal);
            var jungle = _jungle.Items.Cast<PlayerChoice>().ToDictionary(
                p => p.Player.Player.PuuId, StringComparer.Ordinal);
            var selected = new HashSet<string>(_players.CheckedItems.Cast<PlayerChoice>()
                .Select(p => p.Player.Player.PuuId), StringComparer.Ordinal);
            var jungleSelected = new HashSet<string>(_jungle.CheckedItems.Cast<PlayerChoice>()
                .Select(p => p.Player.Player.PuuId), StringComparer.Ordinal);
            _binding = true;
            try
            {
                _players.Items.Clear();
                _jungle.Items.Clear();
                foreach (var player in LeagueDynamicMessageService.OrderPlayers(_snapshot,
                    _options.OrderPlayerBy, _options.PremadeMatchThreshold))
                {
                    var id = player.Player.PuuId;
                    PlayerChoice choice;
                    if (primary.TryGetValue(id, out choice)) _players.Items.Add(choice, selected.Contains(id));
                    if (jungle.TryGetValue(id, out choice)) _jungle.Items.Add(choice, jungleSelected.Contains(id));
                }
            }
            finally { _binding = false; }
            RefreshPreview();
        }

        private void RebuildPremadeGroups(LeagueDynamicMessageSnapshot snapshot, bool preserveSelection)
        {
            var existing = preserveSelection ? new HashSet<string>(_groups.Items.Cast<PremadeChoice>()
                .Select(g => g.Key), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var checkedGroups = preserveSelection ? SelectedGroups() : new HashSet<string>(StringComparer.Ordinal);
            _groups.Items.Clear();
            var premadeLabels = LeaguePremadePlayerTagProjection.Project(snapshot, _options.PremadeMatchThreshold);
            foreach (var choice in _players.Items.Cast<PlayerChoice>().Concat(_jungle.Items.Cast<PlayerChoice>()))
            {
                string label;
                choice.PremadeGroup = _options.ShowPremadeTeamTag && premadeLabels.TryGetValue(choice.Player.Player.PuuId, out label) ? label : null;
            }
            _players.Refresh();
            _jungle.Refresh();
            var byId = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId))
                .GroupBy(p => p.Player.PuuId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var group in LeaguePremadeInference.Infer(snapshot, _options.PremadeMatchThreshold)
                .OrderByDescending(g => byId[g[0]].Friendly))
            {
                var classification = LeaguePremadeTeamTagProjection.Classify(group.Select(id => byId[id]).ToArray());
                var tag = classification == LeaguePremadeTeamTagProjection.WinRateTeam ?
                    T(LeagueEfficiencyUiTextKeys.PremadeWinRateTeam) :
                    classification == LeaguePremadeTeamTagProjection.LossRateTeam ?
                    T(LeagueEfficiencyUiTextKeys.PremadeLossRateTeam) : null;
                var aggregate = LeaguePremadeTeamAggregate.Project(group.Select(id => byId[id]).ToArray());
                var summary = aggregate == null ? null :
                    (aggregate.Kda.HasValue ? T(LeagueEfficiencyUiTextKeys.PremadeTeamStats) :
                        T(LeagueEfficiencyUiTextKeys.PremadeTeamWinRate))
                    .Replace("{0}", Math.Round(100d * aggregate.Wins / aggregate.Games).ToString("0"))
                    .Replace("{1}", aggregate.Kda.HasValue ? aggregate.Kda.Value.ToString("0.00") : string.Empty);
                var choice = new PremadeChoice(group, byId, T(LeagueEfficiencyUiTextKeys.PremadeGroupCount), tag, summary);
                _groups.Items.Add(choice, !existing.Contains(choice.Key) || checkedGroups.Contains(choice.Key));
            }
        }

        private HashSet<string> Selected()
        {
            return new HashSet<string>(_players.CheckedItems.Cast<PlayerChoice>().Select(p => p.Player.Player.PuuId), StringComparer.Ordinal);
        }

        private HashSet<string> SelectedGroups()
        {
            return new HashSet<string>(_groups.CheckedItems.Cast<PremadeChoice>().Select(g => g.Key), StringComparer.Ordinal);
        }

        private HashSet<string> SelectedJungle()
        {
            return new HashSet<string>(_jungle.CheckedItems.Cast<PlayerChoice>().Select(p => p.Player.Player.PuuId), StringComparer.Ordinal);
        }

        private async Task LoadJungleAsync()
        {
            if (_busy || _snapshot == null || !IsJungle) return;
            var snapshot = _snapshot;
            var next = _jungle.CheckedItems.Cast<PlayerChoice>().FirstOrDefault(choice => choice.Player.JungleSamples == null);
            if (next == null) return;
            _busy = true;
            _load.Enabled = false; _loadJungle.Enabled = false; _send.Enabled = false;
            _status.Text = T(LeagueEfficiencyUiTextKeys.JungleLoading);
            try
            {
                var loaded = await _module.LoadJungleMessagesAsync(snapshot, next.Player.Player.PuuId, _lifetime.Token);
                if (!IsDisposed && ReferenceEquals(_snapshot, snapshot))
                    _status.Text = T(loaded ? LeagueEfficiencyUiTextKeys.DynamicReady : LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable); }
            finally { if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); } }
        }

        private async Task AnalyzePrivacyAsync()
        {
            if (_busy || _snapshot == null || IsJungle || IsPremade) return;
            var selected = _players.SelectedItem as PlayerChoice;
            if (selected == null || selected.Player == null ||
                (selected.Player.PrivacyLoaded && selected.Player.IsPrivate.HasValue)) return;
            var snapshot = _snapshot;
            _busy = true;
            _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicLoading);
            _load.Enabled = false; _send.Enabled = false;
            try
            {
                var completed = await _module.LoadPrivacyMessagesAsync(snapshot,
                    selected.Player.Player.PuuId, _lifetime.Token);
                if (IsDisposed || !ReferenceEquals(_snapshot, snapshot)) return;
                if (completed)
                {
                    foreach (var choice in _players.Items.Cast<PlayerChoice>()
                        .Concat(_jungle.Items.Cast<PlayerChoice>())) choice.RefreshSourceTags();
                    _players.Refresh();
                    _jungle.Refresh();
                }
                _status.Text = T(completed ? LeagueEfficiencyUiTextKeys.DynamicReady :
                    LeagueEfficiencyUiTextKeys.DynamicPartial);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable);
            }
            finally
            {
                if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); }
            }
        }

        private async Task AnalyzeEasyGankAsync()
        {
            if (_busy || _snapshot == null || IsJungle || IsPremade) return;
            var selected = _players.SelectedItem as PlayerChoice;
            if (selected == null || selected.Player == null ||
                selected.Player.EarlyGankLoaded) return;
            var snapshot = _snapshot;
            _busy = true;
            _status.Text = T(LeagueEfficiencyUiTextKeys.JungleLoading);
            _load.Enabled = false; _send.Enabled = false;
            try
            {
                var completed = await _module.LoadEarlyGankMessagesAsync(snapshot,
                    selected.Player.Player.PuuId, _lifetime.Token);
                if (IsDisposed || !ReferenceEquals(_snapshot, snapshot)) return;
                if (completed)
                {
                    foreach (var choice in _players.Items.Cast<PlayerChoice>()
                        .Concat(_jungle.Items.Cast<PlayerChoice>())) choice.RefreshSourceTags();
                    _players.Refresh();
                    _jungle.Refresh();
                }
                _status.Text = T(completed ? LeagueEfficiencyUiTextKeys.DynamicReady :
                    LeagueEfficiencyUiTextKeys.DynamicPartial);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable);
            }
            finally
            {
                if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); }
            }
        }

        private void RefreshPreview()
        {
            if (_binding || IsDisposed || _preview == null) return;
            var lines = IsPremade ? LeaguePremadeMessageFormatter.Build(_snapshot, _options, SelectedGroups()) : IsJungle ?
                LeagueJungleMessageFormatter.Build(_snapshot, _options, SelectedJungle()) :
                LeagueDynamicMessageFormatter.Build(_snapshot, _options, Selected());
            if (!IsPremade && !IsJungle) _module.SelectDynamicPlayers(_snapshot, Selected());
            _preview.Text = lines.Count == 0 && IsPremade && _snapshot != null ?
                T(LeagueEfficiencyUiTextKeys.PremadeNoGroups).Replace("{0}", _options.PremadeMatchThreshold.ToString()) : lines.Count == 0 && IsJungle && _snapshot != null ?
                T(LeagueEfficiencyUiTextKeys.JungleSelectHint) : string.Join(Environment.NewLine, lines);
            _loadJungle.Enabled = !_busy && IsJungle && _snapshot != null &&
                _jungle.CheckedItems.Cast<PlayerChoice>().Any(p => p.Player.JungleSamples == null);
            _load.Text = _snapshot != null && DateTime.UtcNow - _snapshot.CreatedUtc < TimeSpan.FromMinutes(2) &&
                LeagueDynamicMessageService.TargetNeedsHistory(_snapshot, _options.Target) ?
                T(LeagueEfficiencyUiTextKeys.DynamicCompleteMissing) : T(LeagueEfficiencyUiTextKeys.DynamicLoad);
            var incomplete = _snapshot != null && (IsPremade ?
                LeagueDynamicMessageService.TargetNeedsHistory(_snapshot, _options.Target) : !IsJungle &&
                LeagueDynamicMessageService.SelectedNeedsHistory(_snapshot, Selected()));
            _send.Enabled = !_busy && !incomplete && lines.Count > 0 && lines.Count <= 100 &&
                _snapshot != null && _snapshot.Phase != "InProgress" && _snapshot.Phase != "Lobby";
        }

        private async Task SendAsync()
        {
            if (_busy) return;
            if (_snapshot != null && (IsPremade &&
                LeagueDynamicMessageService.TargetNeedsHistory(_snapshot, _options.Target) ||
                !IsPremade && !IsJungle && LeagueDynamicMessageService.SelectedNeedsHistory(_snapshot, Selected())))
            {
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicPartial);
                return;
            }
            _busy = true;
            _send.Enabled = false; _load.Enabled = false; _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicSending);
            try
            {
                var sent = IsPremade ? await _module.SendPremadeMessagesAsync(_snapshot, _options, SelectedGroups(), _lifetime.Token) : IsJungle ?
                    await _module.SendJungleMessagesAsync(_snapshot, _options, SelectedJungle(), _lifetime.Token) :
                    await _module.SendDynamicMessagesAsync(_snapshot, _options, Selected(), _lifetime.Token);
                if (!IsDisposed) _status.Text = T(sent ? LeagueEfficiencyUiTextKeys.QuickMessageSent : LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicUnavailable); }
            finally { if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); } }
        }

        private void HandleContextChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    _snapshot = null; _binding = true; _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear(); _binding = false;
                    _preview.Clear(); _send.Enabled = false; _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded);
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void RefreshShortcuts()
        {
            var labels = new[] { T(LeagueEfficiencyUiTextKeys.DynamicFriendly), T(LeagueEfficiencyUiTextKeys.DynamicEnemy), T(LeagueEfficiencyUiTextKeys.DynamicAll) };
            for (var index = 0; index < _shortcutButtons.Length; index++)
                _shortcutButtons[index].Text = labels[index] + "：" +
                    (string.IsNullOrEmpty(Shortcut(index)) ? T(LeagueEfficiencyUiTextKeys.Disabled) : Shortcut(index));
        }

        private static void SelectContextPlayer(CheckedListBox list, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            list.SelectedIndex = list.IndexFromPoint(e.Location);
        }

        private void ImportTags()
        {
            if (_busy || IsDisposed) return;
            using (var dialog = new OpenFileDialog { Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                CheckFileExists = true, Multiselect = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var count = _module.ImportPlayerTags(dialog.FileName);
                    RefreshTagLabels();
                    _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagImported).Replace("{0}", count.ToString());
                }
                catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagExchangeFailed); }
            }
        }

        private void BrowseTags()
        {
            if (_busy || IsDisposed) return;
            using (var dialog = new LeaguePlayerTagBrowserDialog(_module, _ui))
                dialog.ShowDialog(this);
            if (_snapshot != null)
            {
                try { RefreshTagLabels(); }
                catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagUnavailable); }
            }
        }

        private void ExportTags()
        {
            if (_busy || IsDisposed) return;
            using (var dialog = new SaveFileDialog { Filter = "JSON 文件 (*.json)|*.json", DefaultExt = "json",
                FileName = "GGman-player-tags.json", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var count = _module.ExportPlayerTags(dialog.FileName);
                    _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagExported).Replace("{0}", count.ToString());
                }
                catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagExchangeFailed); }
            }
        }

        private void RefreshTagLabels()
        {
            if (_snapshot == null) return;
            var tags = _module.ReadPlayerTags(_snapshot);
            foreach (var item in _players.Items.Cast<PlayerChoice>().Concat(_jungle.Items.Cast<PlayerChoice>()))
            {
                string tag;
                item.Tag = item.Player != null && item.Player.Player != null &&
                    tags.TryGetValue(item.Player.Player.PuuId, out tag) ? tag : string.Empty;
            }
            _players.Refresh(); _jungle.Refresh();
        }

        private void ShowEncounterHistory(LeagueDynamicMessageSnapshot snapshot, PlayerChoice choice)
        {
            if (_busy || snapshot == null || !ReferenceEquals(snapshot, _snapshot) ||
                choice.Player == null || choice.Player.Player == null) return;
            using (var dialog = new LeagueEncounterHistoryBrowserDialog(_module, _ui,
                snapshot, choice.Player.Player.PuuId,
                () => !IsDisposed && ReferenceEquals(snapshot, _snapshot)))
                dialog.ShowDialog(this);
            if (!IsDisposed && ReferenceEquals(snapshot, _snapshot)) RefreshEncounterLabels();
        }

        private void RefreshEncounterLabels()
        {
            if (_snapshot == null) return;
            IReadOnlyDictionary<string, int> counts;
            try { counts = _module.ReadPlayerEncounterCounts(_snapshot); }
            catch (System.IO.InvalidDataException) { counts = new Dictionary<string, int>(StringComparer.Ordinal); }
            catch (System.IO.IOException) { counts = new Dictionary<string, int>(StringComparer.Ordinal); }
            catch (System.UnauthorizedAccessException) { counts = new Dictionary<string, int>(StringComparer.Ordinal); }
            foreach (var choice in _players.Items.Cast<PlayerChoice>().Concat(_jungle.Items.Cast<PlayerChoice>()))
            {
                int count;
                choice.EncounterCount = choice.Player?.Player != null &&
                    counts.TryGetValue(choice.Player.Player.PuuId, out count) ? count : 0;
            }
            _players.Refresh(); _jungle.Refresh();
        }

        private Form CreateTagDialog(string currentTag)
        {
            var dialog = new Form { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagTitle), Width = 370, Height = 157,
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
                BackColor = FacmDesignSystem.Canvas, ForeColor = FacmDesignSystem.Text };
            var input = new TextBox { Left = 12, Top = 16, Width = 330,
                MaxLength = LeaguePlayerTagStore.MaximumTagLength, Text = currentTag ?? string.Empty };
            var save = new Button { Text = T(LeagueEfficiencyUiTextKeys.AdvancedSave),
                DialogResult = DialogResult.OK, Left = 182, Top = 58, Width = 75 };
            var cancel = new Button { Text = T(LeagueEfficiencyUiTextKeys.DynamicTagCancel),
                DialogResult = DialogResult.Cancel, Left = 267, Top = 58, Width = 75 };
            dialog.Controls.Add(input); dialog.Controls.Add(save); dialog.Controls.Add(cancel);
            dialog.AcceptButton = save; dialog.CancelButton = cancel;
            return dialog;
        }

        private async Task SetPlayerTagAsync(LeagueDynamicMessageSnapshot snapshot, PlayerChoice choice, string tag)
        {
            if (_busy || snapshot == null || !ReferenceEquals(_snapshot, snapshot) ||
                choice == null || choice.Player == null || choice.Player.Player == null) return;
            var puuid = choice.Player.Player.PuuId;
            _busy = true;
            _load.Enabled = false; _loadJungle.Enabled = false; _send.Enabled = false;
            try
            {
                var saved = await _module.UpdatePlayerTagAsync(snapshot, puuid, tag, _lifetime.Token);
                if (IsDisposed || !ReferenceEquals(_snapshot, snapshot)) return;
                if (!saved) { _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicRefreshNeeded); return; }
                foreach (var item in _players.Items.Cast<PlayerChoice>().Concat(_jungle.Items.Cast<PlayerChoice>())
                    .Where(p => p.Player != null && p.Player.Player != null && p.Player.Player.PuuId == puuid))
                    item.Tag = (tag ?? string.Empty).Trim();
                _players.Refresh(); _jungle.Refresh();
                _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagSaved);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.DynamicTagUnavailable); }
            finally { if (!IsDisposed) { _busy = false; _load.Enabled = true; RefreshPreview(); } }
        }

        private void AddTagChoice(ToolStripMenuItem parent, string key, Func<bool> read, Action<bool> write)
        {
            var item = new ToolStripMenuItem(T(key).Replace("{0}", "").Trim())
            {
                CheckOnClick = true,
                Checked = read()
            };
            item.CheckedChanged += delegate
            {
                write(item.Checked);
                if (_snapshot != null)
                {
                    foreach (var choice in _players.Items.Cast<PlayerChoice>()
                        .Concat(_jungle.Items.Cast<PlayerChoice>()))
                        choice.RefreshSourceTags();
                    RebuildPremadeGroups(_snapshot, true);
                    _players.Refresh();
                    _jungle.Refresh();
                }
                RefreshPreview();
            };
            parent.DropDownItems.Add(item);
        }

        private string Shortcut(int index)
        {
            if (IsJungle) return index == 0 ? _options.JungleFriendlyShortcut : index == 1 ? _options.JungleEnemyShortcut : _options.JungleAllShortcut;
            if (IsPremade) return index == 0 ? _options.PremadeFriendlyShortcut : index == 1 ? _options.PremadeEnemyShortcut : _options.PremadeAllShortcut;
            return index == 0 ? _options.FriendlyShortcut : index == 1 ? _options.EnemyShortcut : _options.AllShortcut;
        }

        private void SetShortcut(int index, string value)
        {
            if (IsJungle)
            {
                if (index == 0) _options.JungleFriendlyShortcut = value;
                else if (index == 1) _options.JungleEnemyShortcut = value;
                else _options.JungleAllShortcut = value;
            }
            else if (IsPremade)
            {
                if (index == 0) _options.PremadeFriendlyShortcut = value;
                else if (index == 1) _options.PremadeEnemyShortcut = value;
                else _options.PremadeAllShortcut = value;
            }
            else
            {
                if (index == 0) _options.FriendlyShortcut = value;
                else if (index == 1) _options.EnemyShortcut = value;
                else _options.AllShortcut = value;
            }
        }
        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
        protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { _module.DynamicMessageContextChanged -= HandleContextChanged; _lifetime.Cancel(); _lifetime.Dispose(); _tagMenu.Dispose(); _exchangeMenu.Dispose(); _snapshot = null; } base.Dispose(disposing); }

        private sealed class PlayerChoice
        {
            internal readonly LeagueDynamicMessagePlayer Player;
            private readonly LeagueDynamicMessageOptions _options;
            internal string Tag;
            internal string PremadeGroup;
            internal bool IsSelf;
            internal int EncounterCount;
            private IReadOnlyList<string> _sourceTags;
            internal PlayerChoice(LeagueDynamicMessagePlayer player, LeagueDynamicMessageOptions options)
            {
                Player = player;
                _options = options;
                RefreshSourceTags();
            }
            internal void RefreshSourceTags()
            {
                var row = Player.Player;
                var jungler = row != null &&
                    (string.Equals(row.Position, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(row.Role, "JUNGLE", StringComparison.OrdinalIgnoreCase) ||
                     row.Spell1Id == 11 || row.Spell2Id == 11);
                _sourceTags = LeagueOngoingPlayerTagProjection.Project(Player.History, _options,
                    Player.EarlyGankAverage, jungler, Player.IsPrivate);
            }
            public override string ToString()
            {
                return LeagueEfficiencyText.Get(null, Player.Friendly ? LeagueEfficiencyUiTextKeys.DynamicFriendly : LeagueEfficiencyUiTextKeys.DynamicEnemy) +
                    " · " + (Player.Player.AccountName ?? Player.ChampionName ?? LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.DynamicPlayer)) +
                    (IsSelf && _options.ShowSelfTag ? " · " + LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.OngoingSelf) : string.Empty) +
                    (!_options.ShowTaggedTag || IsSelf || string.IsNullOrEmpty(Tag) ? string.Empty : " · [" + Tag + "]") +
                    (string.IsNullOrEmpty(PremadeGroup) ? string.Empty : " · " + PremadeGroup) +
                    (!IsSelf && _options.ShowMetTag && EncounterCount > 0 ? " · " +
                        LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.OngoingMet)
                            .Replace("{0}", EncounterCount.ToString()) : string.Empty) +
                    (_sourceTags == null || _sourceTags.Count == 0 ? string.Empty : " · " + string.Join(" · ", _sourceTags));
            }
        }

        private sealed class PremadeChoice
        {
            internal readonly string Key;
            private readonly string _label;
            internal PremadeChoice(string[] members, IDictionary<string, LeagueDynamicMessagePlayer> players,
                string countFormat, string tag, string summary)
            {
                Key = LeaguePremadeMessageFormatter.GroupKey(members);
                var names = members.Select(id => LeagueDynamicMessageFormatter.DisplayName(players[id], "preferName",
                    new Dictionary<int, int>()));
                _label = countFormat.Replace("{0}", members.Length.ToString()) +
                    (string.IsNullOrEmpty(tag) ? string.Empty : " · " + tag) +
                    (string.IsNullOrEmpty(summary) ? string.Empty : " · " + summary) + " · " + string.Join(" / ", names);
            }
            public override string ToString() { return _label; }
        }
    }
}
