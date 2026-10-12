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
        private readonly FacmActionButton _publishGame;
        private readonly Label _gameHint;
        private readonly CheckBox _autoRestoreGame;
        private readonly Label _gameStatus;
        private bool _publishingGame;

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
            _gameHint = new Label
            {
                Text = "英雄联盟游戏设置 · 仅手动上传、登录后自动恢复",
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _autoRestoreGame = new CheckBox
            {
                Text = "登录后自动恢复云端游戏设置",
                Checked = _settings.GameAutoRestoreEnabled,
                AutoSize = false,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Enabled = _service != null
            };
            _autoRestoreGame.CheckedChanged += delegate
            {
                if (_service != null)
                    _service.SetGameAutoRestoreEnabled(_autoRestoreGame.Checked);
            };
            _publishGame = new FacmActionButton
            {
                Text = "上传大厅与 ESC 设置",
                Tone = FacmButtonTone.Primary
            };
            _gameStatus = new Label
            {
                Text = "仅保存指定大厅偏好与 ESC 文件，不上传账号或安装标识。",
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _publishGame.Click += async delegate { await PublishGameAsync(); };
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
            Controls.Add(_gameHint);
            Controls.Add(_autoRestoreGame);
            Controls.Add(_publishGame);
            Controls.Add(_gameStatus);
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
            var nextHeight = visibleConflict ? 328 : 291;
            _publishGame.Enabled = !_publishingGame && _service != null &&
                !_service.IsBusy && GgmanAccountSession.Current != null;
            _keepLocal.Visible = visibleConflict;
            _useCloud.Visible = visibleConflict;
            _keepLocal.Enabled = visibleConflict && !_service.IsBusy;
            _useCloud.Enabled = visibleConflict && !_service.IsBusy;
            if (_service != null)
            {
                var status = _ui.Get(_service.StatusKey);
                if (visibleConflict)
                {
                    var key = _service.ConflictCategory == "app" ? UiTextKeys.UnifiedSyncSettings :
                        _service.ConflictCategory == "text" ? UiTextKeys.UnifiedSyncText :
                        UiTextKeys.UnifiedSyncEsc;
                    status = string.Format(status, _ui.Get(key));
                }
                _status.Text = status;
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
            if (_service.ConflictCategory == "text" &&
                UiTextEditorPanel.HasPendingVisibleChanges)
            {
                _status.Text = _ui.Get(UiTextKeys.UnifiedSyncUnsaved);
                return;
            }
            if (!keepLocal &&
                MessageBox.Show(this, _ui.Get(UiTextKeys.AutoSyncConflictConfirm),
                    _ui.Get(UiTextKeys.UnifiedSyncTitle),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await _service.ResolveConflictAsync(keepLocal);
            RefreshAccountActions();
        }

        private async System.Threading.Tasks.Task PublishGameAsync()
        {
            if (_service == null || _publishingGame || _service.IsBusy) return;
            if (MessageBox.Show(this,
                    "将当前电脑的游戏内 ESC 设置和指定大厅偏好保存到你的 GGman 云端账号？" +
                    "这会替换你上次主动上传的游戏配置。请先退出英雄联盟及大厅。",
                    "上传大厅与 ESC 设置", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _publishingGame = true;
            _gameStatus.Text = "正在校验并上传游戏配置…";
            RefreshAccountActions();
            try
            {
                var version = await _service.PublishGameProfileAsync();
                if (!IsDisposed)
                    _gameStatus.Text = "游戏配置已保存到云端，版本 " + version + "。";
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                    _gameStatus.Text = "上传未完成：" + error.Message;
            }
            finally
            {
                _publishingGame = false;
                if (!IsDisposed) RefreshAccountActions();
            }
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
            var gameTop = _useCloud.Visible ? 192 : 155;
            _gameHint.SetBounds(16, gameTop, width - 32, 28);
            _autoRestoreGame.SetBounds(16, gameTop + 26, width - 32, 29);
            _publishGame.SetBounds(16, gameTop + 57, width - 32, 34);
            _gameStatus.SetBounds(16, gameTop + 97, width - 32, 38);
        }

        internal static void ValidateForSmokeTest()
        {
            if (UiTextKeys.AutoSyncEnabled == UiTextKeys.AutoSyncConflict ||
                UiTextKeys.AutoSyncReady == UiTextKeys.AutoSyncOff)
                throw new InvalidOperationException("Automatic config sync UI keys are not unique.");
        }
    }
}
