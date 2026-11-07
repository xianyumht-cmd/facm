using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
    // One native shell and one WebView2 owner for all business routes.
    internal sealed partial class UnifiedWorkspaceForm : Form
    {
        private const string HtmlResource = "FACM.Presentation.Glass.GGman-Unified-Workspace.html";
        private const int StartupTimeoutMs = 20000;
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
        private readonly Panel _loadingSurface;
        private readonly System.Windows.Forms.Timer _startupTimer;
        private readonly Stopwatch _startupStopwatch = new Stopwatch();
        private bool _starting;
        private bool _ready;
        private bool _navigationSucceeded;
        private bool _documentReady;
        private bool _embeddedNavigationPending;
        private bool _embeddedNavigationStarted;
        private ulong _embeddedNavigationId;
        private bool _closing;
        private bool _browserUnavailable;
        private bool _userMovedWindow;
        private LeagueDashboardPhaseState _lastGameflowState;
        internal event Action BrowserUnavailable;
        internal event Action WorkspaceReady;

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
            Font = new Font("Microsoft YaHei UI", 9F);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = UnifiedWorkspaceNavigation.ResolveClientSize(Screen.PrimaryScreen.WorkingArea, false);

            var palette = FacmThemeRuntime.Current;
            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = palette.Background
            };
            _loadingSurface = CreateLoadingSurface(ui, palette);
            _startupTimer = new System.Windows.Forms.Timer { Interval = StartupTimeoutMs };
            _startupTimer.Tick += delegate
            {
                FailWebWorkspace("startup timeout; content handshake not received within " +
                    StartupTimeoutMs + "ms", null);
            };
            Controls.Add(_browser);
            Controls.Add(_loadingSurface);
            _loadingSurface.BringToFront();
            Resize += delegate { ApplyRoundedRegion(); };
            DpiChanged += delegate
            {
                if (!IsHandleCreated || IsDisposed || _closing) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (IsDisposed || _closing) return;
                        ResizeForRoute(!_navigation.IsHome);
                        ApplyRoundedRegion();
                    }));
                }
                catch (InvalidOperationException) { }
            };
            Shown += async delegate
            {
                ApplyRoundedRegion();
                _startupStopwatch.Start();
                _startupTimer.Start();
                await InitializeAsync();
            };
            FormClosing += HandleClosing;
            FormClosed += delegate
            {
                _startupTimer.Stop();
                _startupTimer.Dispose();
                try { if (_browser.CoreWebView2 != null) _browser.CoreWebView2.Stop(); }
                catch (Exception exception) { AppLog.Info("Unified workspace WebView2 shutdown: " + exception.Message); }
                UnifiedWorkspaceShardCatalog.CancelOutside(this, null);
                UnhookCloudEvents();
                UnhookLocalStats();
                _browser.Dispose();
            };
        }

        private static Panel CreateLoadingSurface(UiTextCatalog ui, ThemeDefinition palette)
        {
            var surface = new Panel { Dock = DockStyle.Fill, BackColor = palette.Background };
            var title = new Label
            {
                AutoSize = true,
                Text = ui.AppName,
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                ForeColor = palette.TextPrimary,
                BackColor = Color.Transparent
            };
            var status = new Label
            {
                AutoSize = true,
                Text = UiTextRuntime.Text(UiTextKeys.UnifiedWorkspaceOpening),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = palette.TextMuted,
                BackColor = Color.Transparent
            };
            surface.Controls.Add(title);
            surface.Controls.Add(status);
            surface.Resize += delegate
            {
                title.Location = new Point(Math.Max(0, (surface.ClientSize.Width - title.Width) / 2),
                    Math.Max(0, (surface.ClientSize.Height - title.Height) / 2 - 18));
                status.Location = new Point(Math.Max(0, (surface.ClientSize.Width - status.Width) / 2),
                    title.Bottom + 12);
            };
            surface.PerformLayout();
            title.Location = new Point(Math.Max(0, (surface.ClientSize.Width - title.Width) / 2),
                Math.Max(0, (surface.ClientSize.Height - title.Height) / 2 - 18));
            status.Location = new Point(Math.Max(0, (surface.ClientSize.Width - status.Width) / 2), title.Bottom + 12);
            return surface;
        }

        internal bool HasActiveCleanup { get { return _cleanupExecuting; } }

        private async Task InitializeAsync()
        {
            if (_starting || IsDisposed) return;
            _starting = true;
            AppLog.Info("Unified workspace WebView2 initialization started.");
            try
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM", "UnifiedWorkspaceWebView2");
#if UNIFIED_RELEASE_CANDIDATE
                UnifiedWebView2Bundle.PrepareNativeLoader();
                if (!UnifiedWebView2Bundle.IsRuntimeAvailable(null))
                    throw new InvalidOperationException("WebView2 Evergreen Runtime is not installed.");
#endif
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                if (IsDisposed || _browserUnavailable) return;
                await _browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed || _browserUnavailable || _browser.CoreWebView2 == null) return;

                AppLog.Info("Unified workspace WebView2 environment ready; runtime=" +
                    environment.BrowserVersionString);

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
                    if (IsExpectedEmbeddedNavigation(_embeddedNavigationPending, _embeddedNavigationStarted))
                    {
                        _embeddedNavigationStarted = true;
                        _embeddedNavigationId = args.NavigationId;
                        AppLog.Info("Unified workspace embedded HTML navigation started; id=" +
                            args.NavigationId + "; uri=" + DescribeNavigationUri(args.Uri));
                        return;
                    }
                    args.Cancel = true;
                    AppLog.Info("Unified workspace blocked external navigation; id=" +
                        args.NavigationId + "; uri=" + DescribeNavigationUri(args.Uri));
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
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
                {
                    if (IsDisposed || _browserUnavailable) return;
                    AppLog.Info("Unified workspace WebView2 process failure: " + args.ProcessFailedKind);
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                        FailWebWorkspace("browser or renderer process failed", null);
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (IsDisposed || _browserUnavailable) return;
                    if (!IsExpectedNavigationCompletion(_embeddedNavigationStarted,
                        _embeddedNavigationId, args.NavigationId))
                    {
                        if (!args.IsSuccess)
                            AppLog.Info("Unified workspace ignored unrelated navigation completion; id=" +
                                args.NavigationId + "; status=" + args.WebErrorStatus);
                        return;
                    }
                    _embeddedNavigationPending = false;
                    if (!args.IsSuccess)
                    {
                        FailWebWorkspace("navigation failed: " + args.WebErrorStatus, null);
                        return;
                    }
                    _navigationSucceeded = true;
                    CompleteWebWorkspaceReady();
                };
                _embeddedNavigationPending = true;
                core.NavigateToString(ReadEmbeddedHtml());
            }
            catch (Exception exception)
            {
                FailWebWorkspace("initialization failed", exception);
            }
        }

        private void CompleteWebWorkspaceReady()
        {
            if (!HasCompletedStartup(_navigationSucceeded, _documentReady) || _ready || IsDisposed ||
                _browserUnavailable || _closing) return;
            _startupTimer.Stop();
            _startupStopwatch.Stop();
            _ready = true;
            _loadingSurface.Hide();
            AppLog.Info("Unified workspace WebView2 HTML content ready; startup=" +
                _startupStopwatch.ElapsedMilliseconds + "ms.");
            if (WorkspaceReady != null) WorkspaceReady();
            SyncWebNavigation();
            SyncGameflowState();
            SyncThemeOptions();
            HookCloudEvents();
            UnifiedWorkspaceShardCatalog.Ready(this, _navigation.Current);
        }

        private static bool HasCompletedStartup(bool navigationSucceeded, bool documentReady)
        {
            return navigationSucceeded && documentReady;
        }

        private bool CanPublishOnRoute(string route, string relatedRoute = null)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                (_navigation.Current == route ||
                 (relatedRoute != null && _navigation.Current == relatedRoute));
        }

        private static bool IsExpectedEmbeddedNavigation(bool pending, bool alreadyStarted)
        {
            return pending && !alreadyStarted;
        }

        private static bool IsExpectedNavigationCompletion(bool started, ulong expectedId, ulong completedId)
        {
            return started && expectedId == completedId;
        }

        private static string DescribeNavigationUri(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return "<invalid>";
            if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
                return value.Length <= 96 ? value : value.Substring(0, 96);
            if (string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
                return "data:<embedded-content;length=" + value.Length + ">";
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                // Never record a URL's query, fragment or credentials in application logs.
                var safe = uri.Scheme + "://" + uri.Host +
                    (uri.IsDefaultPort ? string.Empty : ":" + uri.Port) + uri.AbsolutePath;
                return safe.Length <= 192 ? safe : safe.Substring(0, 192);
            }
            return uri.Scheme + ":<redacted>";
        }

        private void FailWebWorkspace(string reason, Exception exception)
        {
            if (IsDisposed || _browserUnavailable || _closing) return;
            _startupTimer.Stop();
            _browserUnavailable = true;
            _ready = false;
            if (exception == null)
                AppLog.Info("Unified workspace WebView2 fallback: " + reason);
            else
                AppLog.Error("Unified workspace WebView2 fallback: " + reason, exception);
            if (BrowserUnavailable != null) BrowserUnavailable();
            Close();
        }

        // The orb dismisses the desktop UI without tearing down the browser engine.
        // Returning home cancels page-owned reads; the League service modules keep running.
        internal bool Dismiss()
        {
            if (IsDisposed || _closing || !CanDismiss(_browserUnavailable, _ownerBall.IsExiting,
                _onlineForceRequired, _onlineInstalling, _cleanupExecuting)) return false;
            Hide();
            ReturnHome();
            return true;
        }

        private static bool CanDismiss(bool browserUnavailable, bool exiting, bool forceRequired,
            bool installing, bool cleaning)
        {
            return !browserUnavailable && !exiting && !forceRequired && !installing && !cleaning;
        }

        private void HandleWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (IsDisposed || _closing || _browserUnavailable) return;
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (message == "shell-ready")
            {
                _documentReady = true;
                CompleteWebWorkspaceReady();
                return;
            }
            if (!_ready) return;
            if (message == "close")
            {
                if (_onlineForceRequired) ExitForcedOnline();
                else Dismiss();
                return;
            }
            if (message == "drag")
            {
                if (WindowState != FormWindowState.Normal) return;
                _userMovedWindow = true;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                return;
            }
            if (_onlineForceRequired && message != "updates-refresh" &&
                message != "updates-install" && message != "updates-stop" && message != "updates-exit" &&
                message != "updates-link") return;
            // Required updates own the interaction even if a previous page was still active.
            var activeRoute = _onlineForceRequired ? "updates" : _navigation.Current;
            if (UnifiedWorkspaceShardCatalog.TryHandle(this, activeRoute, message)) return;
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
            if (IsDisposed || _closing || !IsRouteAllowedDuringForcedUpdate(_onlineForceRequired, route) ||
                !UnifiedWorkspaceNavigation.IsAllowed(route)) return;
            OpenRoute(route);
        }

        private void OpenRoute(string route)
        {
            OpenNativeRoute(route, true);
        }

        private void OpenNativeRoute(string route, bool pushHistory)
        {
            if (!UnifiedWorkspaceNavigation.IsAllowed(route)) return;
            if (!IsRouteAllowedDuringForcedUpdate(_onlineForceRequired, route)) return;
            if (_cleanupExecuting) { PostUnifiedCleanupStatus("deleting"); return; }
            UnifiedWorkspaceShardCatalog.CancelOutside(this, route);
            if (UnifiedWorkspaceShardCatalog.TryOpen(this, route, pushHistory)) return;
            // An allowed but unregistered route is a contract failure, never a native dialog.
            AppLog.Info("Unified workspace rejected an unmapped route: " + route);
        }

        private void NavigateBack()
        {
            if (_onlineForceRequired) return;
            if (_cleanupExecuting) { PostUnifiedCleanupStatus("deleting"); return; }
            if (_navigation.IsHome) return;
            UnifiedWorkspaceShardCatalog.CancelOutside(this, null);
            _navigation.Back();
            if (_navigation.IsHome) ShowHome();
            else OpenNativeRoute(_navigation.Current, false);
        }

        private void ReturnHome()
        {
            if (_onlineForceRequired) return;
            if (_cleanupExecuting) { PostUnifiedCleanupStatus("deleting"); return; }
            UnifiedWorkspaceShardCatalog.CancelOutside(this, null);
            _navigation.Home();
            ShowHome();
        }

        private void ShowHome()
        {
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(false);
            SyncWebNavigation();
        }

        private void ResizeForRoute(bool detail)
        {
            if (IsDisposed) return;
            var area = Screen.FromControl(this).WorkingArea;
            var size = UnifiedWorkspaceNavigation.ResolveClientSize(area,
                detail ? _navigation.Current : string.Empty,
                Math.Max(1F, DeviceDpi / 96F));
            var original = Location;
            MinimumSize = Size.Empty;
            ClientSize = size;
            if (!_userMovedWindow && _ownerBall.Visible)
            {
                _ownerBall.PositionUnifiedWorkspace(this);
                return;
            }
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
            if (_onlineInstalling && !_ownerBall.IsExiting && !_browserUnavailable)
            {
                e.Cancel = true;
                return;
            }
            if (_cleanupExecuting)
            {
                e.Cancel = true;
                PostUnifiedCleanupStatus("deleting");
                return;
            }
            if (_closing) return;
            if (_onlineForceRequired && !_browserUnavailable && !_ownerBall.IsExiting)
            {
                e.Cancel = true;
                try { _ownerBall.BeginInvoke(new Action(_ownerBall.ExitApplication)); }
                catch (InvalidOperationException) { }
                return;
            }
            _closing = true;
            UnifiedWorkspaceShardCatalog.CancelOutside(this, null);
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
            UnifiedWorkspaceShardCatalog.ValidateForSmokeTest();
            UnifiedWorkspaceDashboardAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceLiveAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceHistoryAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceMayhemAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceRecommendationAdapter.ValidateForSmokeTest();
            UnifiedWorkspacePresenceAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceProfileAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceHistoryDetailAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceRegionalHistoryAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceHistoryExtrasAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceProfileCustomizationAdapter.ValidateForSmokeTest();
            UnifiedWorkspaceSettingsContract.ValidateForSmokeTest();
            UnifiedWorkspaceAdvancedContract.ValidateForSmokeTest();
            UnifiedWorkspaceQuickMessageContract.ValidateForSmokeTest();
            if (HasCompletedStartup(false, false) || HasCompletedStartup(false, true) ||
                HasCompletedStartup(true, false) || !HasCompletedStartup(true, true))
                throw new InvalidOperationException("WebView2 startup requires both navigation and HTML readiness.");
            if (IsExpectedEmbeddedNavigation(false, false) ||
                IsExpectedEmbeddedNavigation(true, true) ||
                !IsExpectedEmbeddedNavigation(true, false) ||
                IsExpectedNavigationCompletion(false, 7UL, 7UL) ||
                IsExpectedNavigationCompletion(true, 7UL, 8UL) ||
                !IsExpectedNavigationCompletion(true, 7UL, 7UL))
                throw new InvalidOperationException("Only the initiated HTML navigation may complete startup.");
            if (DescribeNavigationUri("https://user:pass@example.org/app?access_token=secret#fragment")
                .Contains("secret") ||
                DescribeNavigationUri("https://user:pass@example.org/app").Contains("pass") ||
                DescribeNavigationUri("data:text/html,<secret>").Contains("secret"))
                throw new InvalidOperationException("Navigation logs must redact embedded content and URL secrets.");
            var html = ReadEmbeddedHtml();
            if (!html.Contains("post(\"shell-ready\")"))
                throw new InvalidOperationException("Unified workspace HTML must acknowledge complete script initialization.");
            if (!html.Contains("requestAnimationFrame(function(){requestAnimationFrame(function(){post(\"shell-ready\");});});"))
                throw new InvalidOperationException("Unified workspace must wait for a render opportunity before hiding its native startup surface.");
            if (!CanDismiss(false, false, false, false, false) ||
                CanDismiss(true, false, false, false, false) ||
                CanDismiss(false, true, false, false, false) ||
                CanDismiss(false, false, true, false, false) ||
                CanDismiss(false, false, false, true, false) ||
                CanDismiss(false, false, false, false, true))
                throw new InvalidOperationException("Workspace reuse must not bypass browser failure, exit, update or cleanup lifetimes.");
            if (!html.Contains("id=\"advancedRegionalResults\"") ||
                !html.Contains("data-action=\"history-region-search\""))
                throw new InvalidOperationException("Regional Riot ID history must be a native-backed HTML feature.");
            if (!html.Contains("id=\"advancedHistoryTimeline\"") ||
                !html.Contains("id=\"advancedHistoryRadar\"") ||
                !html.Contains("data-action=\"history-replay\""))
                throw new InvalidOperationException("History timeline, radar and replay must be real HTML controls.");
            if (!html.Contains("id=\"advancedHistoryPage\"") ||
                !html.Contains("data-action=\"history-advanced-refresh\""))
                throw new InvalidOperationException("Historical match detail must be available inside HTML.");
            if (!html.Contains("id=\"profileCustomizationPage\"") ||
                !html.Contains("data-profile-action=\"crest\""))
                throw new InvalidOperationException("LOL profile customization must be a native-backed HTML page.");
            if (!html.Contains("id=\"advancedDashboardPage\"") ||
                !html.Contains("data-action=\"dashboard-respawn-refresh\""))
                throw new InvalidOperationException("Full dashboard must be an HTML route.");
            if (!html.Contains("id=\"benchPage\"") || !html.Contains("data-action=\"bench-refresh\""))
                throw new InvalidOperationException("Advanced ARAM bench must be HTML-backed.");
            if (!html.Contains("id=\"quickMessagePage\""))
                throw new InvalidOperationException("Unified quick message presets must be HTML-backed.");
            if (!html.Contains("id=\"advancedToolsPage\"") ||
                !html.Contains("data-action=\"advanced-save\""))
                throw new InvalidOperationException("Unified advanced settings route is not HTML-backed.");
            if (!html.Contains("id=\"profilePage\""))
                throw new InvalidOperationException("Unified local profile page is missing.");
            if (!html.Contains("id=\"cleanupPage\"") ||
                !html.Contains("data-action=\"cleanup-preview\"") ||
                !html.Contains("data-action=\"cleanup-confirm\""))
                throw new InvalidOperationException("Unified cleanup route must preserve plan review and confirmation.");
            if (!IsRouteAllowedDuringForcedUpdate(false, "player") ||
                IsRouteAllowedDuringForcedUpdate(true, "player") ||
                !IsRouteAllowedDuringForcedUpdate(true, "updates"))
                throw new InvalidOperationException("Forced online update must lock native route navigation.");
            if (!html.Contains("id=\"updateForcedBanner\"") ||
                !html.Contains("data-action=\"updates-exit\"") ||
                !html.Contains("data-action=\"updates-stop\"") ||
                !html.Contains("data-action=\"updates-link\"") ||
                !html.Contains("id=\"updateConfirm\"") ||
                !html.Contains("data-force-update"))
                throw new InvalidOperationException("Unified online page lacks force, download cancel or announcement controls.");
            if (!html.Contains("Microsoft YaHei UI") ||
                !html.Contains("class=\"status status-home\"") ||
                !html.Contains("class=\"section-caption\"") ||
                !html.Contains("data-mode=\"home\""))
                throw new InvalidOperationException("Unified workbench design token or compact home is missing.");
            foreach (var view in LeagueHubNavigation.Views)
            {
                if (html.IndexOf("data-route=\"" + view.Id + "\"", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Unified workspace HTML lost a native route.");
            }
            foreach (var setting in new[]
            {
                "autoSearch", "autoAccept", "autoReconnect", "autoHonor", "autoReturn"
            })
            {
                if (!html.Contains("data-setting=\"" + setting + "\""))
                    throw new InvalidOperationException("Unified HTML lost a real quick setting.");
            }
            if (!html.Contains("data-route=\"cleanup\"") ||
                !html.Contains("data-route=\"appearance\"") ||
                !html.Contains("data-route=\"client-status\"") ||
                !html.Contains("id=\"statusConnection\"") ||
                !html.Contains("data-route=\"efficiency-advanced\"") ||
                !html.Contains("id=\"saveMatchmaking\"") ||
                !html.Contains("id=\"saveAutoSelection\"") ||
                !html.Contains("id=\"savePostgame\"") ||
                !html.Contains("data-route=\"postgame-settings\"") ||
                !html.Contains("data-route=\"matchmaking-settings\"") ||
                !html.Contains("data-route=\"dashboard-advanced\"") ||
                !html.Contains("data-route=\"auto-selection\"") ||
                !html.Contains("id=\"presencePage\"") ||
                !html.Contains("id=\"repairPage\"") ||
                !html.Contains("id=\"livePage\"") ||
                !html.Contains("id=\"historyPage\"") ||
                !html.Contains("id=\"cloudSyncPage\"") ||
                !html.Contains("id=\"updatesPage\"") ||
                !html.Contains("data-action=\"cloud-refresh\"") ||
                !html.Contains("data-action=\"updates-refresh\"") ||
                !html.Contains("id=\"mayhemPage\"") ||
                !html.Contains("id=\"recommendationPage\"") ||
                !html.Contains("data-action=\"mayhem-query\"") ||
                !html.Contains("data-action=\"recommendation-refresh\"") ||
                !html.Contains("data-action=\"recommendation-apply\"") ||
                !html.Contains("id=\"historyRefresh\"") ||
                !html.Contains("id=\"historyMore\"") ||
                !html.Contains("data-route=\"player-advanced\"") ||
                !html.Contains("id=\"liveRefresh\"") ||
                !html.Contains("id=\"liveSgpRefresh\"") ||
                !html.Contains("data-route=\"live-advanced\"") ||
                !html.Contains("data-repair-action=\"window\"") ||
                !html.Contains("data-repair-action=\"settlement\"") ||
                !html.Contains("data-repair-action=\"restart\"") ||
                !html.Contains("data-repair-action=\"exit\"") ||
                !html.Contains("data-presence-mode=\"Online\"") ||
                !html.Contains("id=\"presenceSaveSignature\"") ||
                !html.Contains("id=\"presenceSaveRank\"") ||
                !html.Contains("data-route=\"presence-advanced\"") ||
                !html.Contains("id=\"refreshDashboard\"") ||
                !html.Contains("data-route=\"dashboard-advanced\"") ||
                !html.Contains("id=\"themeList\"") ||
                !html.Contains("Content-Security-Policy") ||
                !html.Contains("data-action=\"back\"") || !html.Contains("data-action=\"home\"") ||
                !html.Contains("data-action=\"close\""))
                throw new InvalidOperationException("Unified workspace HTML lost back/home/close navigation.");
        }
    }
}
