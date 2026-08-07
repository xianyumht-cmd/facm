using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.Mayhem;
using FACM.Online;
using FACM.Pets;
using FACM.Services;
using FACM.Theming;

namespace FACM
{
    internal sealed class MainForm : Form
    {
        private const int BallSize = 88;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly UiTextCatalog _ui = UiTextCatalog.Load();
        private readonly NotifyIcon _tray;
        private readonly Icon _appIcon;
        private readonly System.Windows.Forms.Timer _ballAnimation;
        private CompactMenuForm _menu;
        private CancellationTokenSource _petEventsCancellation;
        private bool _startCleanup;
        private bool _onlineCheckStarted;
        private bool _onlineCenterOpen;
        private bool _petPickerOpen;
        private bool _themePickerOpen;
        private bool _mayhemOpen;
        private bool _exiting;
        private bool _externalPetActive;
        private bool _hovered;
        private bool _dragging;
        private bool _moved;
        private Point _dragCursor;
        private Point _dragWindow;
        private float _hoverProgress;
        private float _pulse;
        private float _orbit;

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
            _tray.DoubleClick += delegate { ToggleMenu(); };

            _ballAnimation = new System.Windows.Forms.Timer { Interval = 24 };
            _ballAnimation.Tick += AnimateBall;
            _ballAnimation.Start();

            MouseEnter += delegate { _hovered = true; };
            MouseLeave += delegate { _hovered = false; };
            MouseDown += BeginBallDrag;
            MouseMove += ContinueBallDrag;
            MouseUp += EndBallDrag;
            Shown += HandleShown;
            FormClosed += HandleClosed;
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
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            var hoverLift = 2.2f * _hoverProgress;
            var inset = 7f - hoverLift;
            var sphere = new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);

