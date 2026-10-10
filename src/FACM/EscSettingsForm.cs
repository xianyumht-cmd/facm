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
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _busy;

        internal EscSettingsForm(UiTextCatalog ui, string suggestedGameRoot)
        {
            _ui = ui ?? UiTextCatalog.Load();
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
            ClientSize = new Size(630, 355);
            MinimumSize = Size;
            MaximumSize = Size;

            var title = NewLabel(_ui.Get(UiTextKeys.EscSettingsTitle), 16, 15, 590, 30, true);
            var hint = NewLabel(_ui.Get(UiTextKeys.EscSettingsHint), 16, 49, 596, 41);
            var folderLabel = NewLabel(_ui.Get(UiTextKeys.EscSettingsFolder), 16, 102, 580, 22);
            _directory = new TextBox
            {
                Location = new Point(16, 130),
                Size = new Size(482, 26),
                ReadOnly = true,
                TabStop = true
            };
            if (!string.IsNullOrWhiteSpace(suggestedGameRoot))
            {
                try { _directory.Text = EscSettingsBackup.FindConfigDirectory(suggestedGameRoot); }
                catch { }
            }

            var browse = NewButton(UiTextKeys.EscSettingsBrowse, 510, 127, 102);
            browse.Click += delegate
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

            var note = NewLabel(_ui.Get(UiTextKeys.EscSettingsCloudScope), 16, 237, 590, 43);
            note.ForeColor = FacmDesignSystem.TextMuted;
            _status = NewLabel(_ui.Get(UiTextKeys.EscSettingsReady), 16, 286, 595, 55);
            _status.AutoEllipsis = true;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(folderLabel);
            Controls.Add(_directory);
            Controls.Add(browse);
            foreach (var action in _actions) Controls.Add(action);
            Controls.Add(note);
            Controls.Add(_status);
            Activated += delegate { if (!_busy) SetBusy(false); };
            FormClosed += delegate { _cancellation.Cancel(); _cancellation.Dispose(); };
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
                EscSettingsBackup.RequireGameClosed();
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
                EscSettingsBackup.RequireGameClosed();
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

        private async Task TransferCloudAsync(bool upload)
        {
            if (_busy || IsDisposed) return;
            GgmanAccountIdentity account;
            EscSettingsBundle local = null;
            string directory;
            try
            {
                account = GgmanEscCloudClient.RequireRegisteredSession();
                EscSettingsBackup.RequireGameClosed();
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
                        EscSettingsBackup.RequireGameClosed();
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
                        EscSettingsBackup.RequireGameClosed();
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
                        EscSettingsBackup.RequireGameClosed();
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
