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
    internal sealed class LeagueAutoSelectProfilesForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _text;
        private readonly List<LeagueAutoSelectProfile> _profiles;
        private readonly Dictionary<int, string> _championNames = new Dictionary<int, string>();
        private readonly CancellationTokenSource _catalogLifetime = new CancellationTokenSource();
        private readonly ComboBox _mode;
        private readonly ComboBox _role;
        private readonly ComboBox _action;
        private readonly ComboBox _catalog;
        private readonly ComboBox _strategy;
        private readonly ListBox _priority;
        private readonly NumericUpDown _delay;
        private readonly Label _status;
        private bool _binding;

        public LeagueAutoSelectProfilesForm(LeagueEfficiencyModule module, UiTextCatalog text)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _profiles = LeagueAutoSelectProfiles.Parse(module.AutoSelectProfilesJson).ToList();
            Text = T(LeagueEfficiencyUiTextKeys.AdvancedTitle);
            ClientSize = new Size(700, 625);
            MinimumSize = new Size(680, 590);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(22), AutoScroll = true,
                ColumnCount = 2, RowCount = 8, BackColor = FacmDesignSystem.Canvas
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var height in new[] { 54, 46, 46, 46, 266, 48, 48, 55 })
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

            var header = new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.AdvancedHint),
                Dock = DockStyle.Fill, ForeColor = FacmDesignSystem.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            _mode = AddChoice(root, 1, LeagueEfficiencyUiTextKeys.AdvancedMode,
                new[] { T(LeagueEfficiencyUiTextKeys.ProfileModeDefault),
                    T(LeagueEfficiencyUiTextKeys.ProfileModeRift), T(LeagueEfficiencyUiTextKeys.ProfileModeAram),
                    T(LeagueEfficiencyUiTextKeys.ProfileModeMayhem) });
            _role = AddChoice(root, 2, LeagueEfficiencyUiTextKeys.AdvancedRole,
                new[] { T(LeagueEfficiencyUiTextKeys.ProfileRoleDefault),
                    T(LeagueEfficiencyUiTextKeys.ProfileRoleTop), T(LeagueEfficiencyUiTextKeys.ProfileRoleJungle),
                    T(LeagueEfficiencyUiTextKeys.ProfileRoleMiddle), T(LeagueEfficiencyUiTextKeys.ProfileRoleBottom),
                    T(LeagueEfficiencyUiTextKeys.ProfileRoleSupport) });
            _action = AddChoice(root, 3, LeagueEfficiencyUiTextKeys.AdvancedAction,
                new[] { T(LeagueEfficiencyUiTextKeys.AutoPick), T(LeagueEfficiencyUiTextKeys.AutoBan) });

            root.Controls.Add(Caption(LeagueEfficiencyUiTextKeys.AdvancedPriority), 0, 4);
            var listRegion = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            listRegion.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            listRegion.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            _priority = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text
            };
            listRegion.Controls.Add(_priority, 0, 0);
            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            for (var i = 0; i < 6; i++) buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6F));
            _catalog = new ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Name",
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                FlatStyle = FlatStyle.Flat
            };
            buttons.Controls.Add(_catalog, 0, 0);
            AddButton(buttons, 1, LeagueEfficiencyUiTextKeys.AdvancedAdd, delegate { AddChampion(); });
            AddButton(buttons, 2, LeagueEfficiencyUiTextKeys.AdvancedRemove, delegate { RemoveChampion(); });
            AddButton(buttons, 3, LeagueEfficiencyUiTextKeys.AdvancedUp, delegate { MoveChampion(-1); });
            AddButton(buttons, 4, LeagueEfficiencyUiTextKeys.AdvancedDown, delegate { MoveChampion(1); });
            listRegion.Controls.Add(buttons, 1, 0);
            root.Controls.Add(listRegion, 1, 4);

            _strategy = AddChoice(root, 5, LeagueEfficiencyUiTextKeys.AdvancedStrategy,
                new[] { T(LeagueEfficiencyUiTextKeys.StrategyImmediate),
                    T(LeagueEfficiencyUiTextKeys.StrategyShowLock), T(LeagueEfficiencyUiTextKeys.StrategyShowOnly) });
            _delay = new NumericUpDown
            {
                Minimum = 0, Maximum = 15, DecimalPlaces = 1, Increment = 0.5m,
                Dock = DockStyle.Left, Width = 110, BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            root.Controls.Add(Caption(LeagueEfficiencyUiTextKeys.AdvancedDelay), 0, 6);
            root.Controls.Add(_delay, 1, 6);

            _status = new Label { Dock = DockStyle.Fill, ForeColor = FacmDesignSystem.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            var save = new FacmActionButton { Text = T(LeagueEfficiencyUiTextKeys.AdvancedSave),
                Tone = FacmButtonTone.Primary, Width = 112, Height = 34 };
            var cancel = new FacmActionButton { Text = T(LeagueEfficiencyUiTextKeys.AdvancedCancel),
                Tone = FacmButtonTone.Secondary, Width = 100, Height = 34 };
            save.Click += delegate { SaveProfiles(); };
            cancel.Click += delegate { Close(); };
            footer.Controls.Add(_status, 0, 0);
            footer.Controls.Add(cancel, 1, 0);
            footer.Controls.Add(save, 2, 0);
            root.Controls.Add(footer, 0, 7);
            root.SetColumnSpan(footer, 2);
            Controls.Add(root);

            _mode.SelectedIndexChanged += ContextChanged;
            _role.SelectedIndexChanged += ContextChanged;
            _action.SelectedIndexChanged += ContextChanged;
            _strategy.SelectedIndexChanged += StrategyChanged;
            _delay.ValueChanged += StrategyChanged;
            _mode.SelectedIndex = 0;
            _role.SelectedIndex = 0;
            _action.SelectedIndex = 0;
            RefreshProfile();
            FormClosed += delegate { _catalogLifetime.Cancel(); _catalogLifetime.Dispose(); };
            _ = LoadCatalogAsync();
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_text, key); }

        private Label Caption(string key)
        {
            return new Label { Text = T(key), Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.Text, TextAlign = ContentAlignment.MiddleLeft };
        }

        private ComboBox AddChoice(TableLayoutPanel root, int row, string key, string[] names)
        {
            root.Controls.Add(Caption(key), 0, row);
            var combo = new ComboBox { Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = FacmDesignSystem.CanvasRaised, ForeColor = FacmDesignSystem.Text,
                FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(names);
            root.Controls.Add(combo, 1, row);
            return combo;
        }

        private void AddButton(TableLayoutPanel root, int row, string key, EventHandler click)
        {
            var button = new FacmActionButton { Text = T(key), Dock = DockStyle.Fill,
                Tone = FacmButtonTone.Secondary, Margin = new Padding(3) };
            button.Click += click;
            root.Controls.Add(button, 0, row);
        }

        private LeagueAutoSelectProfile CurrentProfile(bool create)
        {
            var mode = LeagueAutoSelectProfiles.Modes[Math.Max(0, _mode.SelectedIndex)];
            var role = LeagueAutoSelectProfiles.Roles[Math.Max(0, _role.SelectedIndex)];
            var found = _profiles.FirstOrDefault(p => p.Mode == mode && p.Role == role);
            if (found == null && create)
            {
                found = new LeagueAutoSelectProfile { Mode = mode, Role = role };
                _profiles.Add(found);
            }
            return found;
        }

        private List<int> CurrentList(bool create)
        {
            var profile = CurrentProfile(create);
            return profile == null ? null : _action.SelectedIndex == 1 ? profile.Bans : profile.Picks;
        }

        private void ContextChanged(object sender, EventArgs e)
        {
            if (_binding) return;
            _role.Enabled = _mode.SelectedIndex == 1;
            if (!_role.Enabled && _role.SelectedIndex != 0)
            {
                _role.SelectedIndex = 0;
                return;
            }
            RefreshProfile();
        }

        private void RefreshProfile()
        {
            if (_mode.SelectedIndex < 0 || _role.SelectedIndex < 0 || _action.SelectedIndex < 0) return;
            _binding = true;
            try
            {
                _priority.Items.Clear();
                var list = CurrentList(false);
                if (list != null) foreach (var id in list) _priority.Items.Add(ChampionName(id));
                var profile = CurrentProfile(false) ?? new LeagueAutoSelectProfile();
                var strategy = _action.SelectedIndex == 1 ? profile.BanStrategy : profile.PickStrategy;
                _strategy.SelectedIndex = Math.Max(0, Array.IndexOf(LeagueAutoSelectProfiles.Strategies, strategy));
                _delay.Value = (_action.SelectedIndex == 1 ? profile.BanDelayMs : profile.PickDelayMs) / 1000m;
            }
            finally { _binding = false; }
        }

        private string ChampionName(int id)
        {
            string name;
            return _championNames.TryGetValue(id, out name) ? name + " (#" + id + ")" : "#" + id;
        }

        private void AddChampion()
        {
            var option = _catalog.SelectedItem as LeagueProfileChampionOption;
            if (option == null || option.Id <= 0) return;
            var list = CurrentList(true);
            if (list.Count >= 20 || list.Contains(option.Id)) return;
            list.Add(option.Id);
            RefreshProfile();
            _priority.SelectedIndex = list.Count - 1;
        }

        private void RemoveChampion()
        {
            var list = CurrentList(false);
            if (list == null || _priority.SelectedIndex < 0) return;
            list.RemoveAt(_priority.SelectedIndex);
            RefreshProfile();
        }

        private void MoveChampion(int offset)
        {
            var list = CurrentList(false);
            if (list == null) return;
            var index = _priority.SelectedIndex;
            var target = index + offset;
            if (index < 0 || target < 0 || target >= list.Count) return;
            var id = list[index];
            list[index] = list[target];
            list[target] = id;
            RefreshProfile();
            _priority.SelectedIndex = target;
        }

        private void StrategyChanged(object sender, EventArgs e)
        {
            if (_binding || _strategy.SelectedIndex < 0) return;
            var profile = CurrentProfile(true);
            if (_action.SelectedIndex == 1)
            {
                profile.BanStrategy = LeagueAutoSelectProfiles.Strategies[_strategy.SelectedIndex];
                profile.BanDelayMs = (int)(_delay.Value * 1000);
            }
            else
            {
                profile.PickStrategy = LeagueAutoSelectProfiles.Strategies[_strategy.SelectedIndex];
                profile.PickDelayMs = (int)(_delay.Value * 1000);
            }
        }

        private async Task LoadCatalogAsync()
        {
            try
            {
                var champions = await _module.LoadChampionsAsync(_catalogLifetime.Token);
                if (_catalogLifetime.IsCancellationRequested || IsDisposed || Disposing) return;
                _binding = true;
                try
                {
                    foreach (var champion in champions ?? Array.Empty<LeagueProfileChampionOption>())
                    {
                        if (champion == null || champion.Id <= 0 || string.IsNullOrWhiteSpace(champion.Name)) continue;
                        _championNames[champion.Id] = champion.Name;
                        _catalog.Items.Add(champion);
                    }
                    if (_catalog.Items.Count > 0) _catalog.SelectedIndex = 0;
                }
                finally { _binding = false; }
                RefreshProfile();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!IsDisposed && !Disposing)
                {
                    _status.Text = T(LeagueEfficiencyUiTextKeys.ChampionCatalogUnavailable);
                    AppLog.Info("Auto-select catalog unavailable: " + exception.GetType().Name);
                }
            }
        }

        private void SaveProfiles()
        {
            try
            {
                _module.UpdateAutoSelectProfiles(LeagueAutoSelectProfiles.Serialize(_profiles));
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                _status.ForeColor = FacmDesignSystem.Warning;
                _status.Text = T(LeagueEfficiencyUiTextKeys.SaveFailed).Replace("{0}", exception.GetType().Name);
            }
        }
    }
}
