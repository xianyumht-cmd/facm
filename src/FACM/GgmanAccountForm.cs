using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class GgmanAccountForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly GgmanEmailAuthClient _client = new GgmanEmailAuthClient();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TextBox _email;
        private readonly TextBox _code;
        private readonly Button _send;
        private readonly Button _verify;
        private readonly Button _logout;
        private readonly Label _status;
        private readonly string _deviceId;
        private GgmanEmailChallenge _challenge;
        private DateTimeOffset _lastSentUtc;
        private bool _busy;
        private int _failures;

        internal GgmanAccountForm(UiTextCatalog ui)
        {
            _ui = ui ?? UiTextCatalog.Load();
            _deviceId = CloudIdentityStore.CreateDefault().LoadOrCreate().DeviceId;
            Text = _ui.AppName + " · " + _ui.Get(UiTextKeys.AccountTitle);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(520, 330);
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var title = AddLabel(_ui.Get(UiTextKeys.AccountTitle), 20, 14, 460, 30, 15F, true);
            var hint = AddLabel(_ui.Get(UiTextKeys.AccountHint), 20, 46, 470, 43, 9F);
            AddLabel(_ui.Get(UiTextKeys.AccountEmail), 20, 100, 100, 22, 9F);
            _email = new TextBox { Location = new Point(117, 98), Size = new Size(265, 27), MaxLength = 254 };
            _send = AddButton(UiTextKeys.AccountSendCode, 389, 95, 110);
            _send.Click += async delegate { await SendAsync(); };

            AddLabel(_ui.Get(UiTextKeys.AccountCode), 20, 141, 110, 22, 9F);
            _code = new TextBox { Location = new Point(117, 139), Size = new Size(125, 27), MaxLength = 6 };
            _verify = AddButton(UiTextKeys.AccountVerify, 250, 137, 132);
            _verify.Click += async delegate { await VerifyAsync(); };
            _logout = AddButton(UiTextKeys.AccountLogout, 389, 137, 110);
            _logout.Click += async delegate { await LogoutAsync(); };

            _status = AddLabel(_ui.Get(UiTextKeys.AccountSignedOut), 20, 186, 470, 57, 9F);
            _status.AutoEllipsis = true;
            var privacy = AddLabel(_ui.Get(UiTextKeys.AccountNoRemember), 20, 259, 470, 44, 9F);
            privacy.ForeColor = FacmDesignSystem.TextMuted;
            Controls.Add(_email);
            Controls.Add(_code);
            Shown += delegate { RefreshControls(); };
            FormClosing += delegate { if (!_lifetime.IsCancellationRequested) _lifetime.Cancel(); };
            FormClosed += delegate { _client.Dispose(); _lifetime.Dispose(); };
            RefreshControls();
        }

        private Label AddLabel(string value, int x, int y, int width, int height, float size, bool bold = false)
        {
            var label = new Label
            {
                Text = value,
                Bounds = new Rectangle(x, y, width, height),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = true
            };
            Controls.Add(label);
            return label;
        }

        private Button AddButton(string key, int x, int y, int width)
        {
            var btn = new Button
            {
                Text = _ui.Get(key),
                Bounds = new Rectangle(x, y, width, 31),
                UseVisualStyleBackColor = true
            };
            Controls.Add(btn);
            return btn;
        }

        private void RefreshControls()
        {
            var signedIn = GgmanAccountSession.Current;
            _email.Enabled = !_busy && signedIn == null;
            _code.Enabled = !_busy && signedIn == null && _challenge != null;
            _send.Enabled = !_busy && signedIn == null;
            _verify.Enabled = !_busy && signedIn == null && _challenge != null;
            _logout.Enabled = !_busy && signedIn != null;
            if (!_busy && signedIn != null)
            {
                _email.Text = signedIn.Email;
                _status.Text = string.Format(_ui.Get(UiTextKeys.AccountSignedIn), signedIn.Email);
            }
        }

        private async Task SendAsync()
        {
            if (_busy || GgmanAccountSession.Current != null) return;
            if (_lastSentUtc != default(DateTimeOffset))
            {
                var wait = TimeSpan.FromSeconds(60) - (DateTimeOffset.UtcNow - _lastSentUtc);
                if (wait.TotalSeconds > 0)
                {
                    _status.Text = string.Format(_ui.Get(UiTextKeys.AccountSendWait),
                        Math.Ceiling(wait.TotalSeconds));
                    return;
                }
            }

            _challenge = null;
            _failures = 0;
            var email = _email.Text.Trim();
            SetBusy(true);
            try
            {
                _challenge = await _client.SendCodeAsync(email, _deviceId, _lifetime.Token);
                if (IsDisposed) return;
                _lastSentUtc = DateTimeOffset.UtcNow;
                _code.Text = string.Empty;
                _status.Text = _ui.Get(UiTextKeys.AccountCodeSent);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) SetError(error); }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task VerifyAsync()
        {
            if (_busy || _challenge == null || GgmanAccountSession.Current != null) return;
            if (_failures >= 5)
            {
                _challenge = null;
                _status.Text = _ui.Get(UiTextKeys.AccountTooManyAttempts);
                RefreshControls();
                return;
            }
            if (!string.Equals(_challenge.Email, _email.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _challenge = null;
                _status.Text = _ui.Get(UiTextKeys.AccountCodeSent);
                RefreshControls();
                return;
            }
            SetBusy(true);
            try
            {
                var identity = await _client.VerifyAndLoginAsync(
                    _challenge, _code.Text.Trim(), _deviceId, _lifetime.Token);
                if (IsDisposed) return;
                GgmanAccountSession.Set(identity);
                _challenge = null;
                _code.Text = string.Empty;
                _status.Text = string.Format(_ui.Get(UiTextKeys.AccountVerified), identity.UserId);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                _failures++;
                if (!IsDisposed) SetError(error);
                if (_failures >= 5) _challenge = null;
            }
            finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task LogoutAsync()
        {
            var account = GgmanAccountSession.Current;
            if (account == null || _busy) return;
            SetBusy(true);
            try
            {
                await _client.LogoutAsync(account, _deviceId, _lifetime.Token);
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.AccountLoggedOut);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) SetError(error); }
            finally
            {
                // Always drop local credentials, even when CloudBase logout could not be confirmed.
                GgmanAccountSession.Clear();
                _challenge = null;
                _code.Text = string.Empty;
                _email.Text = string.Empty;
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetBusy(bool value)
        {
            _busy = value;
            if (value) _status.Text = _ui.Get(UiTextKeys.AccountBusy);
            RefreshControls();
        }

        private void SetError(Exception error)
        {
            _status.Text = string.Format(_ui.Get(UiTextKeys.AccountError), error.Message);
        }
    }
}
