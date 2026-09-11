using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueEfficiencyForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly TextBox _exitGame;
        private readonly TextBox _closeLobby;
        private readonly CheckBox _autoHonor;
        private readonly CheckBox _autoReturn;
        private readonly CheckBox _autoSearch;
        private readonly CheckBox _autoAccept;
        private readonly NumericUpDown _minPartySize;
        private readonly NumericUpDown _matchmakingDelaySeconds;
        private readonly NumericUpDown _acceptDelaySeconds;
        private readonly Label _help;
        private readonly Label _status;
        private readonly Label _honorStatus;
        private bool _loading = true;

        public LeagueEfficiencyForm(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));

            Text = T(LeagueEfficiencyUiTextKeys.WindowTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 760);
            MinimumSize = new Size(680, 650);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            FacmWindowChrome.SetSubtitle(this, T(LeagueEfficiencyUiTextKeys.Hint));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 16,
                BackColor = FacmDesignSystem.Canvas,
                AutoScroll = true
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Title),
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _help = new Label
            {
                Text = T(LeagueEfficiencyUiTextKeys.Hint),
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true
            };
            root.Controls.Add(_help, 0, 1);
            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.HotkeySection)), 0, 2);

            _exitGame = AddHotkeyRow(root, 3, T(LeagueEfficiencyUiTextKeys.ExitGame), T(LeagueEfficiencyUiTextKeys.ExitGameHint), _module.ExitGameHotkey);
            _closeLobby = AddHotkeyRow(root, 4, T(LeagueEfficiencyUiTextKeys.CloseLobby), T(LeagueEfficiencyUiTextKeys.CloseLobbyHint), _module.CloseLobbyHotkey);

            var hotkeyFooter = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = FacmDesignSystem.Canvas
            };
            hotkeyFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _status = new Label
            {
                Text = string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var save = CreateButton(T(LeagueEfficiencyUiTextKeys.Save), 140, FacmButtonTone.Primary);
            save.Click += delegate { SaveBindings(); };
            hotkeyFooter.Controls.Add(_status, 0, 0);
            hotkeyFooter.Controls.Add(save, 1, 0);
            root.Controls.Add(hotkeyFooter, 0, 5);

            _honorStatus = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Text = FormatHonorStatus(_module.LastHonorStatus)
            };
            root.Controls.Add(CreatePostGameHeader(), 0, 6);
            _autoHonor = AddAutomationRow(root, 7, T(LeagueEfficiencyUiTextKeys.AutoHonor), T(LeagueEfficiencyUiTextKeys.AutoHonorHint), _module.AutoHonorEnabled);
            _autoReturn = AddAutomationRow(root, 8, T(LeagueEfficiencyUiTextKeys.AutoReturn), T(LeagueEfficiencyUiTextKeys.AutoReturnHint), _module.AutoReturnLobbyEnabled);
            _autoHonor.CheckedChanged += PostGameSettingChanged;
            _autoReturn.CheckedChanged += PostGameSettingChanged;

            root.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.NextGameSection)), 0, 9);
            _autoSearch = AddAutomationRow(root, 10, T(LeagueEfficiencyUiTextKeys.AutoMatchmaking), T(LeagueEfficiencyUiTextKeys.AutoMatchmakingHint), _module.AutoMatchmakingEnabled);
            _autoAccept = AddAutomationRow(root, 11, T(LeagueEfficiencyUiTextKeys.AutoAccept), T(LeagueEfficiencyUiTextKeys.AutoAcceptHint), _module.AutoAcceptEnabled);
            _autoSearch.CheckedChanged += MatchmakingSettingChanged;
            _autoAccept.CheckedChanged += MatchmakingSettingChanged;
            _minPartySize = AddNumberRow(root, 12, T(LeagueEfficiencyUiTextKeys.MinPartySize), T(LeagueEfficiencyUiTextKeys.MinPartySizeHint), 1m, 5m, 1m, _module.AutoMatchmakingMinPartySize, "人");
            _matchmakingDelaySeconds = AddNumberRow(root, 13, T(LeagueEfficiencyUiTextKeys.MatchmakingDelay), T(LeagueEfficiencyUiTextKeys.MatchmakingDelayHint), 0m, 60m, 1m, _module.AutoMatchmakingStartDelayMs / 1000m, "秒");
            _acceptDelaySeconds = AddNumberRow(root, 14, T(LeagueEfficiencyUiTextKeys.AcceptDelay), T(LeagueEfficiencyUiTextKeys.AcceptDelayHint), 0m, 15m, 0.5m, _module.AutoAcceptDelayMs / 1000m, "秒");
            _minPartySize.ValueChanged += MatchmakingSettingChanged;
            _matchmakingDelaySeconds.ValueChanged += MatchmakingSettingChanged;
            _acceptDelaySeconds.ValueChanged += MatchmakingSettingChanged;

            _module.HonorStatusChanged += HandleHonorStatusChanged;
            FormClosed += HandleFormClosed;
            Controls.Add(root);
            _loading = false;
            UpdateMatchmakingControlStates();
        }

        private Control CreatePostGameHeader()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = FacmDesignSystem.Canvas,
                Margin = Padding.Empty
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            panel.Controls.Add(SectionLabel(T(LeagueEfficiencyUiTextKeys.PostGameSection)), 0, 0);
            panel.Controls.Add(_honorStatus, 1, 0);
            return panel;
        }

        private Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private TextBox AddHotkeyRow(TableLayoutPanel parent, int row, string title, string hint, string value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(titleLabel, 0, 0);

            var box = new TextBox
            {
                Text = value ?? string.Empty,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 3, 6, 3),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            panel.Controls.Add(box, 1, 0);

            var capture = CreateButton(T(LeagueEfficiencyUiTextKeys.Capture), 68, FacmButtonTone.Secondary);
            capture.Click += delegate
            {
                using (var dialog = new LeagueHotkeyCaptureDialog(_ui))
                {
                    dialog.TopMost = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        box.Text = dialog.Binding == null ? string.Empty : dialog.Binding.ToString();
                }
            };
            panel.Controls.Add(capture, 2, 0);

            var clear = CreateButton(T(LeagueEfficiencyUiTextKeys.Clear), 68, FacmButtonTone.Secondary);
            clear.Click += delegate { box.Text = string.Empty; };
            panel.Controls.Add(clear, 3, 0);

            WireHelp(panel, hint);
            WireHelp(titleLabel, hint);
            WireHelp(box, hint);
            WireHelp(capture, hint);
            WireHelp(clear, hint);
            parent.Controls.Add(panel, 0, row);
            return box;
        }

        private CheckBox AddAutomationRow(TableLayoutPanel parent, int row, string title, string hint, bool value)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            var check = new FacmToggleSwitch
            {
                Text = title,
                Checked = value,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };
            panel.Controls.Add(check, 0, 0);
            WireHelp(panel, hint);
            WireHelp(check, hint);
            parent.Controls.Add(panel, 0, row);
            return check;
        }

        private NumericUpDown AddNumberRow(
            TableLayoutPanel parent,
            int row,
            string title,
            string hint,
            decimal minimum,
            decimal maximum,
            decimal increment,
            decimal value,
            string suffix)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = FacmDesignSystem.Surface,
                Padding = new Padding(12, 7, 12, 7)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var input = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment,
                DecimalPlaces = increment < 1m ? 1 : 0,
                Value = Math.Max(minimum, Math.Min(maximum, value)),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 4, 2),
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right
            };
            var unit = new Label
            {
                Text = suffix ?? string.Empty,
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(input, 1, 0);
            panel.Controls.Add(unit, 2, 0);
            WireHelp(panel, hint);
            WireHelp(titleLabel, hint);
            WireHelp(input, hint);
            WireHelp(unit, hint);
            parent.Controls.Add(panel, 0, row);
            return input;
        }

        private void WireHelp(Control control, string text)
        {
            if (control == null) return;
            control.MouseEnter += delegate
            {
                if (_help != null && !_help.IsDisposed) _help.Text = text;
            };
            control.MouseLeave += delegate
            {
                if (_help != null && !_help.IsDisposed) _help.Text = T(LeagueEfficiencyUiTextKeys.Hint);
            };
        }

        private FacmActionButton CreateButton(string text, int width, FacmButtonTone tone)
        {
            return new FacmActionButton
            {
                Text = text,
                Width = width,
                Height = 30,
                Dock = DockStyle.Fill,
                Tone = tone,
                Font = new Font(Font.FontFamily, 8.8F, FontStyle.Bold),
                Margin = new Padding(4, 2, 4, 2)
            };
        }

        private void SaveBindings()
        {
            string error;
            if (_module.TryUpdateBindings(_exitGame.Text, _closeLobby.Text, out error))
            {
                _exitGame.Text = _module.ExitGameHotkey;
                _closeLobby.Text = _module.CloseLobbyHotkey;
                _status.ForeColor = FacmDesignSystem.Success;
                _status.Text = T(LeagueEfficiencyUiTextKeys.Saved);
            }
            else
            {
                _status.ForeColor = FacmDesignSystem.Warning;
                _status.Text = string.Format(T(LeagueEfficiencyUiTextKeys.SaveFailed), error ?? string.Empty);
            }
        }

        private void PostGameSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _module.UpdatePostGameSettings(_autoHonor.Checked, _autoReturn.Checked);
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.PostGameSaved);
        }

        private void MatchmakingSettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            UpdateMatchmakingControlStates();
            _module.UpdateMatchmakingSettings(
                _autoSearch.Checked,
                _autoAccept.Checked,
                Decimal.ToInt32(_minPartySize.Value),
                Decimal.ToInt32(_matchmakingDelaySeconds.Value * 1000m),
                Decimal.ToInt32(_acceptDelaySeconds.Value * 1000m));
            _status.ForeColor = FacmDesignSystem.Success;
            _status.Text = T(LeagueEfficiencyUiTextKeys.NextGameSaved);
        }

        private void UpdateMatchmakingControlStates()
        {
            if (_minPartySize != null) _minPartySize.Enabled = _autoSearch != null && _autoSearch.Checked;
            if (_matchmakingDelaySeconds != null) _matchmakingDelaySeconds.Enabled = _autoSearch != null && _autoSearch.Checked;
            if (_acceptDelaySeconds != null) _acceptDelaySeconds.Enabled = _autoAccept != null && _autoAccept.Checked;
        }

        private void HandleHonorStatusChanged(LeagueHonorAttemptStatus status)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(delegate { ApplyHonorStatus(status); }));
            }
            catch (InvalidOperationException) { }
        }

        private void ApplyHonorStatus(LeagueHonorAttemptStatus status)
        {
            if (_honorStatus == null || _honorStatus.IsDisposed) return;
            _honorStatus.Text = FormatHonorStatus(status);
            var state = status == null ? string.Empty : status.State ?? string.Empty;
            if (string.Equals(state, "success", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Success;
            else if (string.Equals(state, "failed", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Warning;
            else if (string.Equals(state, "unknown", StringComparison.Ordinal))
                _honorStatus.ForeColor = FacmDesignSystem.Warning;
            else
                _honorStatus.ForeColor = FacmDesignSystem.TextMuted;
        }

        private string FormatHonorStatus(LeagueHonorAttemptStatus status)
        {
            if (status == null) return T(LeagueEfficiencyUiTextKeys.HonorLastNone);
            var time = status.CompletedAtUtc == default(DateTime)
                ? DateTime.Now.ToString("HH:mm")
                : status.CompletedAtUtc.ToLocalTime().ToString("HH:mm");
            if (string.Equals(status.State, "success", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastSuccess), time);
            if (string.Equals(status.State, "skipped", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastSkipped), time);
            if (string.Equals(status.State, "unknown", StringComparison.Ordinal))
                return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastUnknown), time);
            return string.Format(T(LeagueEfficiencyUiTextKeys.HonorLastFailed), time);
        }

        private void HandleFormClosed(object sender, FormClosedEventArgs e)
        {
            _module.HonorStatusChanged -= HandleHonorStatusChanged;
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }

    internal sealed class LeagueHotkeyCaptureDialog : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly Label _prompt;

        public LeagueHotkeyCaptureDialog(UiTextCatalog ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            FacmWindowChrome.SetSubtitle(this, LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CapturePrompt));
            _prompt = new Label
            {
                Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CapturePrompt),
                Dock = DockStyle.Fill,
                Padding = new Padding(20),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(_prompt);
            KeyDown += HandleKeyDown;
        }

        public LeagueHotkeyBinding Binding { get; private set; }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Escape && !e.Control && !e.Alt && !e.Shift)
            {
                Binding = LeagueHotkeyBinding.Disabled;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var candidate = LeagueHotkeyBinding.FromKeyEvent(e.KeyData);
            string parsedError;
            LeagueHotkeyBinding parsed;
            if (!LeagueHotkeyBinding.TryParse(candidate.ToString(), out parsed, out parsedError) || !parsed.Enabled)
            {
                _prompt.ForeColor = FacmDesignSystem.Warning;
                _prompt.Text = LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.CaptureUnsafe);
                return;
            }
            Binding = parsed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
