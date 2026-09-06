using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Services;
using FACM.Theming;

namespace FACM.Online
{
    internal sealed class OnlineCenterForm : Form
    {
        private readonly MainForm _owner;
        private readonly AppSettings _settings;
        private readonly bool _forceMode;
        private readonly Label _versionValue;
        private readonly Label _updateStatus;
        private readonly FacmStatusBadge _updateBadge;
        private readonly Label _announcementTitle;
        private readonly TextBox _announcementBody;
        private readonly FacmActionButton _refreshButton;
        private readonly FacmActionButton _updateButton;
        private readonly FacmActionButton _linkButton;
        private readonly FacmActionButton _closeButton;
        private readonly ProgressBar _progress;
        private readonly FacmToggleSwitch _autoUpdate;
        private OnlineSnapshot _snapshot;
        private CancellationTokenSource _cancellation;
        private bool _updateStarted;
        private bool _closing;

        public OnlineCenterForm(MainForm owner, AppSettings settings, OnlineSnapshot snapshot, bool forceMode)
        {
            _owner = owner;
            _settings = settings;
            _snapshot = snapshot ?? new OnlineSnapshot();
            _forceMode = forceMode;

            Text = forceMode ? "FACM 必须更新" : "FACM 检查更新";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = forceMode;
            ClientSize = new Size(560, 620);
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            ControlBox = !forceMode;
            FacmWindowChrome.SetSubtitle(this, forceMode ? "必须完成更新后继续" : "版本、公告与自动更新");

            var header = new Label
            {
                Text = forceMode ? "检测到必须安装的新版本" : "检查更新与公告",
                Location = new Point(24, 18),
                Size = new Size(510, 34),
                Font = new Font(FacmThemeRuntime.Current.FontName, 17F, FontStyle.Bold),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent
            };
            var subtitle = new Label
            {
                Text = forceMode
                    ? "当前版本已不再支持，请更新后继续使用。"
                    : "查看版本更新和最新公告。",
                Location = new Point(26, 55),
                Size = new Size(508, 24),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };

            var versionPanel = CreatePanel(new Point(20, 92), new Size(520, 172));
            var versionTitle = CreateSectionTitle("版本更新", new Point(16, 13));
            _updateBadge = new FacmStatusBadge
            {
                Location = new Point(398, 11),
                Size = new Size(104, 27),
                Text = "检查中",
                Tone = FacmStatusTone.Neutral
            };
            _versionValue = new Label
            {
                Location = new Point(16, 43),
                Size = new Size(486, 25),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 10F, FontStyle.Bold)
            };
            _updateStatus = new Label
            {
                Location = new Point(16, 70),
                Size = new Size(486, 40),
                AutoEllipsis = true,
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent
            };
            _autoUpdate = new FacmToggleSwitch
            {
                Text = "启动时自动检查更新",
                Location = new Point(16, 124),
                Size = new Size(244, 32),
                Checked = _settings.AutoUpdateEnabled,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F)
            };
            _autoUpdate.CheckedChanged += delegate
            {
                _settings.AutoUpdateEnabled = _autoUpdate.Checked;
                _settings.Save();
            };
            _refreshButton = CreateButton("立即检查", new Point(282, 124), 100, FacmButtonTone.Secondary);
            _refreshButton.Click += async delegate { await RefreshAsync(); };
            _updateButton = CreateButton("立即更新", new Point(392, 124), 110, FacmButtonTone.Primary);
            _updateButton.Click += async delegate { await BeginUpdateAsync(); };

            versionPanel.Controls.Add(versionTitle);
            versionPanel.Controls.Add(_updateBadge);
            versionPanel.Controls.Add(_versionValue);
            versionPanel.Controls.Add(_updateStatus);
            versionPanel.Controls.Add(_autoUpdate);
            versionPanel.Controls.Add(_refreshButton);
            versionPanel.Controls.Add(_updateButton);

