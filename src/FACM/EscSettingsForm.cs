using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class EscSettingsForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly TextBox _directory;
        private readonly Label _status;
        private readonly Button[] _actions;
        private readonly Button _probe;
        private readonly Button _browse;
        private readonly Button _autoDetect;
        private readonly Label _note;
        private readonly string _configuredGameRoot;
        private bool _detecting;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _busy;

        internal EscSettingsForm(UiTextCatalog ui, string suggestedGameRoot)
        {
            _ui = ui ?? UiTextCatalog.Load();
            _configuredGameRoot = suggestedGameRoot;
            Text = _ui.AppName + " · " + _ui.Get(UiTextKeys.EscSettingsTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            ShowInTaskbar = false;
            ClientSize = new Size(630, 410);
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;

            var title = NewLabel(_ui.Get(UiTextKeys.EscSettingsTitle), 16, 15, 590, 30, true);
            var hint = NewLabel(_ui.Get(UiTextKeys.EscSettingsHint), 16, 49, 596, 41);
            var folderLabel = NewLabel(_ui.Get(UiTextKeys.EscSettingsFolder), 16, 102, 580, 22);
            _directory = new TextBox
            {
                Location = new Point(16, 130),
                Size = new Size(357, 26),
                ReadOnly = true,
                TabStop = true
            };
            if (!string.IsNullOrWhiteSpace(suggestedGameRoot))
            {
                try { _directory.Text = EscSettingsBackup.FindConfigDirectory(suggestedGameRoot); }
                catch { }
            }

            _autoDetect = NewButton(UiTextKeys.EscSettingsDetect, 381, 127, 112);
            _autoDetect.Click += async delegate { await DetectDirectoryAsync(); };
            _browse = NewButton(UiTextKeys.EscSettingsBrowse, 507, 127, 107);
            _browse.Click += delegate
            {
                using (var picker = new FolderBrowserDialog
                {
                    Description = _ui.Get(UiTextKeys.EscSettingsFolder),
                    ShowNewFolderButton = false,
                    SelectedPath = Directory.Exists(_directory.Text) ? _directory.Text : string.Empty
                })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        _directory.Text = EscSettingsBackup.FindConfigDirectory(picker.SelectedPath);
                        SetStatus(_ui.Get(UiTextKeys.EscSettingsReady));
                    }
                    catch (Exception exception) { ShowFailure(exception); }
                }
            };

            _actions = new[]
            {
                NewButton(UiTextKeys.EscSettingsLocalSave, 16, 180, 141),
                NewButton(UiTextKeys.EscSettingsLocalRestore, 169, 180, 141),
                NewButton(UiTextKeys.EscSettingsCloudUpload, 322, 180, 141),
                NewButton(UiTextKeys.EscSettingsCloudRestore, 475, 180, 139)
            };
            var authenticated = GgmanAccountSession.Current != null;
            _actions[2].Enabled = authenticated;
            _actions[3].Enabled = authenticated;
            _actions[0].Click += delegate { SaveLocal(); };
            _actions[1].Click += delegate { RestoreLocal(); };
            _actions[2].Click += async delegate { await TransferCloudAsync(true); };
            _actions[3].Click += async delegate { await TransferCloudAsync(false); };

            _probe = NewButton(UiTextKeys.EscSettingsProbe, 16, 231, 160);
            _probe.Enabled = authenticated;
            _probe.Click += async delegate { await ProbeCloudAsync(); };

            _note = NewLabel(_ui.Get(UiTextKeys.EscSettingsCloudScope), 16, 279, 590, 43);
            _note.ForeColor = FacmDesignSystem.TextMuted;
            _status = NewLabel(_ui.Get(UiTextKeys.EscSettingsReady), 16, 332, 595, 58);
            _status.AutoEllipsis = true;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(folderLabel);
            Controls.Add(_directory);
            Controls.Add(_autoDetect);
            Controls.Add(_browse);
            foreach (var action in _actions) Controls.Add(action);
            Controls.Add(_probe);
            Controls.Add(_note);
            Controls.Add(_status);
            Activated += delegate { if (!_busy) SetBusy(false); };
            ClientSizeChanged += delegate { LayoutEscPanel(); };
            Shown += async delegate { await DetectDirectoryAsync(); };
            Disposed += delegate { _cancellation.Cancel(); _cancellation.Dispose(); };
            LayoutEscPanel();
        }

        private async Task DetectDirectoryAsync()
        {
            if (_busy || _detecting || IsDisposed) return;
            _detecting = true;
            _autoDetect.Enabled = false;
            SetStatus(_ui.Get(UiTextKeys.EscSettingsDetecting));
            try
            {
                var found = await Task.Run(() =>
                    EscGameDirectoryLocator.Find(_configuredGameRoot, _cancellation.Token), _cancellation.Token);
                if (IsDisposed || _cancellation.IsCancellationRequested) return;
                if (!string.IsNullOrWhiteSpace(found))
                {
                    _directory.Text = found;
                    SetStatus(_ui.Get(UiTextKeys.EscSettingsDetected));
                }
                else
                    SetStatus(_ui.Get(UiTextKeys.EscSettingsNotFound));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (!IsDisposed) ShowFailure(exception); }
            finally
            {
                _detecting = false;
                if (!IsDisposed) _autoDetect.Enabled = true;
            }
        }

        private void LayoutEscPanel()
        {
            if (_directory == null || _actions == null || _probe == null) return;
            var width = Math.Max(390, ClientSize.Width);
            _directory.Width = Math.Max(120, width - 264);
            _autoDetect.Left = width - 249;
            _browse.Left = width - 123;
            var compact = width < 620;
            if (compact)
            {
                var buttonWidth = (width - 48) / 2;
                for (var index = 0; index < _actions.Length; index++)
                    _actions[index].SetBounds(16 + (index % 2) * (buttonWidth + 16),
                        180 + (index / 2) * 42, buttonWidth, 35);
                _probe.Top = 273;
                _note.SetBounds(16, 317, width - 32, 67);
                _status.SetBounds(16, 390, width - 32, 60);
            }
            else
            {
                var buttonWidth = (width - 64) / 4;
                for (var index = 0; index < _actions.Length; index++)
                    _actions[index].SetBounds(16 + index * (buttonWidth + 10),
                        180, buttonWidth, 35);
                _probe.Top = 231;
                _note.SetBounds(16, 279, width - 32, 45);
                _status.SetBounds(16, 332, width - 32, 58);
            }
        }

        private Label NewLabel(string text, int x, int y, int width, int height, bool heading = false)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font(Font.FontFamily, heading ? 14F : 9F,
                    heading ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent
            };
        }

        private Button NewButton(string key, int x, int y, int width)
        {
            return new FacmActionButton
            {
                Text = _ui.Get(key),
                Location = new Point(x, y),
                Size = new Size(width, 35),
                Tone = (key == UiTextKeys.EscSettingsCloudUpload ||
                    key == UiTextKeys.EscSettingsLocalSave)
                        ? FacmButtonTone.Primary : FacmButtonTone.Secondary
            };
        }

        private string RequireDirectory()
        {
            return EscSettingsBackup.FindConfigDirectory(_directory.Text);
        }

        private void SaveLocal()
        {
            try
            {
                var snapshot = EscSettingsBackup.Capture(RequireDirectory());
                var path = EscSettingsBackup.SaveLocal(snapshot, "manual");
                SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsLocalSaved), path));
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void RestoreLocal()
        {
            try
            {
                var directory = RequireDirectory();
                using (var picker = new OpenFileDialog
                {
                    Title = _ui.Get(UiTextKeys.EscSettingsPickBackup),
                    Filter = "GGman ESC backup (*.json)|*.json",
                    Multiselect = false,
                    CheckFileExists = true,
                    InitialDirectory = Path.Combine(RuntimePaths.DataDirectory, "esc-backups")
                })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    var bundle = EscSettingsBackup.ReadLocal(picker.FileName);
                    ApplyAfterConfirmation(bundle, directory);
                }
            }
            catch (Exception exception) { ShowFailure(exception); }
        }

        private void ApplyAfterConfirmation(EscSettingsBundle bundle, string directory)
        {
            var names = string.Join("、", bundle.Files.Select(file => file.Name));
            var prompt = string.Format(_ui.Get(UiTextKeys.EscSettingsRestoreConfirm), names);
            if (MessageBox.Show(this, prompt, Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                SetStatus(_ui.Get(UiTextKeys.EscSettingsCancelled));
                return;
            }
            var recovery = EscSettingsBackup.Restore(bundle, directory);
            SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsRestored), recovery));
        }

        private async Task ProbeCloudAsync()
        {
            if (_busy || IsDisposed) return;
            GgmanAccountIdentity account;
            try { account = GgmanEscCloudClient.RequireRegisteredSession(); }
            catch (Exception exception) { ShowFailure(exception); return; }

            SetBusy(true);
            try
            {
                using (var client = new GgmanEscCloudClient())
                {
                    var revision = await client.CheckReadOnlyAccessAsync(account, _cancellation.Token);
                    if (IsDisposed || _cancellation.IsCancellationRequested) return;
                    AssertSessionStillValid(account);
                    SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsProbePassed),
                        revision == 0 ? _ui.Get(UiTextKeys.EscSettingsProbeEmpty) :
                            revision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) SetStatus(_ui.Get(UiTextKeys.EscSettingsCancelled));
            }
            catch (Exception exception) { if (!IsDisposed) ShowFailure(exception); }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task TransferCloudAsync(bool upload)
        {
            if (_busy || IsDisposed) return;
            GgmanAccountIdentity account;
            EscSettingsBundle local = null;
            string directory;
            try
            {
                account = GgmanEscCloudClient.RequireRegisteredSession();
                directory = RequireDirectory();
                if (upload)
                {
                    local = EscSettingsBackup.Capture(directory);
                    EscSettingsBackup.SaveLocal(local, "before-upload");
                }
            }
            catch (Exception exception) { ShowFailure(exception); return; }

            SetBusy(true);
            try
            {
                using (var client = new GgmanEscCloudClient())
                {
                    var existing = await client.GetAsync(account, _cancellation.Token);
                    if (IsDisposed || _cancellation.IsCancellationRequested) return;
                    AssertSessionStillValid(account);
                    if (upload)
                    {
                        var message = string.Format(_ui.Get(UiTextKeys.EscSettingsUploadConfirm),
                            existing == null ? 0L : existing.Version,
                            string.Join("、", local.Files.Select(file => file.Name)));
                        if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        {
                            SetStatus(_ui.Get(UiTextKeys.EscSettingsCancelled));
                            return;
                        }
                        AssertSessionStillValid(account);
                        var revision = await client.SetAsync(account, local,
                            existing == null ? 0L : existing.Version, _cancellation.Token);
                        if (!IsDisposed)
                            SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsUploaded), revision));
                    }
                    else if (existing == null)
                    {
                        SetStatus(_ui.Get(UiTextKeys.EscSettingsCloudEmpty));
                    }
                    else
                    {
                        AssertSessionStillValid(account);
                        var names = string.Join("、", existing.Bundle.Files.Select(file => file.Name));
                        var prompt = string.Format(_ui.Get(UiTextKeys.EscSettingsCloudRestoreConfirm),
                            existing.Version, existing.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), names);
                        if (MessageBox.Show(this, prompt, Text, MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        {
                            SetStatus(_ui.Get(UiTextKeys.EscSettingsCancelled));
                            return;
                        }
                        AssertSessionStillValid(account);
                        var recovery = EscSettingsBackup.Restore(existing.Bundle, directory);
                        SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsRestored), recovery));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) SetStatus(_ui.Get(UiTextKeys.EscSettingsCancelled));
            }
            catch (Exception exception) { if (!IsDisposed) ShowFailure(exception); }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private static void AssertSessionStillValid(GgmanAccountIdentity expected)
        {
            var current = GgmanAccountSession.Current;
            if (expected == null || current == null ||
                !string.Equals(current.UserId, expected.UserId, StringComparison.Ordinal) ||
                !string.Equals(current.AccessToken, expected.AccessToken, StringComparison.Ordinal))
                throw new InvalidOperationException(UiTextRuntime.Text(UiTextKeys.EscSettingsSessionChanged));
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            for (var index = 0; index < _actions.Length; index++)
                _actions[index].Enabled = !busy && (index < 2 || GgmanAccountSession.Current != null);
            _probe.Enabled = !busy && GgmanAccountSession.Current != null;
            if (busy) SetStatus(_ui.Get(UiTextKeys.EscSettingsBusy));
        }

        private void SetStatus(string value)
        {
            _status.Text = value ?? string.Empty;
        }

        private void ShowFailure(Exception exception)
        {
            SetStatus(string.Format(_ui.Get(UiTextKeys.EscSettingsOperationFailed), exception.Message));
        }
    }
}
