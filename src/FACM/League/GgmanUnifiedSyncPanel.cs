using System;
using System.Drawing;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class GgmanUnifiedSyncPanel : UserControl
    {
        private readonly UiTextCatalog _ui;
        private readonly AppSettings _settings;
        private readonly GgmanAutoSyncService _service;
        private readonly Label _title;
        private readonly CheckBox _enabled;
        private readonly Label _hint;
        private readonly Label _status;
        private readonly FacmActionButton _keepLocal;
        private readonly FacmActionButton _useCloud;

        internal event EventHandler ExpandedHeightChanged;

        internal GgmanUnifiedSyncPanel(UiTextCatalog ui, AppSettings settings)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _service = GgmanAutoSyncService.Current;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            Height = 148;

            _title = new Label
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncTitle),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
            };
            _enabled = new CheckBox
            {
                Text = _ui.Get(UiTextKeys.AutoSyncEnabled),
                Checked = _settings.AutoConfigSyncEnabled,
                AutoSize = false,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Enabled = _service != null
            };
            _hint = new Label
            {
                Text = _ui.Get(UiTextKeys.AutoSyncDescription),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _status = new Label
            {
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _keepLocal = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.AutoSyncKeepLocal),
                Tone = FacmButtonTone.Secondary,
                Visible = false
            };
            _useCloud = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.AutoSyncUseCloud),
                Tone = FacmButtonTone.Secondary,
                Visible = false
            };
            _enabled.CheckedChanged += delegate
            {
                if (_service != null) _service.SetEnabled(_enabled.Checked);
                RefreshAccountActions();
            };
            _keepLocal.Click += async delegate { await ResolveAsync(true); };
            _useCloud.Click += async delegate { await ResolveAsync(false); };
            Controls.Add(_title);
            Controls.Add(_enabled);
            Controls.Add(_hint);
            Controls.Add(_status);
            Controls.Add(_keepLocal);
            Controls.Add(_useCloud);
            if (_service != null) _service.StateChanged += OnStateChanged;
            SizeChanged += delegate { Arrange(); };
            Disposed += delegate
            {
                if (_service != null) _service.StateChanged -= OnStateChanged;
            };
            RefreshAccountActions();
        }

        internal void RefreshAccountActions()
        {
            if (IsDisposed) return;
            var visibleConflict = _service != null && _settings.AutoConfigSyncEnabled &&
                !string.IsNullOrEmpty(_service.ConflictCategory);
            var nextHeight = visibleConflict ? 184 : 148;
            _keepLocal.Visible = visibleConflict;
            _useCloud.Visible = visibleConflict;
            _keepLocal.Enabled = visibleConflict && !_service.IsBusy;
            _useCloud.Enabled = visibleConflict && !_service.IsBusy;
            if (_service != null)
            {
                _status.Text = _ui.Get(_service.StatusKey);
                _useCloud.Enabled = _useCloud.Enabled && _service.ConflictHasRemote;
            }
            else
                _status.Text = _ui.Get(UiTextKeys.AutoSyncWaiting);
            if (Height != nextHeight)
            {
                Height = nextHeight;
                ExpandedHeightChanged?.Invoke(this, EventArgs.Empty);
            }
            Arrange();
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) RefreshAccountActions();
        }

        private async System.Threading.Tasks.Task ResolveAsync(bool keepLocal)
        {
            if (_service == null || _service.IsBusy) return;
            if (!keepLocal &&
                MessageBox.Show(this, _ui.Get(UiTextKeys.AutoSyncConflictConfirm),
                    _ui.Get(UiTextKeys.UnifiedSyncTitle),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await _service.ResolveConflictAsync(keepLocal);
            RefreshAccountActions();
        }

        private void Arrange()
        {
            var width = Math.Max(410, ClientSize.Width);
            _title.SetBounds(16, 8, width - 32, 28);
            _enabled.SetBounds(16, 41, width - 32, 30);
            _hint.SetBounds(16, 76, width - 32, 36);
            _status.SetBounds(16, 112, width - 32, 30);
            var buttonWidth = (width - 48) / 2;
            _keepLocal.SetBounds(16, 146, buttonWidth, 30);
            _useCloud.SetBounds(32 + buttonWidth, 146, buttonWidth, 30);
        }

        internal static void ValidateForSmokeTest()
        {
            if (UiTextKeys.AutoSyncEnabled == UiTextKeys.AutoSyncConflict ||
                UiTextKeys.AutoSyncReady == UiTextKeys.AutoSyncOff)
                throw new InvalidOperationException("Automatic config sync UI keys are not unique.");
        }
    }
}
