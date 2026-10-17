using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class GgmanCaptchaForm : Form
    {
        private readonly UiTextCatalog _ui;
        private readonly GgmanEmailAuthClient _client;
        private readonly string _deviceId;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly PictureBox _image;
        private readonly TextBox _answer;
        private readonly Label _status;
        private readonly Button _refresh;
        private readonly Button _verify;
        private GgmanImageCaptcha _challenge;
        private bool _busy;
        private int _failures;
        internal string VerifiedToken { get; private set; }

        internal GgmanCaptchaForm(UiTextCatalog ui, GgmanEmailAuthClient client, string deviceId)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _deviceId = deviceId;
            Text = _ui.Get(UiTextKeys.AccountCaptchaTitle);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(432, 304);

            var hint = AddLabel(_ui.Get(UiTextKeys.AccountCaptchaHint), 22, 18, 388, 40);
            _image = new PictureBox
            {
                Location = new Point(22, 64),
                Size = new Size(252, 98),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            Controls.Add(_image);

            _refresh = AddButton(UiTextKeys.AccountCaptchaRefresh, 286, 92, 122);
            _refresh.Click += async delegate { await LoadCaptchaAsync(); };
            AddLabel(_ui.Get(UiTextKeys.AccountCaptchaCode), 22, 178, 112, 28);
            _answer = new TextBox
            {
                Location = new Point(140, 177),
                Size = new Size(133, 30),
                MaxLength = 6
            };
            Controls.Add(_answer);
            _verify = AddButton(UiTextKeys.AccountCaptchaVerify, 286, 176, 122);
            _verify.Click += async delegate { await VerifyCaptchaAsync(); };
            _status = AddLabel(_ui.Get(UiTextKeys.AccountCaptchaLoading), 22, 221, 388, 65);
            _status.AutoEllipsis = false;
            hint.ForeColor = FacmDesignSystem.TextMuted;

            Shown += async delegate { await LoadCaptchaAsync(); };
            FormClosing += delegate
            {
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
            };
            FormClosed += delegate
            {
                var image = _image.Image;
                _image.Image = null;
                if (image != null) image.Dispose();
                _lifetime.Dispose();
            };
            UpdateControls();
        }

        private Label AddLabel(string copy, int x, int y, int width, int height)
        {
            var label = new Label
            {
                Text = copy,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.Transparent,
                ForeColor = FacmDesignSystem.Text
            };
            Controls.Add(label);
            return label;
        }

        private Button AddButton(string key, int x, int y, int width)
        {
            var button = new FacmActionButton
            {
                Text = _ui.Get(key),
                Location = new Point(x, y),
                Size = new Size(width, 34),
                Tone = key == UiTextKeys.AccountCaptchaVerify
                    ? FacmButtonTone.Primary : FacmButtonTone.Secondary,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9.5F, FontStyle.Bold),
                TabStop = true
            };
            Controls.Add(button);
            return button;
        }

        private void UpdateControls()
        {
            _refresh.Enabled = !_busy;
            _verify.Enabled = !_busy && _challenge != null;
            _answer.Enabled = !_busy && _challenge != null;
        }

        private async Task LoadCaptchaAsync()
        {
            if (_busy || IsDisposed) return;
            _busy = true;
            _challenge = null;
            _answer.Clear();
            _status.Text = _ui.Get(UiTextKeys.AccountCaptchaLoading);
            UpdateControls();
            try
            {
                var challenge = await _client.GetCaptchaAsync(_deviceId, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                Image next;
                using (var stream = new MemoryStream(challenge.ImageBytes, false))
                using (var original = Image.FromStream(stream, true, true))
                    next = new Bitmap(original);
                var previous = _image.Image;
                _image.Image = next;
                if (previous != null) previous.Dispose();
                _challenge = challenge;
                _failures = 0;
                _status.Text = _ui.Get(UiTextKeys.AccountCaptchaReady);
                _answer.Focus();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed)
                    _status.Text = string.Format(_ui.Get(UiTextKeys.AccountCaptchaFailed), error.Message);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _busy = false;
                    UpdateControls();
                }
            }
        }

        private async Task VerifyCaptchaAsync()
        {
            if (_busy || _challenge == null || IsDisposed) return;
            if (_failures >= 3)
            {
                _status.Text = _ui.Get(UiTextKeys.AccountCaptchaRetryLimit);
                _challenge = null;
                UpdateControls();
                return;
            }
            _busy = true;
            UpdateControls();
            try
            {
                var verified = await _client.VerifyCaptchaAsync(
                    _challenge, _answer.Text.Trim(), _deviceId, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                VerifiedToken = verified;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                _failures++;
                if (!IsDisposed)
                {
                    _status.Text = string.Format(_ui.Get(UiTextKeys.AccountCaptchaFailed), error.Message);
                    if (_failures >= 3)
                    {
                        _challenge = null;
                        _status.Text = _ui.Get(UiTextKeys.AccountCaptchaRetryLimit);
                    }
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    _busy = false;
                    UpdateControls();
                }
            }
        }
    }
}
