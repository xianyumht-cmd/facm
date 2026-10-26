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
            var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            _target = Choice(new[] { T(LeagueEfficiencyUiTextKeys.DynamicFriendly), T(LeagueEfficiencyUiTextKeys.DynamicEnemy), T(LeagueEfficiencyUiTextKeys.DynamicAll) });
            _target.SelectedIndex = Array.IndexOf(new[] { "friendly", "enemy", "all" }, _options.Target);
            _names = Choice(new[] { T(LeagueEfficiencyUiTextKeys.DynamicNames), T(LeagueEfficiencyUiTextKeys.DynamicChampionNames), T(LeagueEfficiencyUiTextKeys.DynamicCombinedNames) });
            _names.SelectedIndex = Array.IndexOf(new[] { "preferName", "preferChampionName", "championNameWithName" }, _options.NameStrategy);
            choices.Controls.Add(_target); choices.Controls.Add(_names);
            var champion = new CheckBox { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicCurrentChampion), Checked = _options.ShowCurrentChampion };
            champion.CheckedChanged += delegate
            {
                if (_binding) return;
                if (IsJungle) _options.JungleShowCurrentChampion = champion.Checked;
                else _options.ShowCurrentChampion = champion.Checked;
                RefreshPreview();
            };
            choices.Controls.Add(champion); root.Controls.Add(choices, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            _load = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicLoad), Tone = FacmButtonTone.Secondary };
            _loadJungle = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.JungleLoad),
                Enabled = false, Visible = false, Tone = FacmButtonTone.Secondary };
            _send = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.DynamicSend), Enabled = false, Tone = FacmButtonTone.Primary };
            var save = new FacmActionButton { AutoSize = true, Text = T(LeagueEfficiencyUiTextKeys.AdvancedSave), Tone = FacmButtonTone.Secondary };
            _load.Click += async delegate { await LoadAsync(); };
            _loadJungle.Click += async delegate { await LoadJungleAsync(); };
            _send.Click += async delegate { await SendAsync(); };
            save.Click += delegate
            {
                string error;
                _status.Text = _module.TrySaveDynamicMessageOptions(_options, out error) ? T(LeagueEfficiencyUiTextKeys.DynamicSaved) : error;
            };
            actions.Controls.Add(_load); actions.Controls.Add(_loadJungle); actions.Controls.Add(save); actions.Controls.Add(_send);
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
                ForeColor = FacmDesignSystem.Text, AccessibleName = T(LeagueEfficiencyUiTextKeys.DynamicSelectPlayers) };
            _groups = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text, AccessibleName = T(LeagueEfficiencyUiTextKeys.PremadeSelectGroups) };
            _jungle = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text, AccessibleName = T(LeagueEfficiencyUiTextKeys.JungleTab) };
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
                            try { RebuildPremadeGroups(current, true); }
                            finally { _binding = false; }
                        }
                        _status.Text = T(completed ? LeagueEfficiencyUiTextKeys.DynamicReady :
                            LeagueEfficiencyUiTextKeys.DynamicPartial);
                    }
                    return;
                }
                var snapshot = await _module.LoadDynamicMessagesAsync(_lifetime.Token);
                if (IsDisposed) return;
                _snapshot = snapshot; _binding = true; _players.Items.Clear(); _groups.Items.Clear(); _jungle.Items.Clear();
                var selected = _module.DynamicSelection(snapshot);
                if (snapshot != null)
                {
                    foreach (var player in snapshot.Players)
                    {
                        _players.Items.Add(new PlayerChoice(player), selected == null || selected.Contains(player.Player.PuuId));
                        _jungle.Items.Add(new PlayerChoice(player), false);
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

        private void RebuildPremadeGroups(LeagueDynamicMessageSnapshot snapshot, bool preserveSelection)
        {
            var existing = preserveSelection ? new HashSet<string>(_groups.Items.Cast<PremadeChoice>()
                .Select(g => g.Key), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var checkedGroups = preserveSelection ? SelectedGroups() : new HashSet<string>(StringComparer.Ordinal);
            _groups.Items.Clear();
            var byId = snapshot.Players.Where(p => p != null && p.Player != null &&
                LeagueDynamicMessageService.SafePuuid(p.Player.PuuId))
                .GroupBy(p => p.Player.PuuId, StringComparer.Ordinal)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var group in LeaguePremadeInference.Infer(snapshot).OrderByDescending(g => byId[g[0]].Friendly))
            {
                var choice = new PremadeChoice(group, byId, T(LeagueEfficiencyUiTextKeys.PremadeGroupCount));
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

        private void RefreshPreview()
        {
            if (_binding || IsDisposed || _preview == null) return;
            var lines = IsPremade ? LeaguePremadeMessageFormatter.Build(_snapshot, _options, SelectedGroups()) : IsJungle ?
                LeagueJungleMessageFormatter.Build(_snapshot, _options, SelectedJungle()) :
                LeagueDynamicMessageFormatter.Build(_snapshot, _options, Selected());
            if (!IsPremade && !IsJungle) _module.SelectDynamicPlayers(_snapshot, Selected());
            _preview.Text = lines.Count == 0 && IsPremade && _snapshot != null ?
                T(LeagueEfficiencyUiTextKeys.PremadeNoGroups) : lines.Count == 0 && IsJungle && _snapshot != null ?
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
                _snapshot != null && _snapshot.Phase != "InProgress";
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
        protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { _module.DynamicMessageContextChanged -= HandleContextChanged; _lifetime.Cancel(); _lifetime.Dispose(); _snapshot = null; } base.Dispose(disposing); }

        private sealed class PlayerChoice
        {
            internal readonly LeagueDynamicMessagePlayer Player;
            internal PlayerChoice(LeagueDynamicMessagePlayer player) { Player = player; }
            public override string ToString()
            {
                return LeagueEfficiencyText.Get(null, Player.Friendly ? LeagueEfficiencyUiTextKeys.DynamicFriendly : LeagueEfficiencyUiTextKeys.DynamicEnemy) +
                    " · " + (Player.Player.AccountName ?? Player.ChampionName ?? LeagueEfficiencyText.Get(null, LeagueEfficiencyUiTextKeys.DynamicPlayer));
            }
        }

        private sealed class PremadeChoice
        {
            internal readonly string Key;
            private readonly string _label;
            internal PremadeChoice(string[] members, IDictionary<string, LeagueDynamicMessagePlayer> players, string countFormat)
            {
                Key = LeaguePremadeMessageFormatter.GroupKey(members);
                var names = members.Select(id => LeagueDynamicMessageFormatter.DisplayName(players[id], "preferName",
                    new Dictionary<int, int>()));
                _label = countFormat.Replace("{0}", members.Length.ToString()) + " · " + string.Join(" / ", names);
            }
            public override string ToString() { return _label; }
        }
    }
}
