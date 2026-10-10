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
        private readonly FacmActionButton _send;
        private readonly FacmActionButton _verify;
        private readonly FacmActionButton _logout;
        private readonly FacmGlassPanel _loginPanel;
        private readonly FacmGlassPanel _sessionPanel;
        private readonly Label _sessionEmail;
        private readonly Label _sessionUid;
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
            ClientSize = new Size(520, 344);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            AddLabel(this, _ui.Get(UiTextKeys.AccountTitle), new Rectangle(24, 18, 472, 32), 16F, true);
            AddLabel(this, _ui.Get(UiTextKeys.AccountHint), new Rectangle(24, 56, 472, 40), 9F, muted: true);

            _loginPanel = CreateCard();
            AddLabel(_loginPanel, _ui.Get(UiTextKeys.AccountEmail), new Rectangle(18, 12, 270, 20), 9F, muted: true);
            _email = AddField(_loginPanel, new Rectangle(18, 38, 295, 29), 254);
            _send = AddButton(_loginPanel, UiTextKeys.AccountSendCode, new Rectangle(325, 36, 136, 34),
                FacmButtonTone.Secondary);
            _send.Click += async delegate { await SendAsync(); };

            AddLabel(_loginPanel, _ui.Get(UiTextKeys.AccountCode), new Rectangle(18, 83, 270, 20), 9F, muted: true);
            _code = AddField(_loginPanel, new Rectangle(18, 109, 165, 29), 6);
            _verify = AddButton(_loginPanel, UiTextKeys.AccountVerify, new Rectangle(325, 107, 136, 34),
                FacmButtonTone.Primary);
            _verify.Click += async delegate { await VerifyAsync(); };
            Controls.Add(_loginPanel);

            _sessionPanel = CreateCard();
            _sessionEmail = AddLabel(_sessionPanel, string.Empty, new Rectangle(18, 19, 444, 28), 10F, true);
            _sessionEmail.AutoEllipsis = true;
            _sessionUid = AddLabel(_sessionPanel, string.Empty, new Rectangle(18, 56, 444, 22), 9F, muted: true);
            _sessionUid.AutoEllipsis = true;
            _logout = AddButton(_sessionPanel, UiTextKeys.AccountLogout, new Rectangle(18, 107, 136, 34),
                FacmButtonTone.Secondary);
            _logout.Click += async delegate { await LogoutAsync(); };
            Controls.Add(_sessionPanel);

            _status = AddLabel(this, _ui.Get(UiTextKeys.AccountSignedOut),
                new Rectangle(24, 277, 472, 31), 9F, muted: true);
            _status.AutoEllipsis = true;
            var privacy = AddLabel(this, _ui.Get(UiTextKeys.AccountNoRemember),
                new Rectangle(24, 315, 472, 24), 8F, muted: true);
            privacy.AutoEllipsis = true;

            Shown += delegate { RefreshControls(); };
            FormClosing += delegate { if (!_lifetime.IsCancellationRequested) _lifetime.Cancel(); };
            FormClosed += delegate { _client.Dispose(); _lifetime.Dispose(); };
            RefreshControls();
        }

        private static FacmGlassPanel CreateCard()
        {
            return new FacmGlassPanel
            {
                Bounds = new Rectangle(20, 102, 480, 164),
                Radius = FacmDesignSystem.CardRadius,
                DrawBorder = true,
                BackColor = FacmDesignSystem.Surface
            };
        }

        private Label AddLabel(Control parent, string value, Rectangle bounds, float size,
            bool bold = false, bool muted = false)
        {
            var label = new Label
            {
                Text = value,
                Bounds = bounds,
                ForeColor = muted ? FacmDesignSystem.TextMuted : FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular)
            };
            parent.Controls.Add(label);
            return label;
        }

        private TextBox AddField(Control parent, Rectangle bounds, int maxLength)
        {
            var field = new TextBox
            {
                Bounds = bounds,
                MaxLength = maxLength,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FacmDesignSystem.SurfaceRaised,
                ForeColor = FacmDesignSystem.Text,
                Font = new Font(Font.FontFamily, 10F)
            };
            parent.Controls.Add(field);
            return field;
        }

        private FacmActionButton AddButton(Control parent, string key, Rectangle bounds, FacmButtonTone tone)
        {
            var button = new FacmActionButton
            {
                Text = _ui.Get(key),
                Bounds = bounds,
                Tone = tone,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
            };
            parent.Controls.Add(button);
            return button;
        }

        private void RefreshControls()
        {
            var signedIn = GgmanAccountSession.Current;
            _email.Enabled = !_busy && signedIn == null;
            _code.Enabled = !_busy && signedIn == null && _challenge != null;
            _send.Enabled = !_busy && signedIn == null;
            _verify.Enabled = !_busy && signedIn == null && _challenge != null;
            _logout.Enabled = !_busy && signedIn != null;
            _loginPanel.Visible = signedIn == null;
            _sessionPanel.Visible = signedIn != null;
            _status.Visible = signedIn == null;
            if (signedIn != null)
            {
                _sessionEmail.Text = string.Format(_ui.Get(UiTextKeys.AccountSignedIn), signedIn.Email);
                _sessionUid.Text = string.Format(_ui.Get(UiTextKeys.AccountUid), signedIn.UserId);
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

            var email = _email.Text.Trim();
            if (!GgmanEmailAuthClient.IsValidEmail(email))
            {
                _status.Text = _ui.Get(UiTextKeys.AccountEmailInvalid);
                return;
            }
            _challenge = null;
            _failures = 0;
            _lastSentUtc = DateTimeOffset.UtcNow;
            SetBusy(true);
            try
            {
                try
                {
                    _challenge = await _client.SendCodeAsync(email, _deviceId, _lifetime.Token);
                }
                catch (GgmanCaptchaRequiredException)
                {
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    string captchaToken;
                    using (var captcha = new GgmanCaptchaForm(_ui, _client, _deviceId))
                    {
                        if (captcha.ShowDialog(this) != DialogResult.OK ||
                            string.IsNullOrWhiteSpace(captcha.VerifiedToken))
                        {
                            _status.Text = _ui.Get(UiTextKeys.AccountCaptchaCancelled);
                            return;
                        }
                        captchaToken = captcha.VerifiedToken;
                    }
                    if (IsDisposed || _lifetime.IsCancellationRequested) return;
                    _challenge = await _client.SendCodeAsync(email, _deviceId,
                        _lifetime.Token, captchaToken);
                }
                if (IsDisposed) return;
                _code.Text = string.Empty;
                _status.Text = _ui.Get(UiTextKeys.AccountCodeSent);
                _code.Focus();
            }
            catch (GgmanCaptchaInvalidException)
            {
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.AccountCaptchaInvalid);
            }
            catch (GgmanCaptchaRequiredException)
            {
                if (!IsDisposed) _status.Text = _ui.Get(UiTextKeys.AccountCaptchaInvalid);
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
                _status.Text = _ui.Get(UiTextKeys.AccountEmailChanged);
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
                DialogResult = DialogResult.OK;
                Close();
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