            using (var shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(sphere.X + 3, sphere.Y + 7, sphere.Width - 1, sphere.Height - 1);
                using (var shadow = new PathGradientBrush(shadowPath))
                {
                    shadow.CenterColor = Color.FromArgb(88, 0, 0, 0);
                    shadow.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) };
                    e.Graphics.FillPath(shadow, shadowPath);
                }
            }

            using (var spherePath = new GraphicsPath())
            {
                spherePath.AddEllipse(sphere);
                using (var body = new PathGradientBrush(spherePath))
                {
                    body.CenterPoint = new PointF(sphere.Left + sphere.Width * 0.31f, sphere.Top + sphere.Height * 0.25f);
                    body.CenterColor = Color.FromArgb(154, 229, 255);
                    body.SurroundColors = new[] { Color.FromArgb(17, 36, 112) };
                    e.Graphics.FillPath(body, spherePath);
                }
            }

            using (var lowerPath = new GraphicsPath())
            {
                lowerPath.AddEllipse(
                    sphere.Left + sphere.Width * 0.13f,
                    sphere.Top + sphere.Height * 0.48f,
                    sphere.Width * 0.74f,
                    sphere.Height * 0.42f);
                using (var lower = new PathGradientBrush(lowerPath))
                {
                    lower.CenterColor = Color.FromArgb(42, 38, 92, 229);
                    lower.SurroundColors = new[] { Color.FromArgb(0, 26, 52, 126) };
                    e.Graphics.FillPath(lower, lowerPath);
                }
            }

            var glow = 52 + (int)(54 * _hoverProgress) + (int)(14 * (0.5 + 0.5 * Math.Sin(_pulse)));
            using (var outer = new Pen(Color.FromArgb(Math.Max(0, Math.Min(140, glow)), 97, 205, 255), 2.6f))
                e.Graphics.DrawEllipse(outer, sphere);
            using (var rim = new Pen(Color.FromArgb(120, 194, 232, 255), 1.1f))
                e.Graphics.DrawEllipse(rim, sphere.X + 4, sphere.Y + 4, sphere.Width - 8, sphere.Height - 8);

            var orbitBounds = new RectangleF(sphere.X + 8, sphere.Y + sphere.Height * 0.35f, sphere.Width - 16, sphere.Height * 0.30f);
            using (var orbitPen = new Pen(Color.FromArgb(110, 136, 218, 255), 1.3f))
            {
                e.Graphics.DrawArc(orbitPen, orbitBounds, _orbit, 118f);
                e.Graphics.DrawArc(orbitPen, orbitBounds, _orbit + 180f, 82f);
            }

            using (var shine = new SolidBrush(Color.FromArgb(178, 255, 255, 255)))
                e.Graphics.FillEllipse(shine, sphere.X + sphere.Width * 0.20f, sphere.Y + sphere.Height * 0.16f, sphere.Width * 0.25f, sphere.Height * 0.13f);
            using (var shineSoft = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                e.Graphics.FillEllipse(shineSoft, sphere.X + sphere.Width * 0.14f, sphere.Y + sphere.Height * 0.11f, sphere.Width * 0.42f, sphere.Height * 0.28f);

            var coreSize = 35f + 2.5f * _hoverProgress;
            var core = new RectangleF((Width - coreSize) / 2f, (Height - coreSize) / 2f, coreSize, coreSize);
            using (var corePath = new GraphicsPath())
            {
                corePath.AddEllipse(core);
                using (var coreBrush = new PathGradientBrush(corePath))
                {
                    coreBrush.CenterColor = Color.FromArgb(245, 243, 251, 255);
                    coreBrush.SurroundColors = new[] { Color.FromArgb(95, 75, 151, 247) };
                    e.Graphics.FillPath(coreBrush, corePath);
                }
            }
            using (var corePen = new Pen(Color.FromArgb(190, 209, 239, 255), 1.2f))
                e.Graphics.DrawEllipse(corePen, core);

            using (var font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.FromArgb(20, 54, 116)))
            {
                const string logo = "F";
                var size = e.Graphics.MeasureString(logo, font);
                e.Graphics.DrawString(logo, font, textBrush, (Width - size.Width) / 2f - 0.5f, (Height - size.Height) / 2f - 2.5f);
            }

            var lightX = sphere.X + sphere.Width * (0.50f + 0.35f * (float)Math.Cos(_orbit * Math.PI / 180D));
            var lightY = sphere.Y + sphere.Height * (0.50f + 0.16f * (float)Math.Sin(_orbit * Math.PI / 180D));
            using (var light = new SolidBrush(Color.FromArgb(210, 210, 248, 255)))
                e.Graphics.FillEllipse(light, lightX - 2.6f, lightY - 2.6f, 5.2f, 5.2f);
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
            if (_exiting) return;
            _exiting = true;
            CloseMenu();
            StopPetEventSubscription();
            Close();
        }

        public void ApplyThemeSelection()
        {
            CloseMenu();
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed || _menu != null) return;
                ToggleMenu();
            }));
        }

        public void OpenPanelThemeSelector()
        {
            if (_themePickerOpen || IsDisposed) return;
            _themePickerOpen = true;
            try
            {
                CloseMenu();
                using (var picker = new ThemePickerForm(_settings.ThemeId))
                {
                    picker.TopMost = true;
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    _settings.ThemeId = picker.SelectedThemeId;
                    _settings.Save();
                }
                ApplyThemeSelection();
            }
            finally
            {
                _themePickerOpen = false;
            }
        }

        public void OpenPetSelector()
        {
            if (_petPickerOpen || IsDisposed) return;
            _petPickerOpen = true;
            try
            {
                CloseMenu();
                ShowBuiltInBall();
                using (var picker = new PetPickerForm(_settings.PetStyleId))
                {
                    picker.TopMost = true;
                    if (picker.ShowDialog() != DialogResult.OK)
                    {
                        ShowBuiltInBall();
                        return;
                    }
                    _settings.PetStyleId = picker.SelectedPetId;
                    _settings.Save();
                    StartPetEventSubscription(
                        string.IsNullOrWhiteSpace(picker.ActivatedPersonaId)
                            ? PetCatalog.Get(_settings.PetStyleId).PersonaId
                            : picker.ActivatedPersonaId);
                    _externalPetActive = true;
                    HideBuiltInBall();
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Pet selector failed", exception);
                ShowBuiltInBall();
                MessageBox.Show("桌宠暂时无法启用，已保留默认悬浮球。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                _petPickerOpen = false;
            }
        }

        public void OpenMayhemLookup()
        {
            if (_mayhemOpen || IsDisposed) return;
            _mayhemOpen = true;
            try
            {
                CloseMenu();
                using (var form = new MayhemLookupForm())
                {
                    form.TopMost = true;
                    form.ShowDialog();
                }
            }
            finally
            {
                _mayhemOpen = false;
            }
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
            RestoreBallPosition();
            ShowBuiltInBall();
            BeginInvoke(new Action(StartPetRestore));

            if (_startCleanup)
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed) return;
                    if (_menu == null) ToggleMenu();
                    if (_menu != null && !_menu.IsDisposed)
                        _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
                    _startCleanup = false;
                }));
                return;
            }

            BeginInvoke(new Action(StartOnlineCheck));
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            StopPetEventSubscription();
            _ballAnimation.Stop();
            _ballAnimation.Dispose();
            _tray.Visible = false;
            var trayMenu = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = null;
            _tray.Dispose();
            if (trayMenu != null) trayMenu.Dispose();
            if (_menu != null) _menu.Dispose();
            _appIcon.Dispose();
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false
            };
            menu.Items.Add("打开" + _ui.ControlCenter, null, delegate { ToggleMenu(); });
            menu.Items.Add(_ui.Cleanup, null, delegate
            {
                if (_menu == null) ToggleMenu();
                if (_menu != null && !_menu.IsDisposed)
                    _menu.BeginInvoke(new Action(_menu.StartEnvironmentCleanup));
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("控制面板主题", null, delegate { OpenPanelThemeSelector(); });
            menu.Items.Add("3D 桌面宠物", null, delegate { OpenPetSelector(); });
            menu.Items.Add("海斗排行榜", null, delegate { OpenMayhemLookup(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.CheckUpdate, null, delegate { OpenUpdateCenter(); });
            menu.Items.Add(_ui.OpenLog, null, delegate { OpenLogFile(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.Exit, null, delegate { ExitApplication(); });
            return menu;
        }

        private async void StartPetRestore()
        {
            try
            {
                var pet = PetCatalog.Get(_settings.PetStyleId);
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
                {
                    var result = await DesktopHomunculusManager.TryRestoreAsync(pet, cancellation.Token);
                    if (IsDisposed) return;
                    if (result.Success)
                    {
                        _externalPetActive = true;
                        StartPetEventSubscription(result.PersonaId);
                        HideBuiltInBall();
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Info("Pet restore unavailable: " + exception.Message);
            }

            if (!IsDisposed)
            {
                _externalPetActive = false;
                ShowBuiltInBall();
            }
        }

        private void StartPetEventSubscription(string personaId)
        {
            StopPetEventSubscription();
            _petEventsCancellation = new CancellationTokenSource();
            var token = _petEventsCancellation.Token;
            Task.Run(async delegate
            {
                await DesktopHomunculusManager.SubscribeClicksAsync(
                    personaId,
                    delegate
                    {
                        if (IsDisposed || _exiting) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (!_exiting && !IsDisposed) ToggleMenu();
                            }));
                        }
                        catch
                        {
                        }
                    },
                    token);
            }, token);
        }

        private void StopPetEventSubscription()
        {
            if (_petEventsCancellation == null) return;
            _petEventsCancellation.Cancel();
            _petEventsCancellation.Dispose();
            _petEventsCancellation = null;
        }

        private void AnimateBall(object sender, EventArgs e)
        {
            if (!Visible || _externalPetActive) return;
            var target = _hovered || _menu != null ? 1f : 0f;
            _hoverProgress += (target - _hoverProgress) * 0.20f;
            _pulse += 0.10f;
            _orbit += 1.8f;
            if (_orbit >= 360f) _orbit -= 360f;
            Invalidate();
        }

        private void BeginBallDrag(object sender, MouseEventArgs e)
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

        private void ContinueBallDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var delta = new Size(Cursor.Position.X - _dragCursor.X, Cursor.Position.Y - _dragCursor.Y);
            if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 4) _moved = true;
            var proposed = _dragWindow + delta;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            proposed.X = Math.Max(area.Left - Width / 3, Math.Min(proposed.X, area.Right - Width * 2 / 3));
            proposed.Y = Math.Max(area.Top, Math.Min(proposed.Y, area.Bottom - Height));
            Location = proposed;
            CloseMenu();
        }

        private void EndBallDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Capture = false;
            if (_moved) SaveBallPosition();
            else ToggleMenu();
        }

        private void RestoreBallPosition()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            if (_settings.BallX == int.MinValue || _settings.BallY == int.MinValue)
            {
                Location = new Point(area.Right - Width - 18, area.Top + (area.Height - Height) / 2);
                return;
            }

            var saved = new Point(_settings.BallX, _settings.BallY);
            var screen = Screen.FromPoint(saved);
            area = screen.WorkingArea;
            Location = new Point(
                Math.Max(area.Left - Width / 3, Math.Min(saved.X, area.Right - Width * 2 / 3)),
                Math.Max(area.Top, Math.Min(saved.Y, area.Bottom - Height)));
        }

        private void SaveBallPosition()
        {
            _settings.BallX = Left;
            _settings.BallY = Top;
            _settings.Save();
        }

        private void ShowBuiltInBall()
        {
            if (IsDisposed || _exiting) return;
            _externalPetActive = false;
            RestoreBallPosition();
            if (!Visible) Show();
            TopMost = true;
            Invalidate();
        }

        private void HideBuiltInBall()
        {
            if (IsDisposed || _exiting) return;
            SaveBallPosition();
            if (Visible) Hide();
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
                        form.Shown += async delegate { await form.BeginAutomaticUpdateAsync(); };
                    form.ShowDialog();
                }
            }
            finally
            {
                _onlineCenterOpen = false;
            }
            return Task.CompletedTask;
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
            _menu.Show();
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            if (Visible && !_externalPetActive)
            {
                var area = Screen.FromControl(this).WorkingArea;
                var openLeft = Left > area.Left + area.Width / 2;
                var x = openLeft ? Left - menu.Width - 14 : Right + 14;
                var y = Top + Height / 2 - menu.Height / 2;
                x = Math.Max(area.Left + 8, Math.Min(x, area.Right - menu.Width - 8));
                y = Math.Max(area.Top + 8, Math.Min(y, area.Bottom - menu.Height - 8));
                menu.Location = new Point(x, y);
                return;
            }

            var cursor = Cursor.Position;
            var cursorArea = Screen.FromPoint(cursor).WorkingArea;
            var cursorX = cursor.X + 18;
            if (cursorX + menu.Width > cursorArea.Right - 8) cursorX = cursor.X - menu.Width - 18;
            var cursorY = cursor.Y - menu.Height / 2;
            cursorX = Math.Max(cursorArea.Left + 8, Math.Min(cursorX, cursorArea.Right - menu.Width - 8));
            cursorY = Math.Max(cursorArea.Top + 8, Math.Min(cursorY, cursorArea.Bottom - menu.Height - 8));
            menu.Location = new Point(cursorX, cursorY);
        }

        private static string TrimBalloonText(string value)
        {
            var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 240 ? text : text.Substring(0, 237) + "...";
        }
    }
}
