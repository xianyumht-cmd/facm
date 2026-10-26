using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class LeagueQuickMessageForm : Form
    {
        private readonly LeagueEfficiencyModule _module;
        private readonly UiTextCatalog _ui;
        private readonly TextBox[] _slots = new TextBox[LeagueQuickMessagePresets.SlotCount];
        private readonly Button[] _send = new Button[LeagueQuickMessagePresets.SlotCount];
        private readonly Button[] _shortcutButtons = new Button[LeagueQuickMessageHotkeys.Count];
        private readonly string[] _shortcuts;
        private readonly NumericUpDown _interval;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Label _status;
        private bool _sending;

        internal LeagueQuickMessageForm(LeagueEfficiencyModule module, UiTextCatalog ui)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            Text = T(LeagueEfficiencyUiTextKeys.QuickMessageTitle);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 590);
            MinimumSize = new Size(600, 530);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            FacmWindowChrome.SetSubtitle(this, T(LeagueEfficiencyUiTextKeys.QuickMessageHint));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(18),
                AutoScroll = true
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            for (var index = 0; index < _slots.Length; index++)
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = FacmDesignSystem.TextMuted,
                Text = T(LeagueEfficiencyUiTextKeys.QuickMessageHint),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var presets = LeagueQuickMessagePresets.Parse(_module.QuickMessagePresetsJson);
            _shortcuts = LeagueQuickMessageHotkeys.Parse(_module.QuickMessageHotkeysJson);
            for (var index = 0; index < _slots.Length; index++)
            {
                var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
                row.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = (index + 1).ToString() + ".",
                    ForeColor = FacmDesignSystem.TextMuted,
                    TextAlign = ContentAlignment.MiddleCenter
                }, 0, 0);
                var box = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    MaxLength = 600,
                    Text = presets[index] ?? string.Empty,
                    BackColor = FacmDesignSystem.CanvasRaised,
                    ForeColor = FacmDesignSystem.Text,
                    AccessibleName = T(LeagueEfficiencyUiTextKeys.QuickMessageOpen) + " " + (index + 1)
                };
                _slots[index] = box;
                row.Controls.Add(box, 1, 0);
                var hotkey = new Button { Dock = DockStyle.Fill,
                    AccessibleName = T(LeagueEfficiencyUiTextKeys.QuickMessageShortcut) + " " + (index + 1) };
                _shortcutButtons[index] = hotkey;
                var shortcutIndex = index;
                hotkey.Click += delegate { CaptureShortcut(shortcutIndex); };
                row.Controls.Add(hotkey, 2, 0);
                var send = new Button
                {
                    Dock = DockStyle.Fill,
                    Text = T(LeagueEfficiencyUiTextKeys.QuickMessageSend),
                    AccessibleName = T(LeagueEfficiencyUiTextKeys.QuickMessageSend) + " " + (index + 1)
                };
                var slot = index;
                send.Click += async (sender, args) => await SendAsync(slot);
                _send[index] = send;
                row.Controls.Add(send, 3, 0);
                root.Controls.Add(row, 0, index + 1);
            }

            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            options.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = T(LeagueEfficiencyUiTextKeys.QuickMessageCancel),
                TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var cancelShortcut = new Button { Dock = DockStyle.Fill,
                AccessibleName = T(LeagueEfficiencyUiTextKeys.QuickMessageCancel) };
            cancelShortcut.Click += delegate { CaptureShortcut(LeagueQuickMessagePresets.SlotCount); };
            _shortcutButtons[LeagueQuickMessagePresets.SlotCount] = cancelShortcut;
            options.Controls.Add(cancelShortcut, 1, 0);
            options.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = T(LeagueEfficiencyUiTextKeys.QuickMessageInterval),
                TextAlign = ContentAlignment.MiddleRight }, 2, 0);
            _interval = new NumericUpDown { Minimum = 0, Maximum = 5000, Increment = 100,
                Value = Math.Max(0, Math.Min(5000, _module.QuickMessageIntervalMs)),
                Dock = DockStyle.Fill, AccessibleName = T(LeagueEfficiencyUiTextKeys.QuickMessageInterval) };
            options.Controls.Add(_interval, 3, 0);
            root.Controls.Add(options, 0, 5);

            var save = new Button
            {
                Dock = DockStyle.Right,
                Width = 164,
                Text = T(LeagueEfficiencyUiTextKeys.QuickMessageSave)
            };
            save.Click += delegate { SavePresets(); };
            root.Controls.Add(save, 0, 6);
            _status = new Label { Dock = DockStyle.Fill, ForeColor = FacmDesignSystem.TextMuted,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            root.Controls.Add(_status, 0, 7);
            for (var index = 0; index < _shortcutButtons.Length; index++) UpdateShortcutLabel(index);
            Controls.Add(root);
            FormClosed += delegate { _lifetime.Cancel(); _lifetime.Dispose(); };
        }

        private void SavePresets()
        {
            var values = new string[_slots.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = LeagueQuickMessagePresets.NormalizeMessage(_slots[i].Text);
                if (!string.IsNullOrWhiteSpace(_slots[i].Text) && values[i].Length == 0)
                {
                    _status.Text = T(LeagueEfficiencyUiTextKeys.QuickMessageInvalid);
                    return;
                }
            }
            string hotkeyJson;
            try { hotkeyJson = LeagueQuickMessageHotkeys.Save(_shortcuts); }
            catch (ArgumentException exception) { _status.Text = exception.Message; return; }
            string error;
            if (!_module.TrySaveQuickMessageSettings(LeagueQuickMessagePresets.Save(values),
                hotkeyJson, (int)_interval.Value, out error))
            {
                _status.Text = error;
                return;
            }
            _status.Text = T(LeagueEfficiencyUiTextKeys.QuickMessageSaved);
        }

        private void CaptureShortcut(int index)
        {
            using (var capture = new LeagueHotkeyCaptureDialog(_ui))
                if (capture.ShowDialog(this) == DialogResult.OK && capture.Binding != null)
                {
                    _shortcuts[index] = capture.Binding.ToString();
                    UpdateShortcutLabel(index);
                }
        }

        private void UpdateShortcutLabel(int index)
        {
            _shortcutButtons[index].Text = string.IsNullOrEmpty(_shortcuts[index])
                ? T(LeagueEfficiencyUiTextKeys.Disabled) : _shortcuts[index];
        }

        private async Task SendAsync(int index)
        {
            if (_sending || _lifetime.IsCancellationRequested) return;
            var message = LeagueQuickMessagePresets.NormalizeMessage(_slots[index].Text);
            if (message.Length == 0)
            {
                _status.Text = T(LeagueEfficiencyUiTextKeys.QuickMessageInvalid);
                return;
            }
            _sending = true;
            foreach (var button in _send) button.Enabled = false;
            try
            {
                var sent = await _module.SendQuickMessageAsync(message, _lifetime.Token);
                if (!IsDisposed)
                    _status.Text = T(sent ? LeagueEfficiencyUiTextKeys.QuickMessageSent :
                        LeagueEfficiencyUiTextKeys.QuickMessageUnavailable);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!IsDisposed) _status.Text = T(LeagueEfficiencyUiTextKeys.QuickMessageUnavailable);
            }
            finally
            {
                _sending = false;
                if (!IsDisposed)
                    foreach (var button in _send) button.Enabled = true;
            }
        }

        private string T(string key) { return LeagueEfficiencyText.Get(_ui, key); }
    }
}
