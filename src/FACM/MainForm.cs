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
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly UiTextCatalog _ui = UiTextCatalog.Load();
        private readonly NotifyIcon _tray;
        private readonly Icon _appIcon;
        private PetDefinition _pet;
        private Pet3DWindow _petWindow;
        private CompactMenuForm _menu;
        private bool _startCleanup;
        private bool _onlineCheckStarted;
        private bool _onlineCenterOpen;
        private bool _petPickerOpen;
        private bool _themePickerOpen;
        private bool _mayhemOpen;
        private bool _exiting;

        public MainForm(bool startCleanup = false)
        {
            _startCleanup = startCleanup;
            _pet = PetCatalog.Get(_settings.PetStyleId);
            _appIcon = BrandIcon.Create();

            Text = "FACM";
            Icon = _appIcon;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(1, 1);
            MinimumSize = MaximumSize = ClientSize;
            Opacity = 0;
            Location = new Point(-32000, -32000);

            _tray = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "FACM 3.1",
                Visible = true,
                ContextMenuStrip = BuildTrayMenu()
            };
            _tray.DoubleClick += delegate
            {
                EnsurePetWindow();
                ToggleMenu();
            };

            Shown += HandleShown;
            FormClosed += HandleClosed;
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
            if (_petWindow != null)
            {
                var window = _petWindow;
                _petWindow = null;
                window.Close();
                window.Dispose();
            }
            Close();
        }

        public void ApplyThemeSelection()
        {
            // 主题只重建控制面板，不接触 3D 桌宠场景。
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
                using (var picker = new PetPickerForm(_settings.PetStyleId))
                {
                    picker.TopMost = true;
                    if (picker.ShowDialog() != DialogResult.OK) return;
                    _settings.PetStyleId = picker.SelectedPetId;
                    _settings.Save();
                }
                ApplyPetSelection();
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
            Hide();
            EnsurePetWindow();

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
            _tray.Visible = false;
            var trayMenu = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = null;
            _tray.Dispose();
            if (trayMenu != null) trayMenu.Dispose();
            if (_menu != null) _menu.Dispose();
            if (_petWindow != null)
            {
                var window = _petWindow;
                _petWindow = null;
                window.Close();
                window.Dispose();
            }
            _appIcon.Dispose();
        }

        private void EnsurePetWindow()
        {
            if (_petWindow != null) return;

            _petWindow = new Pet3DWindow(_pet);
            RestorePetPosition(_petWindow);
            _petWindow.Clicked += delegate { BeginInvoke(new Action(ToggleMenu)); };
            _petWindow.DragStarted += delegate { BeginInvoke(new Action(CloseMenu)); };
            _petWindow.DragFinished += delegate
            {
                _settings.BallX = (int)Math.Round(_petWindow.Left);
                _settings.BallY = (int)Math.Round(_petWindow.Top);
                _settings.Save();
            };
            _petWindow.ContextMenuRequested += delegate
            {
                var menu = _tray.ContextMenuStrip;
                if (menu != null) menu.Show(Cursor.Position);
            };
            _petWindow.Closed += delegate
            {
                if (!_exiting) _petWindow = null;
            };
            _petWindow.Show();
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false
            };
            menu.Items.Add("打开" + _ui.ControlCenter, null, delegate { EnsurePetWindow(); ToggleMenu(); });
            menu.Items.Add(_ui.Cleanup, null, delegate
            {
                EnsurePetWindow();
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

            EnsurePetWindow();
            _menu = new CompactMenuForm(this, _settings, _ui);
            _menu.FormClosed += delegate { _menu = null; };
            PositionMenu(_menu);
            _menu.Show();
            _menu.Activate();
        }

        private void PositionMenu(Form menu)
        {
            EnsurePetWindow();
            var petBounds = new Rectangle(
                (int)Math.Round(_petWindow.Left),
                (int)Math.Round(_petWindow.Top),
                Math.Max(1, (int)Math.Round(_petWindow.Width)),
                Math.Max(1, (int)Math.Round(_petWindow.Height)));
            var area = Screen.FromRectangle(petBounds).WorkingArea;
            var openLeft = petBounds.Left > area.Left + area.Width / 2;
            var x = openLeft ? petBounds.Left - menu.Width - 12 : petBounds.Right + 12;
            var y = Math.Max(area.Top + 8, Math.Min(petBounds.Top + petBounds.Height / 2 - menu.Height / 2, area.Bottom - menu.Height - 8));
            x = Math.Max(area.Left + 8, Math.Min(x, area.Right - menu.Width - 8));
            menu.Location = new Point(x, y);
        }

        private void RestorePetPosition(Pet3DWindow window)
        {
            var primary = Screen.PrimaryScreen.WorkingArea;
            if (_settings.BallX == int.MinValue || _settings.BallY == int.MinValue)
            {
                window.SetScreenPosition(primary.Right - window.Width - 24, primary.Top + (primary.Height - window.Height) / 2.0);
                return;
            }

            var saved = new Rectangle(
                _settings.BallX,
                _settings.BallY,
                Math.Max(1, (int)Math.Round(window.Width)),
                Math.Max(1, (int)Math.Round(window.Height)));
            foreach (var screen in Screen.AllScreens)
            {
                if (!screen.WorkingArea.IntersectsWith(saved)) continue;
                window.SetScreenPosition(saved.Left, saved.Top);
                return;
            }

            window.SetScreenPosition(primary.Right - window.Width - 24, primary.Top + (primary.Height - window.Height) / 2.0);
        }

        private void ApplyPetSelection()
        {
            _pet = PetCatalog.Get(_settings.PetStyleId);
            EnsurePetWindow();
            _petWindow.SetPet(_pet);
            EnsurePetVisible();
            _settings.BallX = (int)Math.Round(_petWindow.Left);
            _settings.BallY = (int)Math.Round(_petWindow.Top);
            _settings.Save();
        }

        private void EnsurePetVisible()
        {
            var bounds = new Rectangle(
                (int)Math.Round(_petWindow.Left),
                (int)Math.Round(_petWindow.Top),
                Math.Max(1, (int)Math.Round(_petWindow.Width)),
                Math.Max(1, (int)Math.Round(_petWindow.Height)));
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds)) return;
            }

            var area = Screen.PrimaryScreen.WorkingArea;
            _petWindow.SetScreenPosition(area.Right - _petWindow.Width - 24, area.Top + (area.Height - _petWindow.Height) / 2.0);
        }

        private static string TrimBalloonText(string value)
        {
            var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 240 ? text : text.Substring(0, 237) + "...";
        }
    }
}
