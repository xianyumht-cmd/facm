using System;
using System.Diagnostics;
using System.Drawing;
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
        private readonly LayeredFloatingBall _layeredBall;
        private CompactMenuForm _menu;
        private bool _startCleanup;
        private bool _onlineCheckStarted;
        private bool _onlineCenterOpen;
        private bool _petPickerOpen;
        private bool _themePickerOpen;
        private bool _mayhemOpen;
        private bool _startupWarmupStarted;
        private bool _exiting;
        private bool _animalPetActive;
        private bool _dragging;
        private bool _moved;
        private Point _dragCursor;
        private Point _dragWindow;

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
            BackColor = Color.Black;
            TransparencyKey = Color.Empty;
            DoubleBuffered = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            Region = null;

            _tray = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "FACM 3.1",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += delegate { ToggleMenu(); };

            _layeredBall = LayeredFloatingBall.Attach(this);

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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Per-pixel alpha content is supplied by LayeredFloatingBall.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Per-pixel alpha content is supplied by LayeredFloatingBall.
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
            try { AnimalPetManager.Stop(); } catch { }
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
                using (var picker = new AnimalPetPickerForm(_settings.PetStyleId))
                {
                    picker.TopMost = true;
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    _settings.PetStyleId = AnimalPetCatalog.Get(picker.SelectedPetId).Id;
                    _settings.AnimalPetEnabled = true;
                    _settings.Save();
                    ActivateAnimalPet();
                }
            }
            catch (Exception exception)
            {
                AppLog.Error("Animal pet selector failed", exception);
                _settings.AnimalPetEnabled = false;
                _settings.Save();
                _animalPetActive = false;
                ShowBuiltInBall();
                MessageBox.Show("桌宠暂时无法启用，已保留默认悬浮入口。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                _petPickerOpen = false;
            }
        }

        public void ResetAnimalPet()
        {
            try
            {
                if (AnimalPetManager.IsActive)
                {
                    AnimalPetManager.ResetToPrimaryScreen();
                    return;
                }

                if (_settings.AnimalPetEnabled)
                {
                    ActivateAnimalPet();
                    AnimalPetManager.ResetToPrimaryScreen();
                    return;
                }

                RestoreBallPosition();
                ShowBuiltInBall();
            }
            catch (Exception exception)
            {
                AppLog.Info("Animal pet reset skipped: " + exception.Message);
                ShowBuiltInBall();
            }
        }

        public void RestoreDefaultBall()
        {
            try { AnimalPetManager.Stop(); } catch { }
            _settings.AnimalPetEnabled = false;
            _settings.Save();
            _animalPetActive = false;
            ShowBuiltInBall();
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

            // The FACM shell is the first visible product surface. Optional heavy payloads warm in the
            // background only after this form has been shown, so VPet/tool extraction cannot make the
            // application look like it failed to launch.
            BeginBackgroundWarmup();

            if (_settings.AnimalPetEnabled)
                BeginInvoke(new Action(ActivateAnimalPet));

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

        private void BeginBackgroundWarmup()
        {
            if (_startupWarmupStarted || IsDisposed || _exiting) return;
            _startupWarmupStarted = true;

            Task.Run(async delegate
            {
                // Give the message loop a short head start so the floating entry can paint before disk/AV
                // work begins. These preparations are opportunistic; each feature still lazily retries.
                await Task.Delay(180).ConfigureAwait(false);

                try
                {
                    ToolBundleLoader.Prepare();
                }
                catch (Exception exception)
                {
                    AppLog.Error("Tool bundle background warmup failed", exception);
                }

                try
                {
                    await PetHostBundleLoader.BeginWarmup().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Error("PetHost background warmup failed", exception);
                }
            });
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            try { AnimalPetManager.Stop(); } catch { }
            if (_layeredBall != null) _layeredBall.Dispose();
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
            menu.Items.Add("桌面宠物", null, delegate { OpenPetSelector(); });
            menu.Items.Add("宠物复位", null, delegate { ResetAnimalPet(); });
            menu.Items.Add("恢复默认悬浮球", null, delegate { RestoreDefaultBall(); });
            menu.Items.Add("海斗排行榜", null, delegate { OpenMayhemLookup(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.CheckUpdate, null, delegate { OpenUpdateCenter(); });
            menu.Items.Add(_ui.OpenLog, null, delegate { OpenLogFile(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_ui.Exit, null, delegate { ExitApplication(); });
            return menu;
        }

        private void ActivateAnimalPet()
        {
            if (IsDisposed || _exiting || !_settings.AnimalPetEnabled) return;
            try
            {
                // Keep the lightweight FACM entry visible until the selected desktop form reports that
                // it is actually ready. This removes the previous no-visible-UI gap while PetHost starts.
                _animalPetActive = false;
                ShowBuiltInBall();

                AnimalPetManager.Activate(
                    _settings.PetStyleId,
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
                        catch { }
                    },
                    delegate
                    {
                        if (IsDisposed || _exiting) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (_tray.ContextMenuStrip != null)
                                    _tray.ContextMenuStrip.Show(Cursor.Position);
                            }));
                        }
                        catch { }
                    },
                    delegate
                    {
                        if (IsDisposed || _exiting) return;
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (IsDisposed || _exiting || !_settings.AnimalPetEnabled) return;
                                _animalPetActive = true;
                                HideBuiltInBall();
                            }));
                        }
                        catch { }
                    });
            }
            catch (Exception exception)
            {
                AppLog.Error("Built-in animal pet activation failed", exception);
                _settings.AnimalPetEnabled = false;
                _settings.Save();
                _animalPetActive = false;
                ShowBuiltInBall();
            }
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
            _animalPetActive = false;
            RestoreBallPosition();
            if (!Visible) Show();
            TopMost = true;
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
            _menu.FormClosed += delegate { _menu = null; };
            PositionMenu(_menu);
            _menu.Show();
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            if (Visible && !_animalPetActive)
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