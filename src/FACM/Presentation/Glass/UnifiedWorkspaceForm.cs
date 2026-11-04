using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    // Feature-gated transition host. One native OS window and one WebView2 process owner.
    // Existing native League pages remain embedded until their HTML counterparts are ready.
    internal sealed class UnifiedWorkspaceForm : Form
    {
        private const string HtmlResource = "FACM.Presentation.Glass.GGman-Unified-Workspace.html";
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly UnifiedWorkspaceNavigation _navigation = new UnifiedWorkspaceNavigation();
        private readonly MainForm _ownerBall;
        private readonly UiTextCatalog _ui;
        private readonly AppSettings _settings;
        private readonly CleanupModule _cleanup;
        private readonly WebView2 _browser;
        private readonly Panel _content;
        private LeagueHubForm _hub;
        private CleanupRepairForm _cleanupPage;
        private bool _starting;
        private bool _ready;
        private bool _closing;
        private bool _explicitlyClosingHub;
        private LeagueDashboardPhaseState _lastGameflowState;
        internal event Action BrowserUnavailable;

        internal UnifiedWorkspaceForm(MainForm ownerBall, UiTextCatalog ui,
            AppSettings settings, CleanupModule cleanup)
        {
            _ownerBall = ownerBall ?? throw new ArgumentNullException(nameof(ownerBall));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
            Text = ui.AppName;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = FacmDesignSystem.Canvas;
            ForeColor = FacmDesignSystem.Text;
            Font = new Font(FacmThemeRuntime.Current.FontName, 9F);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = UnifiedWorkspaceNavigation.ResolveClientSize(Screen.PrimaryScreen.WorkingArea, false);

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                Visible = false,
                BackColor = FacmDesignSystem.Canvas,
                Padding = Padding.Empty
            };
            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(4, 12, 32)
            };
            Controls.Add(_content);
            Controls.Add(_browser);
            Resize += delegate { ApplyRoundedRegion(); };
            Shown += async delegate { ApplyRoundedRegion(); await InitializeAsync(); };
            FormClosing += HandleClosing;
            FormClosed += delegate
            {
                try { if (_browser.CoreWebView2 != null) _browser.CoreWebView2.Stop(); }
                catch (Exception exception) { AppLog.Info("Unified workspace WebView2 shutdown: " + exception.Message); }
                if (_hub != null && !_hub.IsDisposed) _hub.Dispose();
                if (_cleanupPage != null && !_cleanupPage.IsDisposed) _cleanupPage.Dispose();
                _browser.Dispose();
            };
        }

        internal bool HasNativeRoute { get { return _hub != null && !_hub.IsDisposed; } }

        private async Task InitializeAsync()
        {
            if (_starting || IsDisposed) return;
            _starting = true;
            try
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM", "UnifiedWorkspaceWebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                if (IsDisposed) return;
                await _browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || _browser.CoreWebView2 == null) return;

                var core = _browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
                {
                    if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                        args.Cancel = true;
                };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args)
                {
                    args.Handled = true;
                };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
                {
                    args.State = CoreWebView2PermissionState.Deny;
                };
                core.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs args)
                {
                    args.Cancel = true;
                };
                core.WebMessageReceived += HandleWebMessage;
                core.NavigationCompleted += delegate
                {
                    if (IsDisposed) return;
                    _ready = true;
                    SyncWebNavigation();
                    SyncGameflowState();
                    if (string.Equals(_navigation.Current, "appearance", StringComparison.Ordinal))
                        SyncThemeOptions();
                    if (string.Equals(_navigation.Current, LeagueHubNavigation.Efficiency, StringComparison.Ordinal))
                        SyncQuickSettings();
                };
                core.NavigateToString(ReadEmbeddedHtml());
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified WebView2 workspace initialization failed", exception);
                if (IsDisposed) return;
                _ready = false;
                if (BrowserUnavailable != null) BrowserUnavailable();
                Close();
            }
        }

        private void HandleWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (IsDisposed || _closing || !_ready) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message == "close")
            {
                Close();
                return;
            }
            if (message == "drag")
            {
                if (WindowState != FormWindowState.Normal) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                return;
            }
            if (message != null && message.StartsWith("toggle:", StringComparison.Ordinal))
            {
                ApplyQuickSetting(message.Substring(7));
                return;
            }
            if (message != null && message.StartsWith("theme:", StringComparison.Ordinal))
            {
                ApplyTheme(message.Substring(6));
                return;
            }
            if (message == "back")
            {
                NavigateBack();
                return;
            }
            if (message == "home")
            {
                ReturnHome();
                return;
            }
            if (message != null && message.StartsWith("open:", StringComparison.Ordinal))
                OpenRoute(message.Substring(5));
        }

        internal void OpenFromNativeRoute(string route)
        {
            if (IsDisposed || _closing || !UnifiedWorkspaceNavigation.IsAllowed(route)) return;
            OpenRoute(route);
        }

        private void OpenRoute(string route)
        {
            OpenNativeRoute(route, true);
        }

        private void OpenNativeRoute(string route, bool pushHistory)
        {
            if (!UnifiedWorkspaceNavigation.IsAllowed(route)) return;
            if (string.Equals(route, "efficiency", StringComparison.Ordinal))
            {
                OpenQuickTools(pushHistory);
                return;
            }
            if (string.Equals(route, "appearance", StringComparison.Ordinal))
            {
                OpenAppearance(pushHistory);
                return;
            }
            if (string.Equals(route, "cleanup", StringComparison.Ordinal))
            {
                OpenCleanupPage(pushHistory);
                return;
            }
            if (_cleanupPage != null && !_cleanupPage.IsDisposed && !TryCloseCurrentHub()) return;
            if (_hub == null || _hub.IsDisposed)
            {
                _hub = LeagueHubUiBridge.CreateUnifiedEmbeddedForm(_ui);
                if (_hub == null) return;
                _hub.SetInitialView(string.Equals(route, "efficiency-advanced", StringComparison.Ordinal)
                    ? LeagueHubNavigation.Efficiency : route);
                _hub.UseUnifiedWorkspaceHost();
                _hub.TopLevel = false;
                _hub.FormBorderStyle = FormBorderStyle.None;
                _hub.Dock = DockStyle.Fill;
                _hub.StartPosition = FormStartPosition.Manual;
                _hub.ShowInTaskbar = false;
                _hub.TopMost = false;
                _hub.MinimumSize = Size.Empty;
                _hub.MaximumSize = Size.Empty;
                _content.Controls.Add(_hub);
                var observedHub = _hub;
                observedHub.FormClosed += delegate
                {
                    if (_explicitlyClosingHub || _closing || IsDisposed) return;
                    if (ReferenceEquals(_hub, observedHub))
                    {
                        _content.Controls.Remove(observedHub);
                        _hub = null;
                        _navigation.Home();
                        ShowHome();
                    }
                };
            }
            else if (!_hub.NavigateInUnifiedWorkspace(
                string.Equals(route, "efficiency-advanced", StringComparison.Ordinal)
                    ? LeagueHubNavigation.Efficiency : route))
            {
                // Unsaved settings editor explicitly declined navigation.
                SyncWebNavigation();
                return;
            }

            if (pushHistory) _navigation.Open(route);
            _browser.Dock = DockStyle.Top;
            _browser.Height = 58;
            _content.Visible = true;
            ResizeForRoute(true);
            _hub.Show();
            SyncWebNavigation();
        }

        private void OpenQuickTools(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Efficiency);
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncQuickSettings();
        }

        private void ApplyQuickSetting(string request)
        {
            if (!string.Equals(_navigation.Current, LeagueHubNavigation.Efficiency, StringComparison.Ordinal)) return;
            var divider = request.LastIndexOf(':');
            if (divider < 0) return;
            var key = request.Substring(0, divider);
            var value = request.Substring(divider + 1);
            if (value != "true" && value != "false") return;
            var enabled = value == "true";
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                if (key == "autoSearch" || key == "autoAccept")
                    module.UpdateMatchmakingSettings(
                        key == "autoSearch" ? enabled : module.AutoMatchmakingEnabled,
                        key == "autoAccept" ? enabled : module.AutoAcceptEnabled,
                        module.AutoMatchmakingMinPartySize, module.AutoMatchmakingStartDelayMs,
                        module.AutoAcceptDelayMs, module.AutoMatchmakingStopPolicy,
                        module.AutoMatchmakingStopAfterMs, module.AutoMatchmakingWaitForInvitees,
                        module.AutoMatchmakingRoomNotices);
                else if (key == "autoReconnect") module.UpdateReconnectSetting(enabled);
                else if (key == "autoHonor" || key == "autoReturn")
                    module.UpdatePostGameSettings(key == "autoHonor" ? enabled : module.AutoHonorEnabled,
                        key == "autoReturn" ? enabled : module.AutoReturnLobbyEnabled,
                        module.AutoHonorStrategy);
                else return;
                SyncQuickSettings();
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified workspace quick setting update failed", exception);
                if (_ready && _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"tool-error\"}");
            }
        }

        private void SyncQuickSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "quick-tools",
                values = new
                {
                    autoSearch = module.AutoMatchmakingEnabled,
                    autoAccept = module.AutoAcceptEnabled,
                    autoReconnect = module.AutoReconnectEnabled,
                    autoHonor = module.AutoHonorEnabled,
                    autoReturn = module.AutoReturnLobbyEnabled
                }
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        private void OpenAppearance(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("appearance");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncThemeOptions();
        }

        private void ApplyTheme(string id)
        {
            if (!string.Equals(_navigation.Current, "appearance", StringComparison.Ordinal)) return;
            var candidate = ThemeCatalog.All.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
            if (candidate == null) return;
            var before = _settings.ThemeId;
            try
            {
                _settings.ThemeId = candidate.Id;
                _settings.Save();
                FacmThemeRuntime.SetCurrent(candidate.Id);
                SyncThemeOptions();
            }
            catch (Exception exception)
            {
                _settings.ThemeId = before;
                AppLog.Error("Unified workspace theme update failed", exception);
                if (_ready && _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"theme-error\"}");
            }
        }

        private void SyncThemeOptions()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "themes",
                selected = _settings.ThemeId,
                items = ThemeCatalog.All.Select(theme => new
                {
                    id = theme.Id, name = theme.Name, description = theme.Description,
                    accent = ColorTranslator.ToHtml(theme.Accent)
                }).ToArray()
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        private void OpenCleanupPage(bool pushHistory)
        {
            if (_hub != null && !_hub.IsDisposed && !TryCloseCurrentHub()) return;
            if (_cleanupPage == null || _cleanupPage.IsDisposed)
            {
                _cleanupPage = new CleanupRepairForm(_ownerBall, _settings, _cleanup);
                _cleanupPage.TopLevel = false;
                _cleanupPage.FormBorderStyle = FormBorderStyle.None;
                _cleanupPage.Dock = DockStyle.Fill;
                _cleanupPage.MinimumSize = Size.Empty;
                _cleanupPage.MaximumSize = Size.Empty;
                _cleanupPage.ShowInTaskbar = false;
                _cleanupPage.TopMost = false;
                _content.Controls.Add(_cleanupPage);
                var page = _cleanupPage;
                page.FormClosed += delegate
                {
                    if (_explicitlyClosingHub || _closing || IsDisposed) return;
                    if (ReferenceEquals(_cleanupPage, page))
                    {
                        _content.Controls.Remove(page);
                        _cleanupPage = null;
                        _navigation.Home();
                        ShowHome();
                    }
                };
            }
            if (pushHistory) _navigation.Open("cleanup");
            _browser.Dock = DockStyle.Top;
            _browser.Height = 58;
            _content.Visible = true;
            ResizeForRoute(true);
            _cleanupPage.Show();
            SyncWebNavigation();
        }

        private void NavigateBack()
        {
            if (_navigation.IsHome || !TryCloseCurrentHub()) return;
            _navigation.Back();
            if (_navigation.IsHome) ShowHome();
            else OpenNativeRoute(_navigation.Current, false);
        }

        private void ReturnHome()
        {
            if (!TryCloseCurrentHub()) return;
            _navigation.Home();
            ShowHome();
        }

        private bool TryCloseCurrentHub()
        {
            if (_cleanupPage != null && !_cleanupPage.IsDisposed)
            {
                _explicitlyClosingHub = true;
                try
                {
                    var cleanup = _cleanupPage;
                    cleanup.Close();
                    if (!cleanup.IsDisposed) return false;
                    _content.Controls.Remove(cleanup);
                    if (ReferenceEquals(_cleanupPage, cleanup)) _cleanupPage = null;
                }
                finally { _explicitlyClosingHub = false; }
            }
            if (_hub == null || _hub.IsDisposed) return true;
            _explicitlyClosingHub = true;
            try
            {
                var closing = _hub;
                closing.Close();
                if (!closing.IsDisposed) return false;
                _content.Controls.Remove(closing);
                if (ReferenceEquals(_hub, closing)) _hub = null;
                return true;
            }
            finally { _explicitlyClosingHub = false; }
        }

        private void ShowHome()
        {
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(false);
            SyncWebNavigation();
        }

        private void ResizeForRoute(bool detail)
        {
            if (IsDisposed) return;
            var area = Screen.FromControl(this).WorkingArea;
            var size = UnifiedWorkspaceNavigation.ResolveClientSize(area, detail,
                Math.Max(1F, DeviceDpi / 96F));
            var original = Location;
            MinimumSize = Size.Empty;
            ClientSize = size;
            Location = new Point(
                Math.Max(area.Left + 2, Math.Min(original.X, area.Right - Width - 2)),
                Math.Max(area.Top + 2, Math.Min(original.Y, area.Bottom - Height - 2)));
        }

        private void ApplyRoundedRegion()
        {
            if (Width < 30 || Height < 30 || IsDisposed) return;
            var radius = Math.Max(8, (int)Math.Round((_navigation.IsHome ? 16 : 12) *
                Math.Max(1F, DeviceDpi / 96F)));
            var arc = Math.Min(radius * 2, Math.Min(Width, Height) / 2);
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, arc, arc, 180, 90);
                path.AddArc(Width - arc - 1, 0, arc, arc, 270, 90);
                path.AddArc(Width - arc - 1, Height - arc - 1, arc, arc, 0, 90);
                path.AddArc(0, Height - arc - 1, arc, arc, 90, 90);
                path.CloseFigure();
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        private void SyncWebNavigation()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsString(
                _navigation.IsHome ? "home" : "detail:" + _navigation.Current);
        }

        internal void UpdateGameflowState(LeagueDashboardPhaseState state)
        {
            if (IsDisposed) return;
            _lastGameflowState = state;
            SyncGameflowState();
        }

        private void SyncGameflowState()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var connected = _lastGameflowState != null && _lastGameflowState.Connected;
            var phase = connected ? (_lastGameflowState.Phase ?? string.Empty) : string.Empty;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "status", connected = connected, phase = phase
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        private void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing) return;
            if (!TryCloseCurrentHub())
            {
                e.Cancel = true;
                return;
            }
            _closing = true;
        }

        internal static string ReadEmbeddedHtml()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(HtmlResource))
            {
                if (stream == null) throw new InvalidOperationException("Unified workspace HTML resource missing.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), true))
                    return reader.ReadToEnd();
            }
        }

        internal static void ValidateForSmokeTest()
        {
            UnifiedWorkspaceNavigation.ValidateForSmokeTest();
            var html = ReadEmbeddedHtml();
            foreach (var view in LeagueHubNavigation.Views)
            {
                if (html.IndexOf("data-route=\"" + view.Id + "\"", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Unified workspace HTML lost a native route.");
            }
            if (!html.Contains("data-route=\"cleanup\"") ||
                !html.Contains("data-setting=\"autoAccept\"") ||
                !html.Contains("data-action=\"back\"") || !html.Contains("data-action=\"home\"") ||
                !html.Contains("data-action=\"close\""))
                throw new InvalidOperationException("Unified workspace HTML lost back/home/close navigation.");
        }
    }
}
