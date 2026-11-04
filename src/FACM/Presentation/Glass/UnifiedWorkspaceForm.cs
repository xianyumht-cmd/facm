using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private CancellationTokenSource _championCatalogCancellation;
        private int _championCatalogGeneration;
        private CancellationTokenSource _dashboardReadCancellation;
        private int _dashboardReadGeneration;
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
                CancelChampionCatalog();
                CancelDashboardRead();
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
                    if (string.Equals(_navigation.Current, "matchmaking-settings", StringComparison.Ordinal))
                        SyncMatchmakingSettings();
                    if (string.Equals(_navigation.Current, "postgame-settings", StringComparison.Ordinal))
                        SyncPostgameSettings();
                    if (string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal))
                    {
                        SyncAutoSelection();
                        StartAutoSelectionCatalog();
                    }
                    if (string.Equals(_navigation.Current, LeagueHubNavigation.Dashboard, StringComparison.Ordinal))
                        StartDashboardRead();
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
            if (message == "dashboard-refresh")
            {
                if (string.Equals(_navigation.Current, LeagueHubNavigation.Dashboard, StringComparison.Ordinal))
                    StartDashboardRead();
                return;
            }
            if (message != null && message.StartsWith("save-postgame:", StringComparison.Ordinal))
            {
                ApplyPostgameSettings(message.Substring(14));
                return;
            }
            if (message != null && message.StartsWith("save-selection:", StringComparison.Ordinal))
            {
                ApplyAutoSelection(message.Substring(15));
                return;
            }
            if (message != null && message.StartsWith("selection-pause:", StringComparison.Ordinal))
            {
                ApplySelectionPause(message.Substring(16));
                return;
            }
            if (message != null && message.StartsWith("save-matchmaking:", StringComparison.Ordinal))
            {
                ApplyMatchmakingSettings(message.Substring(17));
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
            if (!string.Equals(route, "auto-selection", StringComparison.Ordinal)) CancelChampionCatalog();
            if (!string.Equals(route, LeagueHubNavigation.Dashboard, StringComparison.Ordinal)) CancelDashboardRead();
            if (string.Equals(route, LeagueHubNavigation.Dashboard, StringComparison.Ordinal))
            {
                OpenDashboardPage(pushHistory);
                return;
            }
            if (string.Equals(route, "postgame-settings", StringComparison.Ordinal))
            {
                OpenPostgameSettings(pushHistory);
                return;
            }
            if (string.Equals(route, "auto-selection", StringComparison.Ordinal))
            {
                OpenAutoSelection(pushHistory);
                return;
            }
            if (string.Equals(route, "matchmaking-settings", StringComparison.Ordinal))
            {
                OpenMatchmakingSettings(pushHistory);
                return;
            }
            if (string.Equals(route, "efficiency", StringComparison.Ordinal))
            {
                OpenQuickTools(pushHistory);
                return;
            }
            if (string.Equals(route, "client-status", StringComparison.Ordinal))
            {
                OpenClientStatus(pushHistory);
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
                    ? LeagueHubNavigation.Efficiency :
                    string.Equals(route, "dashboard-advanced", StringComparison.Ordinal)
                    ? LeagueHubNavigation.Dashboard : route);
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
                    ? LeagueHubNavigation.Efficiency :
                    string.Equals(route, "dashboard-advanced", StringComparison.Ordinal)
                    ? LeagueHubNavigation.Dashboard : route))
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

        private void OpenDashboardPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Dashboard);
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartDashboardRead();
        }

        private void StartDashboardRead()
        {
            CancelDashboardRead();
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null) return;
            var cancellation = new CancellationTokenSource();
            _dashboardReadCancellation = cancellation;
            var generation = _dashboardReadGeneration;
            LoadDashboardAsync(module, cancellation, generation);
        }

        private async void LoadDashboardAsync(LeagueDashboardModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var snapshot = await module.ReadUnifiedDashboardAsync(cancellation.Token);
                if (IsDisposed || cancellation.IsCancellationRequested ||
                    generation != _dashboardReadGeneration ||
                    !string.Equals(_navigation.Current, LeagueHubNavigation.Dashboard, StringComparison.Ordinal) ||
                    _browser.CoreWebView2 == null) return;
                if (snapshot == null)
                {
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"dashboard-failed\"}");
                    return;
                }
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "dashboard-details",
                    connected = snapshot.Connected,
                    clientProcess = snapshot.ClientProcessDetected,
                    phase = snapshot.Phase,
                    account = snapshot.AccountName,
                    server = !string.IsNullOrWhiteSpace(snapshot.PlatformName)
                        ? snapshot.PlatformName : snapshot.PlatformId,
                    level = snapshot.SummonerLevel,
                    budget = snapshot.BudgetName
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified League dashboard read skipped: " + exception.GetType().Name);
                if (!IsDisposed && generation == _dashboardReadGeneration &&
                    _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"dashboard-failed\"}");
            }
            finally
            {
                if (ReferenceEquals(_dashboardReadCancellation, cancellation))
                    _dashboardReadCancellation = null;
                cancellation.Dispose();
            }
        }

        private void CancelDashboardRead()
        {
            _dashboardReadGeneration++;
            var previous = _dashboardReadCancellation;
            _dashboardReadCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        private void OpenPostgameSettings(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("postgame-settings");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncPostgameSettings();
        }

        private void SyncPostgameSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var textKeys = new[]
            {
                LeagueEfficiencyUiTextKeys.HonorPreferParty,
                LeagueEfficiencyUiTextKeys.HonorOnlyParty,
                LeagueEfficiencyUiTextKeys.HonorAnyAlly,
                LeagueEfficiencyUiTextKeys.HonorOptOut,
                LeagueEfficiencyUiTextKeys.HonorIncludingOpponent
            };
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "postgame-settings",
                autoHonor = module.AutoHonorEnabled, autoReturn = module.AutoReturnLobbyEnabled,
                strategy = module.AutoHonorStrategy,
                strategies = LeagueHonorStrategy.Choices.Select((value, index) => new
                {
                    value, label = LeagueEfficiencyText.Get(_ui, textKeys[Math.Min(index, textKeys.Length - 1)])
                }).ToArray()
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        private void ApplyPostgameSettings(string json)
        {
            if (!string.Equals(_navigation.Current, "postgame-settings", StringComparison.Ordinal)) return;
            UnifiedPostgameChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParsePostgame(json, out input))
            {
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed));
                return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                module.UpdatePostGameSettings(input.AutoHonor, input.AutoReturn, input.Strategy);
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.PostGameSaved));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified postgame save failed", exception);
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed));
            }
        }

        private void SendPostgameResult(string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            { type = "postgame-result", message }));
        }

        private void OpenAutoSelection(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("auto-selection");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncAutoSelection();
            StartAutoSelectionCatalog();
        }

        private void StartAutoSelectionCatalog()
        {
            CancelChampionCatalog();
            if (!_ready || IsDisposed) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var cancellation = new CancellationTokenSource();
            _championCatalogCancellation = cancellation;
            var generation = _championCatalogGeneration;
            LoadAutoSelectionCatalogAsync(module, cancellation, generation);
        }

        private async void LoadAutoSelectionCatalogAsync(LeagueEfficiencyModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var champions = await module.LoadChampionsAsync(cancellation.Token);
                if (IsDisposed || cancellation.IsCancellationRequested ||
                    generation != _championCatalogGeneration ||
                    !string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal) ||
                    _browser.CoreWebView2 == null) return;
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "auto-selection-catalog",
                    champions = (champions ?? new LeagueProfileChampionOption[0])
                        .Where(item => item != null && item.Id > 0 && item.Id <= 10000)
                        .Select(item => new { id = item.Id, name = item.Name }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified auto-select champion directory unavailable: " + exception.GetType().Name);
            }
            finally
            {
                if (ReferenceEquals(_championCatalogCancellation, cancellation))
                    _championCatalogCancellation = null;
                cancellation.Dispose();
            }
        }

        private void CancelChampionCatalog()
        {
            _championCatalogGeneration++;
            var previous = _championCatalogCancellation;
            _championCatalogCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        private void ApplyAutoSelection(string json)
        {
            if (!string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal)) return;
            UnifiedAutoSelectionChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParseAutoSelection(json, out input))
            {
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionInvalid));
                return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                module.UpdateAutoSelectSettings(input.AutoPick, input.PickId, input.AutoBan, input.BanId);
                SendSelectionResult(true, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionSaved));
            }
            catch (ArgumentException)
            {
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionMissingChampion));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified auto-select settings save failed", exception);
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionFailed));
            }
        }

        private void ApplySelectionPause(string value)
        {
            if (!string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal) ||
                (value != "true" && value != "false")) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try { module.UpdateAutoSelectTemporaryPause(value == "true"); SyncAutoSelection(); }
            catch (Exception exception) { AppLog.Error("Unified auto-select pause failed", exception); }
        }

        private void SendSelectionResult(bool ok, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                { type = "selection-result", ok, message }));
        }

        private void SyncAutoSelection()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "auto-selection",
                autoPick = module.AutoPickEnabled, autoBan = module.AutoBanEnabled,
                pickId = module.AutoPickChampionId, banId = module.AutoBanChampionId,
                paused = module.AutoSelectTemporarilyDisabled,
                champions = new object[0]
            }));
        }

        private void OpenMatchmakingSettings(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("matchmaking-settings");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncMatchmakingSettings();
        }

        private void ApplyMatchmakingSettings(string request)
        {
            if (!string.Equals(_navigation.Current, "matchmaking-settings", StringComparison.Ordinal)) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            UnifiedMatchmakingChange setting;
            if (!UnifiedWorkspaceSettingsContract.TryParseMatchmaking(request, out setting))
            {
                PostMatchmakingResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingInvalid));
                return;
            }
            try
            {
                module.UpdateMatchmakingSettings(setting.AutoSearch, setting.AutoAccept,
                    setting.MinParty, setting.QueueDelayMs, setting.AcceptDelayMs,
                    setting.StopPolicy, setting.StopAfterMs,
                    setting.WaitForInvitees, setting.RoomNotices);
                SyncMatchmakingSettings();
                PostMatchmakingResult(true, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingSaved));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified matchmaking settings save failed", exception);
                PostMatchmakingResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingFailed));
            }
        }

        private void PostMatchmakingResult(bool ok, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                { type = "matchmaking-result", ok, message }));
        }

        private void SyncMatchmakingSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "matchmaking-settings",
                autoSearch = module.AutoMatchmakingEnabled,
                autoAccept = module.AutoAcceptEnabled,
                minParty = module.AutoMatchmakingMinPartySize,
                queueDelayMs = module.AutoMatchmakingStartDelayMs,
                acceptDelayMs = module.AutoAcceptDelayMs,
                stopPolicy = module.AutoMatchmakingStopPolicy,
                stopAfterMs = module.AutoMatchmakingStopAfterMs,
                waitForInvitees = module.AutoMatchmakingWaitForInvitees,
                roomNotices = module.AutoMatchmakingRoomNotices,
                policies = new[]
                {
                    new { value = LeagueMatchmakingStopPolicyCodec.Never, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopNever) },
                    new { value = LeagueMatchmakingStopPolicyCodec.Fixed, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopFixed) },
                    new { value = LeagueMatchmakingStopPolicyCodec.Estimated, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopEstimated) }
                }
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
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

        private void OpenClientStatus(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("client-status");
            _content.Visible = false;
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncGameflowState();
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
                    accent = ColorTranslator.ToHtml(theme.Accent),
                    background = ColorTranslator.ToHtml(theme.Background),
                    surface = ColorTranslator.ToHtml(theme.Surface),
                    surfaceRaised = ColorTranslator.ToHtml(theme.SurfaceSecondary),
                    text = ColorTranslator.ToHtml(theme.TextPrimary),
                    muted = ColorTranslator.ToHtml(theme.TextMuted),
                    border = ColorTranslator.ToHtml(theme.Border),
                    light = theme.IsLight
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
            CancelChampionCatalog();
            CancelDashboardRead();
            _navigation.Back();
            if (_navigation.IsHome) ShowHome();
            else OpenNativeRoute(_navigation.Current, false);
        }

        private void ReturnHome()
        {
            if (!TryCloseCurrentHub()) return;
            CancelChampionCatalog();
            CancelDashboardRead();
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
            var size = UnifiedWorkspaceNavigation.ResolveClientSize(area,
                detail ? _navigation.Current : string.Empty,
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
            CancelChampionCatalog();
            CancelDashboardRead();
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
            UnifiedWorkspaceSettingsContract.ValidateForSmokeTest();
            var html = ReadEmbeddedHtml();
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