            var announcementPanel = CreatePanel(new Point(20, 278), new Size(520, 250));
            var announcementSection = CreateSectionTitle("公告", new Point(16, 13));
            _announcementTitle = new Label
            {
                Location = new Point(16, 43),
                Size = new Size(486, 28),
                ForeColor = FacmDesignSystem.Text,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 10.5F, FontStyle.Bold)
            };
            _announcementBody = new TextBox
            {
                Location = new Point(16, 77),
                Size = new Size(486, 122),
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FacmDesignSystem.CanvasRaised,
                ForeColor = FacmDesignSystem.Text
            };
            _linkButton = CreateButton("查看详情", new Point(16, 209), 100, FacmButtonTone.Secondary);
            _linkButton.Click += OpenAnnouncementLink;
            announcementPanel.Controls.Add(announcementSection);
            announcementPanel.Controls.Add(_announcementTitle);
            announcementPanel.Controls.Add(_announcementBody);
            announcementPanel.Controls.Add(_linkButton);

            _progress = new ProgressBar
            {
                Location = new Point(20, 542),
                Size = new Size(520, 14),
                Minimum = 0,
                Maximum = 100,
                Visible = false
            };
            _closeButton = CreateButton(forceMode ? "退出程序" : "关闭", new Point(420, 570), 120,
                forceMode ? FacmButtonTone.Danger : FacmButtonTone.Secondary);
            _closeButton.Click += delegate
            {
                if (_updateStarted) return;
                if (_forceMode)
                {
                    _closing = true;
                    Close();
                    _owner.ExitApplication();
                }
                else
                {
                    Close();
                }
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
                "检测到新版本，现在下载并安装吗？",
                "FACM 更新",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Yes) await BeginUpdateAsync();
        }

        private async Task RefreshAsync()
        {
            if (IsDisposed || Disposing || _closing) return;
            SetBusy(true, "正在检查更新...");
            try
            {
                if (_cancellation != null) _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
                var snapshot = await OnlineService.FetchSnapshotAsync(_cancellation.Token);
                if (IsDisposed || Disposing || _closing) return;
                _snapshot = snapshot;
                ApplySnapshot();
            }
            catch (OperationCanceledException)
            {
                // Closing the dialog cancels an in-flight refresh. No UI update is required afterwards.
            }
            finally
            {
                if (!IsDisposed && !Disposing && !_closing) SetBusy(false, null);
            }
        }

        private async Task BeginUpdateAsync()
        {
            if (_updateStarted || _snapshot == null || !_snapshot.UpdateAvailable || _snapshot.Update == null) return;

            _updateStarted = true;
            SetBusy(true, "正在下载更新...");
            _progress.Visible = true;
            _progress.Value = 0;
            try
            {
                if (_cancellation != null) _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
                var progress = new Progress<int>(value =>
                {
                    if (IsDisposed || Disposing || _closing) return;
                    _progress.Value = Math.Max(_progress.Minimum, Math.Min(_progress.Maximum, value));
                    _updateStatus.Text = "正在下载更新：" + value + "%";
                });
                var downloaded = await UpdateInstaller.DownloadAsync(_snapshot.Update, progress, _cancellation.Token);
                if (IsDisposed || Disposing || _closing) return;
                _updateStatus.Text = "下载完成，正在安装...";
                UpdateInstaller.StartReplacement(downloaded);

                // From this point the replacement script is waiting for FACM to exit. Close the modal
                // update dialog first so its FormClosing guard cannot keep the process alive.
                _updateStarted = false;
                _closing = true;
                Close();
                _owner.ExitApplication();
            }
            catch (OperationCanceledException)
            {
                _updateStarted = false;
                if (!IsDisposed && !Disposing && !_closing)
                    _updateStatus.Text = "更新已取消。";
            }
            catch (Exception exception)
            {
                _updateStarted = false;
                AppLog.Error("Update installation failed", exception);
                if (!IsDisposed && !Disposing && !_closing)
                {
                    MessageBox.Show(this, "更新失败，请稍后重试。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ApplySnapshot();
                }
            }
            finally
            {
                if (!_updateStarted && !IsDisposed && !Disposing && !_closing)
                {
                    _progress.Visible = false;
                    SetBusy(false, null);
                }
            }
        }

        private void ApplySnapshot()
        {
            if (IsDisposed || Disposing) return;
            var current = _snapshot.CurrentVersion == null ? "未知" : _snapshot.CurrentVersion.ToString();
            var latest = _snapshot.LatestVersion == null ? "未获取" : _snapshot.LatestVersion.ToString();
            _versionValue.Text = "当前版本：" + current + "    最新版本：" + latest;

            if (!string.IsNullOrWhiteSpace(_snapshot.ErrorMessage))
            {
                _updateStatus.Text = "暂时无法获取更新信息。";
                _updateButton.Enabled = false;
                SetUpdateBadge("获取失败", FacmStatusTone.Error);
            }
            else if (_snapshot.ForceUpdateRequired)
            {
                _updateStatus.Text = "需要更新后才能继续使用。";
                _updateButton.Enabled = true;
                SetUpdateBadge("必须更新", FacmStatusTone.Error);
            }
            else if (_snapshot.UpdateAvailable)
            {
                _updateStatus.Text = string.IsNullOrWhiteSpace(_snapshot.Update.ReleaseNotes)
                    ? "发现新版本。"
                    : _snapshot.Update.ReleaseNotes;
                _updateButton.Enabled = true;
                SetUpdateBadge("发现更新", FacmStatusTone.Accent);
            }
            else
            {
                _updateStatus.Text = "当前已是最新版本。";
                _updateButton.Enabled = false;
                SetUpdateBadge("已是最新", FacmStatusTone.Success);
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
                _announcementBody.Text = "暂无公告内容。";
                _linkButton.Enabled = false;
            }
        }

        private void SetUpdateBadge(string text, FacmStatusTone tone)
        {
            _updateBadge.Text = text;
            _updateBadge.Tone = tone;
        }

        private void SetBusy(bool busy, string status)
        {
            if (IsDisposed || Disposing) return;
            _refreshButton.Enabled = !busy;
            _updateButton.Enabled = !busy && _snapshot != null && _snapshot.UpdateAvailable;
            _autoUpdate.Enabled = !busy;
            _closeButton.Enabled = !busy || !_forceMode;
            if (!string.IsNullOrWhiteSpace(status))
            {
                _updateStatus.Text = status;
                SetUpdateBadge("处理中", FacmStatusTone.Accent);
            }
            UseWaitCursor = busy;
        }

        private void OpenAnnouncementLink(object sender, EventArgs e)
        {
            var url = _snapshot == null || _snapshot.Announcement == null ? null : _snapshot.Announcement.LinkUrl;
            if (!IsHttpsUrl(url)) return;
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_updateStarted && !_closing)
            {
                e.Cancel = true;
                return;
            }

            try { if (_cancellation != null) _cancellation.Cancel(); } catch { }

            if (_forceMode && e.CloseReason == CloseReason.UserClosing && !_closing)
            {
                _closing = true;
                _owner.BeginInvoke(new Action(_owner.ExitApplication));
            }
        }

        private static FacmGlassPanel CreatePanel(Point location, Size size)
        {
            return new FacmGlassPanel
            {
                Location = location,
                Size = size,
                Radius = FacmDesignSystem.CardRadius,
                BackColor = FacmDesignSystem.Surface,
                DrawBorder = true
            };
        }

        private static Label CreateSectionTitle(string text, Point location)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = new Size(360, 25),
                ForeColor = FacmDesignSystem.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font(FacmThemeRuntime.Current.FontName, 9F, FontStyle.Bold)
            };
        }

        private static FacmActionButton CreateButton(string text, Point location, int width, FacmButtonTone tone)
        {
            return new FacmActionButton
            {
                Text = text,
                Location = location,
                Size = new Size(width, 32),
                Tone = tone,
                Font = new Font(FacmThemeRuntime.Current.FontName, 8.8F, FontStyle.Bold)
            };
        }

        private static bool IsHttpsUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _cancellation != null)
            {
                try { _cancellation.Cancel(); } catch { }
                _cancellation.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
