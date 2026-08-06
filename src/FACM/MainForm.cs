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
using FACM.Theming;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private const int BallSize = 62;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly UiTextCatalog _ui = UiTextCatalog.Load();
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
            BackColor = Color.FromArgb(25, 42, 82);
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            ApplyBallRegion();

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
            SizeChanged += delegate { ApplyBallRegion(); };
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

            var theme = ThemeCatalog.Get(_settings.ThemeId);
            var inset = 2.5f - 0.7f * _hoverProgress;
            var bounds = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
            var topColor = Blend(theme.Accent, theme.AccentSecondary, 0.18F + _hoverProgress * 0.18F);
            var bottomColor = Blend(theme.AccentSecondary, theme.BackgroundSecondary, 0.38F - _hoverProgress * 0.12F);

            using (var brush = new LinearGradientBrush(bounds, topColor, bottomColor, 115F))
            {
                e.Graphics.FillEllipse(brush, bounds);
            }
            using (var border = new Pen(Blend(theme.Border, theme.AccentSecondary, _hoverProgress * 0.55F), 1.6f + _hoverProgress * 0.5f))
            {
                e.Graphics.DrawEllipse(border, bounds);
            }
            using (var highlight = new Pen(Color.FromArgb(theme.IsLight ? 120 : 70, Color.White), 1f))
            {
                e.Graphics.DrawArc(highlight, bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8, 205, 125);
            }

            using (var font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            {
                const string text = "F";
                var size = e.Graphics.MeasureString(text, font);
                e.Graphics.DrawString(text, font, textBrush, (Width - size.Width) / 2f - 1f, (Height - size.Height) / 2f - 1f);
            }

            var statusColor = ElevationService.IsAdministrator ? theme.Success : theme.Warning;
            using (var dotBorder = new SolidBrush(Color.FromArgb(235, theme.Background)))
            using (var dot = new SolidBrush(statusColor))
            {
                e.Graphics.FillEllipse(dotBorder, Width - 17, 7, 10, 10);
                e.Graphics.FillEllipse(dot, Width - 15, 9, 6, 6);
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

        public void ApplyThemeSelection()
        {
            CloseMenu();
            Invalidate();
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed || _menu != null) return;
                ToggleMenu();
            }));
        }

        public void RunToolA()
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

        public void RunToolMode(int mode)
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

        public async void OpenUpdateCenter()
        {
            await OpenOnlineCenterAsync();
        }

        public void OpenLogFile()
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
            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false
            };
            menu.Items.Add("打开" + _ui.ControlCenter, null, delegate { Show(); ToggleMenu(); });
            menu.Items.Add(_ui.Cleanup, null, delegate
            {
                Show();
                if (_menu == null) ToggleMenu();
                if (_menu != null && !_menu.IsDisposed) _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
            });
            menu.Items.Add("主题设置", null, delegate
            {
                Show();
                if (_menu == null) ToggleMenu();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.CheckUpdate, null, delegate { OpenUpdateCenter(); });
            menu.Items.Add(_ui.OpenLog, null, delegate { OpenLogFile(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.Exit, null, delegate { ExitApplication(); });
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
                    _tray.ShowBalloonTip(6000, "FACM", "检测到可用的新版本，可点击“" + _ui.CheckUpdate + "”处理。", ToolTipIcon.Info);
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

        private async Task OpenOnlineCenterAsync()
        {
            if (_onlineCenterOpen || IsDisposed) return;
            try
            {
                _tray.ShowBalloonTip(1800, "FACM", "正在读取更新与公告...", ToolTipIcon.Info);
                var snapshot = await OnlineService.FetchSnapshotAsync(CancellationToken.None);
                await ShowOnlineCenterAsync(snapshot, snapshot.ForceUpdateRequired, false);
            }
            catch (Exception exception)
            {
                AppLog.Error("Open update center failed", exception);
                MessageBox.Show("检查更新失败：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void Animate(object sender, EventArgs e)
        {
            var target = _hovered || _menu != null ? 1f : 0f;
            _hoverProgress += (target - _hoverProgress) * 0.22f;
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

            _menu = new CompactMenuForm(this, _settings, _ui);
            _menu.FormClosed += delegate { _menu = null; Invalidate(); };
            PositionMenu(_menu);
            _menu.Show(this);
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            var area = Screen.FromControl(this).WorkingArea;
            var openLeft = Left > area.Left + area.Width / 2;
            var x = openLeft ? Left - menu.Width - 12 : Right + 12;
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

        private void ApplyBallRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(new Rectangle(0, 0, Width, Height));
                Region = new Region(path);
            }
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(first.A + (second.A - first.A) * amount),
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        private static string TrimBalloonText(string value)
        {
            var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 240 ? text : text.Substring(0, 237) + "...";
        }
    }
}
