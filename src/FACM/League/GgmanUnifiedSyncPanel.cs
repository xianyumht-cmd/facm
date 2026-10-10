using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM.League
{
    internal sealed class GgmanUnifiedSyncPanel : UserControl
    {
        private readonly UiTextCatalog _ui;
        private readonly AppSettings _settings;
        private readonly EscSettingsForm _esc;
        private readonly UiTextEditorPanel _textEditor;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly FacmActionButton _upload;
        private readonly FacmActionButton _restore;
        private readonly Label _title;
        private readonly Label _hint;
        private readonly Label _status;
        private bool _busy;

        internal GgmanUnifiedSyncPanel(UiTextCatalog ui, AppSettings settings,
            EscSettingsForm esc, UiTextEditorPanel textEditor)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _esc = esc ?? throw new ArgumentNullException(nameof(esc));
            _textEditor = textEditor ?? throw new ArgumentNullException(nameof(textEditor));
            Height = 152;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            _title = new Label
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncTitle),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
            };
            _hint = new Label
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncHint),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _status = new Label
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncReady),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _upload = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncUpload),
                Tone = FacmButtonTone.Primary
            };
            _restore = new FacmActionButton
            {
                Text = _ui.Get(UiTextKeys.UnifiedSyncRestore),
                Tone = FacmButtonTone.Secondary
            };
            _upload.Click += async delegate { await TransferAsync(true); };
            _restore.Click += async delegate { await TransferAsync(false); };
            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(_upload);
            Controls.Add(_restore);
            Controls.Add(_status);
            SizeChanged += delegate { Arrange(); };
            Disposed += delegate { _cancellation.Cancel(); _cancellation.Dispose(); };
            Arrange();
            RefreshAccountActions();
        }

        internal void RefreshAccountActions()
        {
            var enabled = !_busy && GgmanAccountSession.Current != null;
            _upload.Enabled = enabled;
            _restore.Enabled = enabled;
            if (!enabled && !_busy)
                _status.Text = _ui.Get(UiTextKeys.UnifiedSyncSignedOut);
        }

        private void Arrange()
        {
            var width = Math.Max(420, ClientSize.Width);
            _title.SetBounds(16, 9, width - 32, 27);
            _hint.SetBounds(16, 40, width - 32, 22);
            var buttonWidth = (width - 44) / 2;
            _upload.SetBounds(16, 70, buttonWidth, 32);
            _restore.SetBounds(28 + buttonWidth, 70, buttonWidth, 32);
            _status.SetBounds(16, 110, width - 32, 36);
        }

        private static void RequireSameAccount(GgmanAccountIdentity expected)
        {
            var now = GgmanAccountSession.Current;
            if (expected == null || now == null || now.UserId != expected.UserId ||
                now.AccessToken != expected.AccessToken)
                throw new InvalidOperationException(
                    UiTextRuntime.Text(UiTextKeys.UnifiedSyncSessionChanged));
        }

        private async Task TransferAsync(bool upload)
        {
            if (_busy || IsDisposed || _cancellation.IsCancellationRequested) return;
            var identity = GgmanAccountSession.Current;
            if (identity == null)
            {
                _status.Text = _ui.Get(UiTextKeys.UnifiedSyncSignedOut);
                return;
            }
            if (_textEditor.HasPendingChanges)
            {
                _status.Text = _ui.Get(UiTextKeys.UnifiedSyncUnsaved);
                return;
            }
            _busy = true;
            _upload.Enabled = false;
            _restore.Enabled = false;
            _status.Text = _ui.Get(UiTextKeys.UnifiedSyncWorking);
            var completed = 0;
            try
            {
                var gameDirectory = _esc.CurrentConfigDirectory;
                if (string.IsNullOrWhiteSpace(EscGameDirectoryLocator.ResolveCandidate(gameDirectory)))
                    gameDirectory = await Task.Run(() =>
                        EscGameDirectoryLocator.Find(_settings.GamePath, _cancellation.Token));
                var validGame = EscGameDirectoryLocator.ResolveCandidate(gameDirectory);
                if (IsDisposed || _cancellation.IsCancellationRequested) return;
                RequireSameAccount(identity);

                using (var settingsClient = new GgmanAppSettingsCloudClient())
                using (var textClient = new GgmanUiTextCloudClient())
                using (var gameClient = new GgmanEscCloudClient())
                {
                    var settingsRemote = await settingsClient.GetAsync(identity, _cancellation.Token);
                    var textRemote = await textClient.GetAsync(identity, _cancellation.Token);
                    var gameRemote = await gameClient.GetAsync(identity, _cancellation.Token);
                    if (IsDisposed || _cancellation.IsCancellationRequested) return;
                    RequireSameAccount(identity);

                    if (upload)
                    {
                        var settingsLocal = GgmanPortableSettingsStore.Capture(_settings);
                        var textLocal = UiTextCustomizationStore.Capture();
                        EscSettingsBundle gameLocal = null;
                        if (!string.IsNullOrWhiteSpace(validGame))
                            gameLocal = EscSettingsBackup.Capture(validGame);
                        var prompt = string.Format(_ui.Get(UiTextKeys.UnifiedSyncConfirmUpload),
                            gameLocal == null ? _ui.Get(UiTextKeys.UnifiedSyncNoGame) :
                                _ui.Get(UiTextKeys.UnifiedSyncEsc));
                        if (MessageBox.Show(this, prompt, _ui.Get(UiTextKeys.UnifiedSyncTitle),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        {
                            _status.Text = _ui.Get(UiTextKeys.UnifiedSyncCancelled);
                            return;
                        }
                        RequireSameAccount(identity);
                        await settingsClient.SetAsync(identity, settingsLocal,
                            settingsRemote == null ? 0 : settingsRemote.Version, _cancellation.Token);
                        completed++;
                        RequireSameAccount(identity);
                        await textClient.SetAsync(identity, textLocal,
                            textRemote == null ? 0 : textRemote.Version, _cancellation.Token);
                        completed++;
                        if (gameLocal != null)
                        {
                            RequireSameAccount(identity);
                            await gameClient.SetAsync(identity, gameLocal,
                                gameRemote == null ? 0 : gameRemote.Version, _cancellation.Token);
                            completed++;
                        }
                        if (!IsDisposed)
                            _status.Text = string.Format(_ui.Get(UiTextKeys.UnifiedSyncUploadResult),
                                completed, 3 - completed,
                                gameLocal == null ? _ui.Get(UiTextKeys.UnifiedSyncNoGame) : string.Empty);
                    }
                    else
                    {
                        var available = (settingsRemote == null ? 0 : 1) +
                            (textRemote == null ? 0 : 1) +
                            (gameRemote == null || validGame == null ? 0 : 1);
                        if (available == 0)
                        {
                            _status.Text = _ui.Get(UiTextKeys.UnifiedSyncNoCloud);
                            return;
                        }
                        var versions = new List<string>();
                        if (settingsRemote != null)
                            versions.Add(_ui.Get(UiTextKeys.UnifiedSyncSettings) + " v" + settingsRemote.Version);
                        if (textRemote != null)
                            versions.Add(_ui.Get(UiTextKeys.UnifiedSyncText) + " v" + textRemote.Version);
                        if (gameRemote != null && validGame != null)
                            versions.Add(_ui.Get(UiTextKeys.UnifiedSyncEsc) + " v" + gameRemote.Version);
                        var prompt = string.Format(_ui.Get(UiTextKeys.UnifiedSyncConfirmRestore),
                            available, string.Join("、", versions));
                        if (MessageBox.Show(this, prompt, _ui.Get(UiTextKeys.UnifiedSyncTitle),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        {
                            _status.Text = _ui.Get(UiTextKeys.UnifiedSyncCancelled);
                            return;
                        }
                        RequireSameAccount(identity);
                        if (gameRemote != null && validGame != null)
                        {
                            EscSettingsBackup.Restore(gameRemote.Bundle, validGame);
                            completed++;
                        }
                        if (textRemote != null)
                        {
                            RequireSameAccount(identity);
                            UiTextCustomizationStore.Apply(textRemote.Profile);
                            _textEditor.ReloadAfterExternalRestore();
                            completed++;
                        }
                        if (settingsRemote != null)
                        {
                            RequireSameAccount(identity);
                            GgmanPortableSettingsStore.Restore(settingsRemote.Profile, _settings);
                            completed++;
                        }
                        if (!IsDisposed)
                            _status.Text = string.Format(_ui.Get(UiTextKeys.UnifiedSyncRestoreResult),
                                completed, 3 - completed,
                                gameRemote != null && validGame == null ?
                                    _ui.Get(UiTextKeys.UnifiedSyncNoGame) : string.Empty);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.UnifiedSyncCancelled);
            }
            catch (Exception error)
            {
                AppLog.Warning("Unified account configuration sync failed; type=" +
                    error.GetType().Name + "; completed=" + completed + ".");
                if (!IsDisposed)
                    _status.Text = string.Format(_ui.Get(UiTextKeys.UnifiedSyncError),
                        completed, error.Message);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) RefreshAccountActions();
            }
        }

        internal static void ValidateForSmokeTest()
        {
            if (3 - 2 != 1 || new[] { UiTextKeys.UnifiedSyncSettings,
                    UiTextKeys.UnifiedSyncText, UiTextKeys.UnifiedSyncEsc }.Distinct().Count() != 3)
                throw new InvalidOperationException("Unified sync category contract drifted.");
        }
    }
}
