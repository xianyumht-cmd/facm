using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Online;
using FACM.Services;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private const int BallSize = 68;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly System.Windows.Forms.Timer _animationTimer;
        private readonly NotifyIcon _tray;
        private readonly Icon _appIcon;
        private CompactMenuForm _menu;
        private bool _hovered;
        private bool _dragging;
        private bool _moved;
        private bool _startCleanup;
        private bool _onlineCheckStarted;
        private bool _onlineCenterOpen;
        private Point _dragCursor;
        private Point _dragWindow;
        private float _hoverProgress;
        private float _pulse;

        public MainForm(bool startCleanup = false)
        {
            _startCleanup = startCleanup;
            _appIcon = BrandIcon.Create();
            Text = "FACM";
            Icon = _appIcon;
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(BallSize, BallSize);
            MinimumSize = MaximumSize = Size;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);

            _tray = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "FACM 3.1",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += delegate { Show(); Activate(); ToggleMenu(); };

            _animationTimer = new System.Windows.Forms.Timer { Interval = 25 };
            _animationTimer.Tick += Animate;
            _animationTimer.Start();

            MouseEnter += delegate { _hovered = true; };
            MouseLeave += delegate { _hovered = false; };
            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
            Shown += HandleShown;
            FormClosed += delegate
            {
                _animationTimer.Stop();
                _tray.Visible = false;
                var trayMenu = _tray.ContextMenuStrip;
                _tray.ContextMenuStrip = null;
                _tray.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
                if (_menu != null) _menu.Dispose();
                _appIcon.Dispose();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var inset = 5f - 2.3f * _hoverProgress;
            var bounds = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            using (var shadow = new SolidBrush(Color.FromArgb(75, 0, 0, 0)))
            {
                e.Graphics.FillEllipse(shadow, bounds.X + 2, bounds.Y + 5, bounds.Width, bounds.Height);
            }

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterPoint = new PointF(bounds.X + bounds.Width * 0.35f, bounds.Y + bounds.Height * 0.28f);
                    brush.CenterColor = Color.FromArgb(91, 205, 255);
                    brush.SurroundColors = new[] { Color.FromArgb(45, 79, 219) };
                    e.Graphics.FillPath(brush, path);
                }
            }

            var glowAlpha = 42 + (int)(45 * _hoverProgress) + (int)(9 * Math.Sin(_pulse));
            using (var glow = new Pen(Color.FromArgb(Math.Max(0, Math.Min(110, glowAlpha)), 137, 219, 255), 2.3f))
            {
                e.Graphics.DrawEllipse(glow, bounds);
            }

            using (var inner = new Pen(Color.FromArgb(55, 255, 255, 255), 1f))
            {
                e.Graphics.DrawEllipse(inner, bounds.X + 5, bounds.Y + 5, bounds.Width - 10, bounds.Height - 10);
            }

            using (var font = new Font("Segoe UI", 21F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            {
                const string text = "F";
                var size = e.Graphics.MeasureString(text, font);
                e.Graphics.DrawString(text, font, textBrush, (Width - size.Width) / 2f - 1f, (Height - size.Height) / 2f - 3f);
            }

            using (var versionFont = new Font("Segoe UI", 6.6F, FontStyle.Bold, GraphicsUnit.Point))
            using (var versionBrush = new SolidBrush(Color.FromArgb(210, 235, 255)))
            {
                e.Graphics.DrawString("3.1", versionFont, versionBrush, 26f, 45f);
            }

            using (var dot = new SolidBrush(ElevationService.IsAdministrator
                ? Color.FromArgb(92, 224, 166)
                : Color.FromArgb(255, 191, 89)))
            {
                e.Graphics.FillEllipse(dot, Width - 18, 9, 8, 8);
            }
        }

        public void CloseMenu()
        {
            if (_menu == null) return;
            var menu = _menu;
            _menu = null;
            if (!menu.IsDisposed) menu.Close();
        }

        public void ExitApplication()
        {
            CloseMenu();
            Close();
        }

        private void HandleShown(object sender, EventArgs e)
        {
            RestorePosition();
            if (_startCleanup)
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    if (_menu == null) ToggleMenu();
                    if (_menu != null && !_menu.IsDisposed)
                    {
                        _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
                    }
                    _startCleanup = false;
                }));
                return;
            }

            BeginInvoke(new Action(StartOnlineCheck));
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9F) };
            menu.Items.Add("打开控制中心", null, delegate { Show(); ToggleMenu(); });
            menu.Items.Add("清理环境", null, delegate
            {
                Show();
                if (_menu == null) ToggleMenu();
                if (_menu != null && !_menu.IsDisposed) _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
            });

            var tools = new ToolStripMenuItem("内置工具");
            tools.DropDownItems.Add("运行工具 A", null, delegate { RunStandaloneToolA(); });
            tools.DropDownItems.Add(new ToolStripSeparator());
            for (var mode = 1; mode <= 4; mode++)
            {
                var capturedMode = mode;
                tools.DropDownItems.Add("运行模式 " + capturedMode, null, delegate { RunMode(capturedMode); });
            }
            menu.Items.Add(tools);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("在线中心", null, async delegate { await OpenOnlineCenterAsync(false); });
            menu.Items.Add("检查更新", null, async delegate { await OpenOnlineCenterAsync(true); });
            menu.Items.Add("打开日志", null, delegate { OpenLog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApplication(); });
            return menu;
        }

        private async void StartOnlineCheck()
        {
            if (_onlineCheckStarted || IsDisposed) return;
            _onlineCheckStarted = true;

            try
            {
                await Task.Delay(900);
                if (IsDisposed) return;

                var snapshot = await OnlineService.FetchSnapshotAsync(CancellationToken.None);
                if (IsDisposed || !string.IsNullOrWhiteSpace(snapshot.ErrorMessage)) return;

                var announcement = snapshot.Announcement;
                var newPopupAnnouncement = announcement != null &&
                                           announcement.Enabled &&
                                           announcement.Popup &&
                                           !string.IsNullOrWhiteSpace(announcement.Id) &&
                                           !string.Equals(announcement.Id, _settings.LastAnnouncementId, StringComparison.Ordinal);

                if (snapshot.ForceUpdateRequired)
                {
                    await ShowOnlineCenterAsync(snapshot, true, false);
                    return;
                }

                if (snapshot.UpdateAvailable && _settings.AutoUpdateEnabled)
                {
                    await ShowOnlineCenterAsync(snapshot, false, true);
                    return;
                }

                if (newPopupAnnouncement)
                {
                    _settings.LastAnnouncementId = announcement.Id;
                    _settings.Save();
                    await ShowOnlineCenterAsync(snapshot, false, false);
                    return;
                }

                if (snapshot.UpdateAvailable)
                {
                    _tray.ShowBalloonTip(6000, "FACM", "检测到可用的新版本，可从在线中心手动更新。", ToolTipIcon.Info);
                }
                else if (announcement != null && announcement.Enabled &&
                         !string.IsNullOrWhiteSpace(announcement.Id) &&
                         !string.Equals(announcement.Id, _settings.LastAnnouncementId, StringComparison.Ordinal))
                {
                    _settings.LastAnnouncementId = announcement.Id;
                    _settings.Save();
                    _tray.ShowBalloonTip(
                        6000,
                        string.IsNullOrWhiteSpace(announcement.Title) ? "FACM 公告" : announcement.Title,
                        TrimBalloonText(announcement.Body),
                        ToolTipIcon.Info);
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Startup online check failed", exception);
            }
        }

        private async Task OpenOnlineCenterAsync(bool updateOnly)
        {
            if (_onlineCenterOpen || IsDisposed) return;
            try
            {
                _tray.ShowBalloonTip(2000, "FACM", "正在读取在线配置...", ToolTipIcon.Info);
                var snapshot = await OnlineService.FetchSnapshotAsync(CancellationToken.None);
                await ShowOnlineCenterAsync(snapshot, snapshot.ForceUpdateRequired, updateOnly && snapshot.UpdateAvailable);
            }
            catch (Exception exception)
            {
                AppLog.Error("Open online center failed", exception);
                MessageBox.Show("在线中心打开失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Task ShowOnlineCenterAsync(OnlineSnapshot snapshot, bool forceMode, bool automaticPrompt)
        {
            if (_onlineCenterOpen || IsDisposed) return Task.CompletedTask;
            _onlineCenterOpen = true;
            try
            {
                CloseMenu();
                using (var form = new OnlineCenterForm(this, _settings, snapshot, forceMode))
                {
                    if (automaticPrompt)
                    {
                        form.Shown += async delegate { await form.BeginAutomaticUpdateAsync(); };
                    }
                    form.ShowDialog(this);
                }
            }
            finally
            {
                _onlineCenterOpen = false;
            }
            return Task.CompletedTask;
        }

        private static void RunStandaloneToolA()
        {
            try
            {
                ToolRunner.RunStandaloneToolA();
            }
            catch (Exception exception)
            {
                AppLog.Error("Built-in tool A failed", exception);
                MessageBox.Show("启动内置工具失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RunMode(int mode)
        {
            try
            {
                ToolRunner.RunFixLcu(mode);
            }
            catch (Exception exception)
            {
                AppLog.Error("Built-in mode failed", exception);
                MessageBox.Show("启动内置工具失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Animate(object sender, EventArgs e)
        {
            var target = _hovered || _menu != null ? 1f : 0f;
            _hoverProgress += (target - _hoverProgress) * 0.22f;
            _pulse += 0.08f;
            Invalidate();
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _tray.ContextMenuStrip.Show(Cursor.Position);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragCursor = Cursor.Position;
            _dragWindow = Location;
            Capture = true;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var delta = new Size(Cursor.Position.X - _dragCursor.X, Cursor.Position.Y - _dragCursor.Y);
            if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4) _moved = true;
            Location = _dragWindow + delta;
            CloseMenu();
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            if (_moved) SnapToEdge();
            else ToggleMenu();
        }

        private void ToggleMenu()
        {
            if (_menu != null)
            {
                CloseMenu();
                return;
            }

            _menu = new CompactMenuForm(this, _settings);
            _menu.FormClosed += delegate { _menu = null; Invalidate(); };
            PositionMenu(_menu);
            _menu.Show(this);
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            var area = Screen.FromControl(this).WorkingArea;
            var openLeft = Left > area.Left + area.Width / 2;
            var x = openLeft ? Left - menu.Width - 14 : Right + 14;
            var y = Math.Max(area.Top + 8, Math.Min(Top + Height / 2 - menu.Height / 2, area.Bottom - menu.Height - 8));
            x = Math.Max(area.Left + 8, Math.Min(x, area.Right - menu.Width - 8));
            menu.Location = new Point(x, y);
        }

        private void SnapToEdge()
        {
            var area = Screen.FromControl(this).WorkingArea;
            var x = Left + Width / 2 < area.Left + area.Width / 2 ? area.Left + 8 : area.Right - Width - 8;
            var y = Math.Max(area.Top + 8, Math.Min(Top, area.Bottom - Height - 8));
            Location = new Point(x, y);
            SavePosition();
        }

        private void RestorePosition()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            if (_settings.BallX == int.MinValue || _settings.BallY == int.MinValue)
            {
                Location = new Point(area.Right - Width - 12, area.Top + (area.Height - Height) / 2);
            }
            else
            {
                Location = new Point(
                    Math.Max(area.Left + 4, Math.Min(_settings.BallX, area.Right - Width - 4)),
                    Math.Max(area.Top + 4, Math.Min(_settings.BallY, area.Bottom - Height - 4)));
            }
        }

        private void SavePosition()
        {
            _settings.BallX = Left;
            _settings.BallY = Top;
            _settings.Save();
        }

        private static string TrimBalloonText(string value)
        {
            var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 240 ? text : text.Substring(0, 237) + "...";
        }

        private static void OpenLog()
        {
            try
            {
                var path = AppLog.CurrentLogPath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                MessageBox.Show("无法打开日志：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
