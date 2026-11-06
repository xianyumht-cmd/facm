using System;
using System.Drawing;
using System.Collections.Generic;
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
    // One native shell and one WebView2 owner for all business routes.
    internal sealed partial class UnifiedWorkspaceForm : Form
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
        private bool _starting;
        private bool _ready;
        private bool _closing;
        private bool _browserUnavailable;
        private bool _userMovedWindow;
        private LeagueDashboardPhaseState _lastGameflowState;
        private CancellationTokenSource _championCatalogCancellation;
        private int _championCatalogGeneration;
        private CancellationTokenSource _dashboardReadCancellation;
        private int _dashboardReadGeneration;
        private CancellationTokenSource _presenceCancellation;
        private int _presenceGeneration;
        private bool _presenceBusy;
        private LeaguePresenceSnapshot _presenceSnapshot;
        private LeagueClientSession _presenceSession;
        private CancellationTokenSource _repairCancellation;
        private bool _repairBusy;
        private CancellationTokenSource _liveCancellation;
        private int _liveGeneration;
        private CancellationTokenSource _historyCancellation;
        private int _historyGeneration;
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

            _browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(4, 12, 32)
            };
            Controls.Add(_browser);
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
            Shown += async delegate { ApplyRoundedRegion(); await InitializeAsync(); };
            FormClosing += HandleClosing;
            FormClosed += delegate
            {
                try { if (_browser.CoreWebView2 != null) _browser.CoreWebView2.Stop(); }
                catch (Exception exception) { AppLog.Info("Unified workspace WebView2 shutdown: " + exception.Message); }
                CancelChampionCatalog();
                CancelDashboardRead();
                CancelAdvancedRespawnRead();
                CancelProfileCustomization();
                CancelAdvancedHistoryDetail();
                CancelPresenceOperation();
                CancelRepairOperation();
                CancelLiveRead();
                CancelLiveBenchRead();
                CancelHistoryRead();
                CancelMayhemRead();
                CancelRecommendationRead();
                CancelOnlineRead();
                CancelQuickMessageSend();
                UnhookCloudEvents();
                UnhookLocalStats();
                _browser.Dispose();
            };
        }

        internal bool HasActiveCleanup { get { return _cleanupExecuting; } }

        private async Task InitializeAsync()
        {
            if (_starting || IsDisposed) return;
            _starting = true;
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
                    if (WorkspaceReady != null) WorkspaceReady();
                    SyncWebNavigation();
                    SyncGameflowState();
                    if (string.Equals(_navigation.Current, "appearance", StringComparison.Ordinal))
                    {
                        SyncThemeOptions();
                        SyncUnifiedPetOptions();
                    }
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
                    if (string.Equals(_navigation.Current, LeagueHubNavigation.Dashboard, StringComparison.Ordinal) ||
                        string.Equals(_navigation.Current, "dashboard-advanced", StringComparison.Ordinal))
                        StartDashboardRead();
                    if (_navigation.Current == LeagueHubNavigation.Presence) StartPresenceRead();
                    if (_navigation.Current == "presence-advanced") StartProfileCustomizationRead();
                    if (_navigation.Current == LeagueHubNavigation.Repair) SyncRepairState();
                    if (_navigation.Current == "cleanup") SyncUnifiedCleanupState();
                    if (_navigation.Current == LeagueHubNavigation.Profile) SyncLocalProfile();
                    if (_navigation.Current == "efficiency-advanced") SyncAdvancedTools();
                    if (_navigation.Current == "quick-messages") SyncQuickMessageSettings();
                    if (_navigation.Current == LeagueHubNavigation.Live) StartLiveRead(false);
                    if (_navigation.Current == "live-advanced") StartLiveBenchRead();
                    if (_navigation.Current == LeagueHubNavigation.Player) StartHistoryRead(10, false);
                    if (_navigation.Current == "player-advanced")
                    {
                        OpenAdvancedHistoryData();
                        SendRegionalOptions();
                    }
                    if (_navigation.Current == LeagueHubNavigation.Recommendation) StartRecommendationRead(false);
                    HookCloudEvents();
                    if (_navigation.Current == "cloud-sync") SyncCloudState();
                    if (_navigation.Current == "updates")
                    {
                        if (_onlineSnapshot != null && _onlineReadCancellation == null)
                            SendOnlineState(_onlineSnapshot);
                        else StartOnlineRead();
                    }
                };
                core.NavigateToString(ReadEmbeddedHtml());
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified WebView2 workspace initialization failed", exception);
                if (IsDisposed) return;
                _browserUnavailable = true;
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
                if (_onlineForceRequired) ExitForcedOnline();
                else Close();
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
            if (message == "quick-message-refresh") { SyncQuickMessageSettings(); return; }
            if (message != null && message.StartsWith("quick-message-save:", StringComparison.Ordinal))
            { SaveQuickMessageSettings(message.Substring(19)); return; }
            if (message != null && message.StartsWith("quick-message-send:", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(message.Substring(19), out index)) SendQuickMessage(index);
                return;
            }
            if (message == "advanced-refresh") { SyncAdvancedTools(); return; }
            if (message != null && message.StartsWith("advanced-save:", StringComparison.Ordinal))
            { ApplyAdvancedTools(message.Substring(14)); return; }
            if (message == "profile-refresh") { SyncLocalProfile(); return; }
            if (message == "cleanup-refresh") { SyncUnifiedCleanupState(); return; }
            if (message == "cleanup-select") { SelectUnifiedCleanupDirectory(); return; }
            if (message == "cleanup-detect") { DetectUnifiedCleanupDirectory(); return; }
            if (message == "cleanup-driver") { OpenUnifiedDriverRepair(); return; }
            if (message == "cleanup-preview") { PreviewUnifiedCleanup(); return; }
            if (message == "cleanup-cancel") { ResetCleanupPreview(); SyncUnifiedCleanupState(); return; }
            if (message == "cleanup-elevate") { RestartUnifiedCleanupElevated(); return; }
            if (message != null && message.StartsWith("cleanup-confirm:", StringComparison.Ordinal))
            { ConfirmUnifiedCleanup(message.Substring(16)); return; }
            if (message != null && message.StartsWith("repair-action:", StringComparison.Ordinal))
            {
                ApplyRepairAction(message.Substring(14)); return;
            }
            if (message != null && message.StartsWith("mayhem-query:", StringComparison.Ordinal))
            {
                StartMayhemQuery(message.Substring(13)); return;
            }
            if (message == "mayhem-cancel")
            {
                if (_navigation.Current == LeagueHubNavigation.Mayhem)
                {
                    CancelMayhemRead();
                    PostLeaguePageStatus("mayhem-failed", "canceled");
                }
                return;
            }
            if (message == "cloud-refresh") { SyncCloudState(); return; }
            if (message != null && message.StartsWith("cloud-toggle:", StringComparison.Ordinal))
            { ApplyCloudToggle(message.Substring(13)); return; }
            if (message == "cloud-publish") { PublishCloudGameProfile(); return; }
            if (message == "cloud-keep-local") { ResolveCloudConflict(); return; }
            if (message == "updates-refresh") { StartOnlineRead(); return; }
            if (message != null && message.StartsWith("updates-auto:", StringComparison.Ordinal))
            { ApplyOnlineAutoSetting(message.Substring(13)); return; }
            if (message == "updates-install") { BeginOnlineInstallation(); return; }
            if (message == "updates-stop") { CancelOnlineInstallation(); return; }
            if (message == "updates-exit") { ExitForcedOnline(); return; }
            if (message == "updates-link") { OpenOnlineAnnouncementLink(); return; }
            if (message == "recommendation-refresh")
            {
                if (_navigation.Current == LeagueHubNavigation.Recommendation) StartRecommendationRead(true);
                return;
            }
            if (message != null && message.StartsWith("recommendation-apply:", StringComparison.Ordinal))
            {
                ApplyUnifiedRecommendation(message.Substring(21)); return;
            }
            if (message != null && message.StartsWith("history-advanced-timeline:", StringComparison.Ordinal))
            { ReadAdvancedTimeline(message.Substring(26)); return; }
            if (message != null && message.StartsWith("history-advanced-radar:", StringComparison.Ordinal))
            { ReadAdvancedRadar(message.Substring(23)); return; }
            if (message != null && message.StartsWith("history-advanced-replay:", StringComparison.Ordinal))
            { StartAdvancedReplay(message.Substring(24)); return; }
            if (message == "history-advanced-replay-folder") { ReadAdvancedReplayFolder(); return; }
            if (message == "history-region-options") { SendRegionalOptions(); return; }
            if (message == "history-region-catalog") { RefreshRegionalCatalog(); return; }
            if (message != null && message.StartsWith("history-region-search:", StringComparison.Ordinal))
            { SearchRegionalRiotId(message.Substring(22)); return; }
            if (message != null && message.StartsWith("history-region-detail:", StringComparison.Ordinal))
            { ReadRegionalMatchDetail(message.Substring(22)); return; }
            if (message == "history-advanced-refresh")
            { if (_navigation.Current == "player-advanced") StartHistoryRead(20, true); return; }
            if (message != null && message.StartsWith("history-advanced-detail:", StringComparison.Ordinal))
            { LoadAdvancedHistoryDetail(message.Substring(24)); return; }
            if (message == "history-refresh" || message == "history-more")
            {
                if (_navigation.Current == LeagueHubNavigation.Player)
                    StartHistoryRead(message == "history-more" ? 20 : 10, true);
                return;
            }
            if (message == "bench-refresh") { StartLiveBenchRead(); return; }
            if (message != null && message.StartsWith("bench-swap:", StringComparison.Ordinal))
            { SwapLiveBenchChampion(message.Substring(11)); return; }
            if (message == "live-refresh" || message == "live-sgp")
            {
                if (_navigation.Current == LeagueHubNavigation.Live)
                    StartLiveRead(message == "live-sgp");
                return;
            }
            if (message == "profile-customization-refresh") { StartProfileCustomizationRead(); return; }
            if (message != null && message.StartsWith("profile-customization-champion:", StringComparison.Ordinal))
            { LoadProfileSkins(message.Substring(31)); return; }
            if (message != null && message.StartsWith("profile-customization-skin:", StringComparison.Ordinal))
            { ApplyProfileSkin(message.Substring(27)); return; }
            if (message != null && message.StartsWith("profile-customization-action:", StringComparison.Ordinal))
            { ApplyProfileCustomizationAction(message.Substring(29)); return; }
            if (message == "presence-refresh")
            {
                if (_navigation.Current == LeagueHubNavigation.Presence) StartPresenceRead();
                return;
            }
            if (message != null && message.StartsWith("presence-mode:", StringComparison.Ordinal))
            { ApplyPresenceChange("mode", message.Substring(14)); return; }
            if (message != null && message.StartsWith("presence-signature:", StringComparison.Ordinal))
            { ApplyPresenceChange("signature", message.Substring(19)); return; }
            if (message != null && message.StartsWith("presence-rank:", StringComparison.Ordinal))
            { ApplyPresenceChange("rank", message.Substring(14)); return; }
            if (message == "dashboard-respawn-refresh") { StartAdvancedRespawnRead(); return; }
            if (message == "dashboard-refresh")
            {
                if (string.Equals(_navigation.Current, LeagueHubNavigation.Dashboard, StringComparison.Ordinal) ||
                    string.Equals(_navigation.Current, "dashboard-advanced", StringComparison.Ordinal))
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
            if (message == "pet-refresh") { SyncUnifiedPetOptions(); return; }
            if (message != null && message.StartsWith("pet-select:", StringComparison.Ordinal))
            { ApplyUnifiedPetChoice(message.Substring(11)); return; }
            if (message == "pet-disable" || message == "pet-reset")
            { ApplyUnifiedPetAction(message.Substring(4)); return; }
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
            if (route != "cleanup") ResetCleanupPreview();
            if (!string.Equals(route, "auto-selection", StringComparison.Ordinal)) CancelChampionCatalog();
            if (route != LeagueHubNavigation.Dashboard && route != "dashboard-advanced") CancelDashboardRead();
            if (route != "dashboard-advanced") CancelAdvancedRespawnRead();
            if (route != LeagueHubNavigation.Presence) CancelPresenceOperation();
            if (route != "presence-advanced") CancelProfileCustomization();
            if (route != LeagueHubNavigation.Repair) CancelRepairOperation();
            if (route != LeagueHubNavigation.Live) CancelLiveRead();
            if (route != "live-advanced") CancelLiveBenchRead();
            if (route != LeagueHubNavigation.Player && route != "player-advanced")
                CancelHistoryRead();
            if (route != "player-advanced")
            {
                CancelAdvancedHistoryDetail();
                CancelRegionalHistoryRead();
            }
            if (route != LeagueHubNavigation.Mayhem) CancelMayhemRead();
            if (route != LeagueHubNavigation.Recommendation) CancelRecommendationRead();
            if (route != "updates") CancelOnlineRead();
            if (route != "quick-messages") CancelQuickMessageSend();
            if (route == "player-advanced") { OpenAdvancedHistoryPage(pushHistory); return; }
            if (route == "presence-advanced") { OpenProfileCustomizationPage(pushHistory); return; }
            if (route == "dashboard-advanced") { OpenAdvancedDashboardPage(pushHistory); return; }
            if (route == "live-advanced") { OpenLiveBenchPage(pushHistory); return; }
            if (route == "quick-messages") { OpenQuickMessagePage(pushHistory); return; }
            if (route == "efficiency-advanced") { OpenAdvancedTools(pushHistory); return; }
            if (route == LeagueHubNavigation.Profile) { OpenLocalProfilePage(pushHistory); return; }
            if (route == "cloud-sync") { OpenCloudPage(pushHistory); return; }
            if (route == "updates") { OpenUpdatesPage(pushHistory); return; }
            if (route == LeagueHubNavigation.Mayhem) { OpenMayhemPage(pushHistory); return; }
            if (route == LeagueHubNavigation.Recommendation) { OpenRecommendationPage(pushHistory); return; }
            if (route == LeagueHubNavigation.Player) { OpenHistoryPage(pushHistory); return; }
            if (route == LeagueHubNavigation.Live) { OpenLivePage(pushHistory); return; }
            if (route == LeagueHubNavigation.Repair) { OpenRepairPage(pushHistory); return; }
            if (route == LeagueHubNavigation.Presence) { OpenPresencePage(pushHistory); return; }
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
                OpenUnifiedCleanupPage(pushHistory);
                return;
            }
            // The route map above is complete. Unmapped IDs never open a second
            // WinForms business surface inside this single-window controller.
            AppLog.Info("Unified workspace rejected an unmapped route: " + route);
        }

        private void OpenDashboardPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Dashboard);
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
                    (_navigation.Current != LeagueHubNavigation.Dashboard &&
                     _navigation.Current != "dashboard-advanced") ||
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
                    budget = snapshot.BudgetName,
                    gameProcess = snapshot.GameProcessDetected,
                    platformId = snapshot.PlatformId,
                    platformName = snapshot.PlatformName,
                    profileIconId = snapshot.ProfileIconId,
                    activity = snapshot.Activity.ToString(),
                    observedAt = snapshot.UpdatedAtUtc == DateTime.MinValue ? null
                        : snapshot.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    ranked = snapshot.IsRankedQueue
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




        private void OpenHistoryPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Player);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartHistoryRead(10, false);
        }

        private void StartHistoryRead(int count, bool force)
        {
            CancelHistoryRead();
            var module = LeagueHubUiBridge.UnifiedPlayer;
            if (module == null) { PostHistoryError(); return; }
            var source = new CancellationTokenSource();
            _historyCancellation = source;
            // History changes invalidate any previously selectable match identity.
            ResetAdvancedHistorySnapshot();
            LoadHistoryAsync(module, count, force, source, _historyGeneration);
        }

        private async void LoadHistoryAsync(LeaguePlayerModule module, int count, bool force,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedHistorySession;
                var result = await module.ReadUnifiedHistoryAsync(count, force, source.Token);
                if (!HistoryReadIsCurrent(module, session, source, generation))
                {
                    if (HistoryReadIsCurrent(module, null, source, generation, false)) PostHistoryError();
                    return;
                }
                if (result == null || result.Profile == null) { PostHistoryError(); return; }
                _advancedHistorySnapshot = result;
                _advancedHistorySession = session;
                var page = result.Page;
                if (_navigation.Current == "player-advanced") SyncAdvancedHistoryList();
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "history-details",
                    name = result.Profile.AccountName,
                    level = result.Profile.SummonerLevel,
                    count = page == null ? 0 : page.Matches.Count,
                    hasMore = page != null && page.HasMore && count < 20,
                    rows = page == null ? new object[0] : page.Matches.Take(20).Select(match => new
                    {
                        gameId = match.GameId,
                        played = match.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                        champion = match.ChampionName,
                        championId = match.ChampionId,
                        mode = match.GameMode,
                        queueId = match.QueueId,
                        outcome = match.StatisticsAvailable
                            ? (match.Win ? "win" : "loss") : "unknown",
                        kills = match.Kills,
                        deaths = match.Deaths,
                        assists = match.Assists,
                        duration = match.GameDurationSeconds
                    }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified recent history read skipped: " + exception.GetType().Name);
                if (HistoryReadIsCurrent(module, null, source, generation, false)) PostHistoryError();
            }
            finally
            {
                if (ReferenceEquals(_historyCancellation, source)) _historyCancellation = null;
                source.Dispose();
            }
        }

        private bool HistoryReadIsCurrent(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !source.IsCancellationRequested &&
                generation == _historyGeneration &&
                ReferenceEquals(_historyCancellation, source) &&
                (_navigation.Current == LeagueHubNavigation.Player ||
                 _navigation.Current == "player-advanced") &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedHistorySession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession)));
        }

        private void CancelHistoryRead()
        {
            _historyGeneration++;
            var previous = _historyCancellation;
            _historyCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        private void PostHistoryError()
        {
            if (_ready && !IsDisposed && _browser.CoreWebView2 != null)
            {
                _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"history-failed\"}");
                if (_navigation.Current == "player-advanced")
                    _browser.CoreWebView2.PostWebMessageAsJson(
                        "{\"type\":\"history-advanced-list\",\"error\":true,\"rows\":[]}");
            }
        }

        private void OpenLivePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Live);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartLiveRead(false);
        }

        private void StartLiveRead(bool includeSgp)
        {
            CancelLiveRead();
            var module = LeagueHubUiBridge.UnifiedLive;
            if (module == null) { PostLiveError(); return; }
            var source = new CancellationTokenSource();
            _liveCancellation = source;
            LoadLiveAsync(module, includeSgp, source, _liveGeneration);
        }

        private async void LoadLiveAsync(LeagueLiveModule module, bool includeSgp,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedLiveSession;
                var snapshot = await module.ReadUnifiedLiveAsync(includeSgp, source.Token);
                if (!LiveReadIsCurrent(module, session, source, generation))
                {
                    if (LiveReadIsCurrent(module, null, source, generation, false)) PostLiveError();
                    return;
                }
                if (snapshot == null) { PostLiveError(); return; }
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "live-details",
                    connected = snapshot.Connected,
                    phase = snapshot.Phase,
                    queue = snapshot.QueueName,
                    mode = snapshot.GameMode,
                    map = snapshot.MapName,
                    gameId = snapshot.GameId,
                    timer = snapshot.TimerMillisecondsLeft,
                    sgp = snapshot.SgpEnriched,
                    allyBans = snapshot.AllyBans.ToArray(),
                    enemyBans = snapshot.EnemyBans.ToArray(),
                    players = snapshot.Players.Select(player => new
                    {
                        side = player.Side,
                        name = player.AccountName,
                        position = player.Position,
                        champion = player.ChampionId,
                        local = player.IsLocalPlayer
                    }).Take(24).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified live read skipped: " + exception.GetType().Name);
                if (LiveReadIsCurrent(module, null, source, generation, false)) PostLiveError();
            }
            finally
            {
                if (ReferenceEquals(_liveCancellation, source)) _liveCancellation = null;
                source.Dispose();
            }
        }

        private bool LiveReadIsCurrent(LeagueLiveModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !source.IsCancellationRequested &&
                ReferenceEquals(_liveCancellation, source) && generation == _liveGeneration &&
                _navigation.Current == LeagueHubNavigation.Live &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedLiveSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedLiveSession)));
        }

        private void CancelLiveRead()
        {
            _liveGeneration++;
            var old = _liveCancellation;
            _liveCancellation = null;
            if (old != null && !old.IsCancellationRequested) old.Cancel();
        }

        private void PostLiveError()
        {
            if (_ready && !IsDisposed && _browser.CoreWebView2 != null)
                _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"live-failed\"}");
        }

        private void OpenPresencePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Presence);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartPresenceRead();
        }

        private void StartPresenceRead()
        {
            if (_presenceBusy) return;
            CancelPresenceOperation();
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null) { PostPresenceSnapshot(null); return; }
            var cancellation = new CancellationTokenSource();
            _presenceCancellation = cancellation;
            _presenceBusy = true;
            ReadPresenceAsync(module, cancellation, _presenceGeneration);
        }

        private async void ReadPresenceAsync(LeagueDashboardModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var session = module.UnifiedPresenceSession;
                LeaguePresenceSnapshot snapshot = null;
                if (session != null)
                    snapshot = await module.CreateUnifiedPresenceService().ReadAsync(cancellation.Token);
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                _presenceSession = session;
                _presenceSnapshot = snapshot;
                PostPresenceSnapshot(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence read skipped: " + exception.GetType().Name);
                if (PresenceRequestIsCurrent(module, null, cancellation, generation, false))
                    PostPresenceSnapshot(null);
            }
            finally { FinishPresenceOperation(cancellation); }
        }

        private bool PresenceRequestIsCurrent(LeagueDashboardModule module, LeagueClientSession session,
            CancellationTokenSource cancellation, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !cancellation.IsCancellationRequested &&
                generation == _presenceGeneration &&
                ReferenceEquals(_presenceCancellation, cancellation) &&
                _navigation.Current == LeagueHubNavigation.Presence &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedPresenceSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession)));
        }

        private void ApplyPresenceChange(string kind, string request)
        {
            if (_presenceBusy || _navigation.Current != LeagueHubNavigation.Presence) return;
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null || _presenceSession == null || _presenceSnapshot == null ||
                !_presenceSnapshot.Connected ||
                !LeagueEncounterReplayService.SessionMatches(_presenceSession, module.UnifiedPresenceSession))
            {
                PostPresenceResult("unavailable");
                return;
            }
            LeaguePresenceMode mode = LeaguePresenceMode.Online;
            string signature = null, queue = null, tier = null, division = null;
            try
            {
                if (kind == "mode")
                {
                    if (!new[] { "Online", "Away", "DoNotDisturb", "Mobile", "Offline", "DisplayInGame" }
                        .Contains(request) || !Enum.TryParse(request, out mode))
                    { PostPresenceResult("invalid"); return; }
                }
                else
                {
                    if (string.IsNullOrEmpty(request) || request.Length > 4096)
                    { PostPresenceResult("invalid"); return; }
                    var fields = new JavaScriptSerializer().DeserializeObject(request) as IDictionary<string, object>;
                    if (fields == null) { PostPresenceResult("invalid"); return; }
                    object value;
                    if (kind == "signature")
                    {
                        if (fields.Count != 1 || !fields.TryGetValue("message", out value) ||
                            !(value is string) || ((string)value).Length > LeaguePresenceService.MaximumStatusMessageLength)
                        { PostPresenceResult("invalid"); return; }
                        signature = (string)value;
                    }
                    else if (kind == "rank")
                    {
                        if (fields.Count != 3 || !fields.TryGetValue("queue", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        queue = (string)value;
                        if (!fields.TryGetValue("tier", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        tier = (string)value;
                        if (!fields.TryGetValue("division", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        division = (string)value;
                        string nq, nt, nd;
                        if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(queue, tier, division,
                            out nq, out nt, out nd))
                        { PostPresenceResult("invalid"); return; }
                        queue = nq; tier = nt; division = nd;
                    }
                    else return;
                }
            }
            catch (ArgumentException) { PostPresenceResult("invalid"); return; }
            catch (InvalidOperationException) { PostPresenceResult("invalid"); return; }

            var cancellation = new CancellationTokenSource();
            _presenceCancellation = cancellation;
            _presenceBusy = true;
            WritePresenceAsync(module, kind, mode, signature, queue, tier, division,
                _presenceSnapshot, _presenceSession, cancellation, _presenceGeneration);
        }

        private async void WritePresenceAsync(LeagueDashboardModule module, string kind,
            LeaguePresenceMode mode, string signature, string queue, string tier, string division,
            LeaguePresenceSnapshot expected, LeagueClientSession session,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                var service = module.CreateUnifiedPresenceService();
                string result;
                if (kind == "mode")
                    result = (await service.ApplyIfAvailabilityUnchangedAsync(mode,
                        expected.Availability, cancellation.Token)).Status;
                else if (kind == "signature")
                    result = (await service.ApplyStatusMessageIfUnchangedAsync(signature,
                        expected.StatusMessage, cancellation.Token)).Status;
                else
                    result = (await service.ApplyRankedStatusIfUnchangedAsync(queue, tier, division,
                        expected, cancellation.Token)).Status;
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                var snapshot = await service.ReadAsync(cancellation.Token);
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                _presenceSnapshot = snapshot;
                PostPresenceSnapshot(snapshot);
                PostPresenceResult(result ?? "write-failed");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence write skipped: " + exception.GetType().Name);
                if (PresenceRequestIsCurrent(module, session, cancellation, generation))
                    PostPresenceResult("write-failed");
            }
            finally { FinishPresenceOperation(cancellation); }
        }

        private void FinishPresenceOperation(CancellationTokenSource cancellation)
        {
            if (ReferenceEquals(_presenceCancellation, cancellation))
            {
                _presenceBusy = false;
                _presenceCancellation = null;
            }
            cancellation.Dispose();
        }

        private void CancelPresenceOperation()
        {
            _presenceGeneration++;
            _presenceSession = null;
            _presenceSnapshot = null;
            _presenceBusy = false;
            var cancellation = _presenceCancellation;
            _presenceCancellation = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }

        private void PostPresenceSnapshot(LeaguePresenceSnapshot snapshot)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "presence-state",
                connected = snapshot != null && snapshot.Connected,
                name = snapshot == null ? null : snapshot.DisplayName,
                availability = snapshot == null ? null : snapshot.Availability,
                gameStatus = snapshot == null ? null : snapshot.GameStatus,
                message = snapshot == null ? null : snapshot.StatusMessage,
                queue = snapshot == null ? null : snapshot.RankedLeagueQueue,
                tier = snapshot == null ? null : snapshot.RankedLeagueTier,
                division = snapshot == null ? null : snapshot.RankedLeagueDivision
            }));
        }

        private void PostPresenceResult(string result)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "presence-result", status = result
            }));
        }


        private void OpenRepairPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Repair);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncRepairState();
        }

        private void SyncRepairState()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var service = LeagueHubUiBridge.UnifiedRepair;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-state",
                available = service != null,
                auto = service != null && service.AutoRepairEnabled
            }));
        }

        private void ApplyRepairAction(string action)
        {
            if (_navigation.Current != LeagueHubNavigation.Repair || _repairBusy) return;
            var service = LeagueHubUiBridge.UnifiedRepair;
            if (service == null) { PostRepairResult("unavailable", null); return; }
            if (action == "auto")
            {
                try
                {
                    var updated = service.SetAutoRepairEnabled(!service.AutoRepairEnabled);
                    PostRepairResult(updated != null && updated.Success ? "success" : "failed",
                        updated == null ? null : updated.Message);
                    SyncRepairState();
                }
                catch (Exception exception)
                {
                    AppLog.Info("Unified auto-repair switch failed: " + exception.GetType().Name);
                    PostRepairResult("failed", null);
                }
                return;
            }
            if (action != "window" && action != "settlement" && action != "restart" && action != "exit")
                return;
            var cancellation = new CancellationTokenSource();
            _repairCancellation = cancellation;
            _repairBusy = true;
            RunRepairActionAsync(action, service, cancellation);
        }

        private async void RunRepairActionAsync(string action,
            LeagueGameRepairService service, CancellationTokenSource cancellation)
        {
            try
            {
                if (cancellation.IsCancellationRequested) return;
                if (action == "exit")
                {
                    var efficiency = LeagueHubUiBridge.UnifiedEfficiency;
                    var result = efficiency == null ? null : await efficiency.RunExitGameAsync();
                    if (IsCurrentRepairOperation(cancellation))
                        PostRepairResult(result == null ? "failed" : result.Status, null);
                }
                else
                {
                    LeagueGameRepairResult result;
                    if (action == "window") result = await service.RepairWindowAsync(cancellation.Token);
                    else if (action == "settlement") result = await service.SkipSettlementAsync(cancellation.Token);
                    else result = await service.RestartClientUxAsync(cancellation.Token);
                    if (IsCurrentRepairOperation(cancellation))
                        PostRepairResult(result != null && result.Success ? "success" : "failed",
                            result == null ? null : result.Message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified repair action failed: " + exception.GetType().Name);
                if (IsCurrentRepairOperation(cancellation)) PostRepairResult("failed", null);
            }
            finally
            {
                if (ReferenceEquals(_repairCancellation, cancellation))
                {
                    _repairCancellation = null;
                    _repairBusy = false;
                }
                cancellation.Dispose();
            }
        }

        private bool IsCurrentRepairOperation(CancellationTokenSource cancellation)
        {
            return !IsDisposed && !_closing && !cancellation.IsCancellationRequested &&
                ReferenceEquals(_repairCancellation, cancellation) &&
                _navigation.Current == LeagueHubNavigation.Repair &&
                _ready && _browser.CoreWebView2 != null;
        }

        private void CancelRepairOperation()
        {
            _repairBusy = false;
            var cancellation = _repairCancellation;
            _repairCancellation = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }

        private void PostRepairResult(string status, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-result", status = status, message = message
            }));
        }

        private void OpenPostgameSettings(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("postgame-settings");
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
            if (pushHistory) _navigation.Open("auto-selection");
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
            if (pushHistory) _navigation.Open("matchmaking-settings");
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
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Efficiency);
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
            if (pushHistory) _navigation.Open("client-status");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncGameflowState();
        }

        private void OpenAppearance(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("appearance");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncThemeOptions();
            SyncUnifiedPetOptions();
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

        private void NavigateBack()
        {
            if (_onlineForceRequired) return;
            if (_cleanupExecuting) { PostUnifiedCleanupStatus("deleting"); return; }
            if (_navigation.IsHome) return;
            ResetCleanupPreview();
            CancelChampionCatalog();
            CancelDashboardRead();
            CancelAdvancedRespawnRead();
            CancelProfileCustomization();
            CancelAdvancedHistoryDetail();
            CancelPresenceOperation();
            CancelRepairOperation();
            CancelLiveRead();
            CancelLiveBenchRead();
            CancelHistoryRead();
            CancelMayhemRead();
            CancelRecommendationRead();
            CancelOnlineRead();
            CancelQuickMessageSend();
            _navigation.Back();
            if (_navigation.IsHome) ShowHome();
            else OpenNativeRoute(_navigation.Current, false);
        }

        private void ReturnHome()
        {
            if (_onlineForceRequired) return;
            if (_cleanupExecuting) { PostUnifiedCleanupStatus("deleting"); return; }
            ResetCleanupPreview();
            CancelChampionCatalog();
            CancelDashboardRead();
            CancelAdvancedRespawnRead();
            CancelProfileCustomization();
            CancelAdvancedHistoryDetail();
            CancelPresenceOperation();
            CancelRepairOperation();
            CancelLiveRead();
            CancelLiveBenchRead();
            CancelHistoryRead();
            CancelMayhemRead();
            CancelRecommendationRead();
            CancelOnlineRead();
            CancelQuickMessageSend();
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
            ResetCleanupPreview();
            _closing = true;
            CancelChampionCatalog();
            CancelDashboardRead();
            CancelAdvancedRespawnRead();
            CancelProfileCustomization();
            CancelAdvancedHistoryDetail();
            CancelPresenceOperation();
            CancelRepairOperation();
            CancelLiveRead();
            CancelLiveBenchRead();
            CancelHistoryRead();
            CancelMayhemRead();
            CancelRecommendationRead();
            CancelOnlineRead();
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
            UnifiedWorkspaceAdvancedContract.ValidateForSmokeTest();
            UnifiedWorkspaceQuickMessageContract.ValidateForSmokeTest();
            var html = ReadEmbeddedHtml();
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
