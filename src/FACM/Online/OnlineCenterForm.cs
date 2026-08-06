using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Online
{
    internal sealed class OnlineCenterForm : Form
    {
        private static readonly Color Background = Color.FromArgb(13, 18, 29);
        private static readonly Color Surface = Color.FromArgb(25, 33, 48);
        private static readonly Color TextPrimary = Color.FromArgb(242, 247, 255);
        private static readonly Color TextMuted = Color.FromArgb(155, 169, 193);
        private static readonly Color Accent = Color.FromArgb(76, 132, 255);

        private readonly MainForm _owner;
        private readonly AppSettings _settings;
        private readonly bool _forceMode;
        private readonly Label _versionValue;
        private readonly Label _updateStatus;
        private readonly Label _announcementTitle;
        private readonly TextBox _announcementBody;
        private readonly Button _updateButton;
        private readonly Button _linkButton;
        private readonly Button _closeButton;
        private readonly ProgressBar _progress;
        private readonly CheckBox _autoUpdate;
        private OnlineSnapshot _snapshot;
        private CancellationTokenSource _cancellation;
        private bool _updateStarted;

        public OnlineCenterForm(MainForm owner, AppSettings settings, OnlineSnapshot snapshot, bool forceMode)
        {
            _owner = owner;
            _settings = settings;
            _snapshot = snapshot ?? new OnlineSnapshot();
            _forceMode = forceMode;

            Text = forceMode ? "FACM 必须更新" : "FACM 在线中心";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = forceMode;
            ClientSize = new Size(540, 610);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9F);
            ControlBox = !forceMode;

            var header = new Label
            {
                Text = forceMode ? "检测到必须安装的新版本" : "在线版本与公告",
                Location = new Point(24, 18),
                Size = new Size(490, 34),
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                ForeColor = TextPrimary
            };
            var subtitle = new Label
            {
                Text = forceMode
                    ? "当前版本已低于允许范围，请完成更新后继续。"
                    : "可手动检查，也可在启动时自动检查并提示。",
                Location = new Point(26, 55),
                Size = new Size(486, 24),
                ForeColor = TextMuted
            };

            var versionPanel = CreatePanel(new Point(20, 92), new Size(500, 156));
            var versionTitle = CreateSectionTitle("版本控制", new Point(16, 13));
            _versionValue = new Label
            {
                Location = new Point(16, 45),
                Size = new Size(464, 25),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            _updateStatus = new Label
            {
                Location = new Point(16, 73),
                Size = new Size(464, 23),
                ForeColor = TextMuted
            };
            _autoUpdate = new CheckBox
            {
                Text = "启动时自动检查并提示更新",
                Location = new Point(16, 105),
                Size = new Size(230, 28),
                Checked = _settings.AutoUpdateEnabled,
                ForeColor = TextPrimary,
                BackColor = Color.Transparent
            };
            _autoUpdate.CheckedChanged += delegate
            {
                _settings.AutoUpdateEnabled = _autoUpdate.Checked;
                _settings.Save();
            };
            var refreshButton = CreateButton("立即检查", new Point(260, 103), 100, false);
            refreshButton.Click += async delegate { await RefreshAsync(); };
            _updateButton = CreateButton("立即更新", new Point(370, 103), 110, true);
            _updateButton.Click += async delegate { await BeginUpdateAsync(); };
            versionPanel.Controls.Add(versionTitle);
            versionPanel.Controls.Add(_versionValue);
            versionPanel.Controls.Add(_updateStatus);
            versionPanel.Controls.Add(_autoUpdate);
            versionPanel.Controls.Add(refreshButton);
            versionPanel.Controls.Add(_updateButton);

            var announcementPanel = CreatePanel(new Point(20, 264), new Size(500, 264));
            var announcementSection = CreateSectionTitle("联网公告", new Point(16, 13));
            _announcementTitle = new Label
            {
                Location = new Point(16, 45),
                Size = new Size(462, 28),
                ForeColor = TextPrimary,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold)
            };
            _announcementBody = new TextBox
            {
                Location = new Point(16, 79),
                Size = new Size(464, 130),
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(18, 24, 36),
                ForeColor = TextPrimary
            };
            _linkButton = CreateButton("打开公告链接", new Point(16, 219), 120, false);
            _linkButton.Click += OpenAnnouncementLink;
            announcementPanel.Controls.Add(announcementSection);
            announcementPanel.Controls.Add(_announcementTitle);
            announcementPanel.Controls.Add(_announcementBody);
            announcementPanel.Controls.Add(_linkButton);

            _progress = new ProgressBar
            {
                Location = new Point(20, 545),
                Size = new Size(500, 14),
                Minimum = 0,
                Maximum = 100,
                Visible = false
            };
            _closeButton = CreateButton(forceMode ? "退出程序" : "关闭", new Point(400, 570), 120, false);
            _closeButton.Click += delegate
            {
                if (_updateStarted) return;
                if (_forceMode) _owner.ExitApplication();
                else Close();
            };

            Controls.Add(header);
            Controls.Add(subtitle);
            Controls.Add(versionPanel);
            Controls.Add(announcementPanel);
            Controls.Add(_progress);
            Controls.Add(_closeButton);

            FormClosing += HandleFormClosing;
            ApplySnapshot();
        }

        public bool HasAvailableUpdate
        {
            get { return _snapshot != null && _snapshot.UpdateAvailable; }
        }

        public async Task BeginAutomaticUpdateAsync()
        {
            if (!_settings.AutoUpdateEnabled || !HasAvailableUpdate || _forceMode) return;

            var choice = MessageBox.Show(
                this,
                "检测到新版本，自动更新已开启。现在下载并安装吗？",
                "FACM 更新",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Yes)
            {
                await BeginUpdateAsync();
            }
        }

        private async Task RefreshAsync()
        {
            SetBusy(true, "正在读取在线配置...");
            try
            {
                _cancellation = new CancellationTokenSource();
                _snapshot = await OnlineService.FetchSnapshotAsync(_cancellation.Token);
                ApplySnapshot();
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task BeginUpdateAsync()
        {
            if (_updateStarted || _snapshot == null || !_snapshot.UpdateAvailable || _snapshot.Update == null) return;

            _updateStarted = true;
            SetBusy(true, "正在下载并校验更新...");
            _progress.Visible = true;
            _progress.Value = 0;
            try
            {
                _cancellation = new CancellationTokenSource();
                var progress = new Progress<int>(value =>
                {
                    _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));
                    _updateStatus.Text = "正在下载更新：" + value + "%";
                });
                var downloaded = await UpdateInstaller.DownloadAsync(
                    _snapshot.Update,
                    progress,
                    _cancellation.Token);
                _updateStatus.Text = "下载完成，正在启动替换程序...";
                UpdateInstaller.StartReplacement(downloaded);
                _owner.ExitApplication();
            }
            catch (OperationCanceledException)
            {
                _updateStarted = false;
                _updateStatus.Text = "更新已取消。";
            }
            catch (Exception exception)
            {
                _updateStarted = false;
                AppLog.Error("Update installation failed", exception);
                MessageBox.Show(this, "更新失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ApplySnapshot();
            }
            finally
            {
                if (!_updateStarted)
                {
                    _progress.Visible = false;
                    SetBusy(false, null);
                }
            }
        }

        private void ApplySnapshot()
        {
            var current = _snapshot.CurrentVersion == null ? "未知" : _snapshot.CurrentVersion.ToString();
            var latest = _snapshot.LatestVersion == null ? "未获取" : _snapshot.LatestVersion.ToString();
            _versionValue.Text = "当前版本：" + current + "    在线版本：" + latest;

            if (!string.IsNullOrWhiteSpace(_snapshot.ErrorMessage))
            {
                _updateStatus.Text = "读取失败：" + _snapshot.ErrorMessage;
                _updateButton.Enabled = false;
            }
            else if (_snapshot.ForceUpdateRequired)
            {
                _updateStatus.Text = "必须更新后才能继续使用。";
                _updateButton.Enabled = true;
            }
            else if (_snapshot.UpdateAvailable)
            {
                _updateStatus.Text = string.IsNullOrWhiteSpace(_snapshot.Update.ReleaseNotes)
                    ? "发现可用的新版本。"
                    : _snapshot.Update.ReleaseNotes;
                _updateButton.Enabled = true;
            }
            else
            {
                _updateStatus.Text = "当前已是最新版本。";
                _updateButton.Enabled = false;
            }

            var announcement = _snapshot.Announcement;
            if (announcement != null && announcement.Enabled)
            {
                _announcementTitle.Text = string.IsNullOrWhiteSpace(announcement.Title) ? "公告" : announcement.Title;
                _announcementBody.Text = announcement.Body ?? string.Empty;
                _linkButton.Enabled = IsHttpsUrl(announcement.LinkUrl);
            }
            else
            {
                _announcementTitle.Text = "暂无公告";
                _announcementBody.Text = "后台当前未启用公告。";
                _linkButton.Enabled = false;
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _updateButton.Enabled = !busy && _snapshot != null && _snapshot.UpdateAvailable;
            _autoUpdate.Enabled = !busy;
            _closeButton.Enabled = !busy || !_forceMode;
            if (!string.IsNullOrWhiteSpace(status)) _updateStatus.Text = status;
            UseWaitCursor = busy;
        }

        private void OpenAnnouncementLink(object sender, EventArgs e)
        {
            var url = _snapshot == null || _snapshot.Announcement == null
                ? null
                : _snapshot.Announcement.LinkUrl;
            if (!IsHttpsUrl(url)) return;
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_updateStarted)
            {
                e.Cancel = true;
                return;
            }

            if (_forceMode && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                _owner.ExitApplication();
            }
        }

        private static Panel CreatePanel(Point location, Size size)
        {
            return new Panel
            {
                Location = location,
                Size = size,
                BackColor = Surface,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static Label CreateSectionTitle(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(460, 25),
                ForeColor = TextMuted,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
        }

        private static Button CreateButton(string text, Point location, int width, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(width, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.FromArgb(38, 49, 68),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(65, 80, 105);
            return button;
        }

        private static bool IsHttpsUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cancellation != null) _cancellation.Dispose();
            base.Dispose(disposing);
        }
    }
}
