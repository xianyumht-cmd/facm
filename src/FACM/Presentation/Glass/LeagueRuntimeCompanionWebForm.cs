using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using FACM.League;
using FACM.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    // The short-lived Champion Select overlay is separate from the desktop workbench.
    // All game data and writes remain owned by LeagueRuntimeCompanionController.
    internal sealed class LeagueRuntimeCompanionWebForm : Form
    {
        private const string HtmlResource = "FACM.Presentation.Glass.GGman-Runtime-Companion.html";
        private const int ExpandedHeight = 442;
        private const int CollapsedHeight = 46;
        private const int StartupTimeoutMs = 20000;

        private readonly LeagueRuntimeCompanionController _controller;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui = UiTextCatalog.Load();
        private readonly WebView2 _web;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly System.Windows.Forms.Timer _startupTimer;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 350000 };
        private bool _starting;
        private bool _ready;
        private bool _documentReady;
        private bool _navigationSucceeded;
        private bool _navigationPending;
        private bool _navigationStarted;
        private ulong _navigationId;
        private bool _unavailable;
        private bool _refreshing;
        private bool _busy;
        private bool _collapsed;
        private bool _dragging;
        private Point _dragStart;
        private Point _windowStart;
        private string _lastIconKey;
        private int _iconGeneration;
        private LeagueRuntimeCompanionApplyPreparation _pendingLoadout;
        private LeagueItemSetPlan _pendingItems;

        internal event Action BrowserUnavailable;

        internal LeagueRuntimeCompanionWebForm(
            LeagueBenchQuickPickService bench,
            ILeagueClientApi leagueClient,
            LeagueBuildAdvisorDataService advisor,
            LeagueBuildApplyService apply,
            LeagueItemSetService items,
            LeaguePlayerDataService player,
            AppSettings settings)
        {
            _controller = new LeagueRuntimeCompanionController(bench, leagueClient, advisor, apply, items, player);
            _settings = settings;
            Text = UiTextRuntime.Text(UiTextKeys.AppName);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = settings == null || settings.LeagueRuntimeCompanionPinned;
            BackColor = Color.FromArgb(6, 17, 37);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(360, ExpandedHeight);
            Opacity = 0d;
            _collapsed = settings != null && settings.LeagueRuntimeCompanionCollapsed;
            if (_collapsed) ClientSize = new Size(ClientSize.Width, CollapsedHeight);

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(6, 17, 37)
            };
            Controls.Add(_web);

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 850 };
            _refreshTimer.Tick += async delegate { await RefreshAsync(); };
            _startupTimer = new System.Windows.Forms.Timer { Interval = StartupTimeoutMs };
            _startupTimer.Tick += delegate { FailBrowser("startup timed out"); };
            _controller.SnapshotChanged += HandleSnapshotChanged;
            Shown += async delegate
            {
                PositionOnScreen();
                _startupTimer.Start();
                await StartBrowserAsync();
            };
            FormClosed += HandleClosed;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private void PositionOnScreen()
        {
            var saved = _settings != null &&
                LeagueRuntimeCompanionWindowState.HasSavedPosition(_settings);
            var screen = saved
                ? Screen.FromPoint(new Point(_settings.LeagueRuntimeCompanionX, _settings.LeagueRuntimeCompanionY))
                : Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            var desired = saved
                ? new Point(_settings.LeagueRuntimeCompanionX, _settings.LeagueRuntimeCompanionY)
                : new Point(area.Right - Width - 18, area.Top + 90);
            Location = Constrain(desired);
        }

        private Point Constrain(Point desired)
        {
            var area = Screen.FromPoint(desired).WorkingArea;
            return new Point(
                Math.Max(area.Left, Math.Min(area.Right - Width, desired.X)),
                Math.Max(area.Top, Math.Min(area.Bottom - Height, desired.Y)));
        }

        private async Task StartBrowserAsync()
        {
            if (_starting || IsDisposed) return;
            _starting = true;
            AppLog.Info("Runtime Companion WebView2 initialization started.");
            try
            {
#if UNIFIED_RELEASE_CANDIDATE
                UnifiedWebView2Bundle.PrepareNativeLoader();
                if (!UnifiedWebView2Bundle.IsRuntimeAvailable(null))
                    throw new InvalidOperationException("WebView2 Evergreen Runtime unavailable.");
#endif
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FACM", "RuntimeCompanionWebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                if (IsDisposed || _unavailable) return;
                await _web.EnsureCoreWebView2Async(environment);
                if (IsDisposed || _unavailable || _web.CoreWebView2 == null) return;

                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
                {
                    if (_navigationPending && !_navigationStarted)
                    {
                        _navigationStarted = true;
                        _navigationId = args.NavigationId;
                        return;
                    }
                    args.Cancel = true;
                    AppLog.Info("Runtime Companion blocked external navigation; id=" + args.NavigationId);
                };
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (_unavailable || IsDisposed || !_navigationStarted || args.NavigationId != _navigationId)
                        return;
                    _navigationPending = false;
                    if (!args.IsSuccess)
                    {
                        FailBrowser("navigation failed: " + args.WebErrorStatus);
                        return;
                    }
                    _navigationSucceeded = true;
                    CompleteReady();
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
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs args)
                {
                    if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                        args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                        FailBrowser("browser process failed: " + args.ProcessFailedKind);
                };
                core.WebMessageReceived += HandleWebMessage;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(HtmlResource))
                {
                    if (stream == null) throw new InvalidOperationException("Embedded companion HTML missing.");
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        _navigationPending = true;
                        core.NavigateToString(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception error)
            {
                AppLog.Error("Runtime Companion WebView2 startup failed", error);
                FailBrowser("startup exception");
            }
        }

        private void CompleteReady()
        {
            if (!_documentReady || !_navigationSucceeded || _ready || _unavailable || IsDisposed) return;
            _startupTimer.Stop();
            _ready = true;
            AppLog.Info("Runtime Companion WebView2 HTML content ready.");
            Post(new { type = "window", collapsed = _collapsed, pinned = TopMost });
            _refreshTimer.Start();
            _ = RefreshAsync();
        }

        private void FailBrowser(string reason)
        {
            if (_unavailable || IsDisposed) return;
            _unavailable = true;
            _startupTimer.Stop();
            _refreshTimer.Stop();
            AppLog.Info("Runtime Companion WebView2 fallback: " + reason);
            var handler = BrowserUnavailable;
            if (handler != null) handler();
            if (!IsDisposed) Close();
        }

        private void HandleSnapshotChanged(object sender, LeagueRuntimeCompanionUpdateEventArgs args)
        {
            if (!_ready || IsDisposed || _unavailable) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(delegate { PublishSnapshot(args.Snapshot); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            PublishSnapshot(args.Snapshot);
        }

        private void PublishSnapshot(LeagueRuntimeCompanionSnapshot snapshot)
        {
            if (!_ready || _unavailable || IsDisposed || snapshot == null) return;
            if (snapshot.HasVisibleChampSelectContext && Opacity < 1d) Opacity = 1d;
            var build = snapshot.Build;
            var recommendation = build == null ? null : build.Recommendation;
            var guide = snapshot.Guide;
            var ids = snapshot.BenchEnabled && snapshot.BenchChampionIds != null
                ? snapshot.BenchChampionIds.Where(id => id > 0).Distinct().Take(24).ToArray()
                : new int[0];
            Post(new
            {
                type = "snapshot",
                connected = snapshot.SessionAvailable,
                championId = snapshot.LocalChampionId,
                name = build == null ? string.Empty : build.ChampionName,
                timer = Math.Max(0, snapshot.TimerMillisecondsLeft / 1000),
                phase = snapshot.TimerPhase,
                bench = snapshot.BenchEnabled,
                choices = ids,
                busy = _busy,
                teams = snapshot.Players == null ? new object[0] : snapshot.Players.Take(12).Select(p => new {
                    side = p.Side, championId = p.ChampionId, name = p.DisplayName,
                    position = p.Position, local = p.IsLocalPlayer
                }).ToArray(),
                build = build == null ? null : new
                {
                    source = build.Source, mode = build.Mode, status = build.Status,
                    tier = recommendation == null ? null : recommendation.Tier,
                    rank = recommendation == null ? 0 : recommendation.Rank,
                    winRate = recommendation == null ? null : recommendation.WinRate,
                    rows = recommendation == null ? new object[0] : recommendation.Rows.Take(30).Select(row => new {
                        category = row.Category, title = row.Recommendation, evidence = row.Evidence
                    }).ToArray()
                },
                recent = snapshot.RecentChampion == null ? null : new {
                    status = snapshot.RecentChampion.Status,
                    games = snapshot.RecentChampion.ChampionGames,
                    wins = snapshot.RecentChampion.Wins,
                    kills = snapshot.RecentChampion.AverageKills,
                    deaths = snapshot.RecentChampion.AverageDeaths,
                    assists = snapshot.RecentChampion.AverageAssists
                },
                guide = guide == null ? null : new {
                    champion = guide.ChampionName,
                    tier = guide.Tier, rank = guide.Rank,
                    winRate = guide.WinRate, pickRate = guide.PickRate,
                    summary = guide.MayhemBalanceSummary ?? guide.BaseBalanceSummary,
                    skills = guide.SkillOrder,
                    starter = guide.StarterItems == null ? new string[0] : guide.StarterItems.Take(5).Select(x => x.Name).ToArray(),
                    boots = guide.BootItems == null ? new string[0] : guide.BootItems.Take(3).Select(x => x.Name).ToArray(),
                    core = guide.CoreItems == null ? new string[0] : guide.CoreItems.Take(8).ToArray(),
                    augments = guide.AugmentRows == null ? new object[0] : guide.AugmentRows.Take(20).Select(x => new {
                        id = x.Id, rank = x.Rank, name = x.Name, rarity = x.RarityKind,
                        winRate = x.WinRate, pickRate = x.PickRate, games = x.Games, description = x.Description
                    }).ToArray()
                },
                guideLoading = snapshot.GuideLoading,
                buildLoading = snapshot.BuildLoading,
                guideError = snapshot.GuideError,
                buildError = snapshot.BuildError
            });
            var key = snapshot.LocalChampionId + "|" + string.Join(",", ids);
            if (!string.Equals(key, _lastIconKey, StringComparison.Ordinal))
            {
                _lastIconKey = key;
                _ = LoadIconsAsync(ids, ++_iconGeneration);
            }
        }

        private async Task LoadIconsAsync(int[] ids, int generation)
        {
            foreach (var id in ids.Take(20))
            {
                if (!Current(generation)) return;
                try
                {
                    var bytes = await _controller.LoadBenchChampionIconAsync(id, _lifetime.Token);
                    if (!Current(generation) || bytes == null || bytes.Length == 0 || bytes.Length > 2097152) continue;
                    using (var input = new MemoryStream(bytes, false))
                    using (var original = Image.FromStream(input))
                    using (var resized = new Bitmap(36, 36))
                    using (var output = new MemoryStream())
                    {
                        using (var graphics = Graphics.FromImage(resized))
                            graphics.DrawImage(original, new Rectangle(0, 0, 36, 36));
                        resized.Save(output, ImageFormat.Png);
                        if (output.Length <= 24576)
                            Post(new { type = "icon", id, value = "data:image/png;base64," +
                                Convert.ToBase64String(output.ToArray()) });
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception e) { AppLog.Info("Runtime Companion icon unavailable: " + e.GetType().Name); }
            }
        }

        private bool Current(int generation)
        {
            return !IsDisposed && !_unavailable && _ready &&
                !_lifetime.IsCancellationRequested && generation == _iconGeneration;
        }

        private async Task RefreshAsync()
        {
            if (!_ready || _refreshing || IsDisposed || _unavailable || _lifetime.IsCancellationRequested) return;
            _refreshing = true;
            try { await _controller.RefreshAsync(_lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { AppLog.Info("Runtime Companion HTML refresh skipped: " + e.GetType().Name); }
            finally { _refreshing = false; }
        }

        private string Copy(string key)
        {
            return LeagueRuntimeCompanionText.Get(_ui, key);
        }

        private void Post(object payload)
        {
            if (!_ready || IsDisposed || _unavailable || _web.CoreWebView2 == null) return;
            try { _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(payload)); }
            catch (InvalidOperationException) { }
        }

        private void HandleWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (IsDisposed || _unavailable) return;
            string command;
            try { command = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; }
            if (command == "shell-ready")
            {
                _documentReady = true;
                CompleteReady();
                return;
            }
            if (!_ready || string.IsNullOrWhiteSpace(command)) return;
            if (command == "close") { Close(); return; }
            if (command == "pin")
            {
                TopMost = !TopMost;
                if (_settings != null) { _settings.LeagueRuntimeCompanionPinned = TopMost; _settings.Save(); }
                Post(new { type = "window", collapsed = _collapsed, pinned = TopMost });
                return;
            }
            if (command == "collapse")
            {
                _collapsed = !_collapsed;
                ClientSize = new Size(ClientSize.Width, _collapsed ? CollapsedHeight : ExpandedHeight);
                Location = Constrain(Location);
                if (_settings != null) { _settings.LeagueRuntimeCompanionCollapsed = _collapsed; _settings.Save(); }
                Post(new { type = "window", collapsed = _collapsed, pinned = TopMost });
                return;
            }
            if (command.StartsWith("drag-start:", StringComparison.Ordinal))
            {
                Point pos;
                if (!TryPoint(command.Substring(11), out pos)) return;
                _dragging = true;
                _dragStart = pos;
                _windowStart = Location;
                return;
            }
            if (command.StartsWith("drag-move:", StringComparison.Ordinal) && _dragging)
            {
                Point pos;
                if (TryPoint(command.Substring(10), out pos))
                    Location = Constrain(new Point(_windowStart.X + pos.X - _dragStart.X,
                        _windowStart.Y + pos.Y - _dragStart.Y));
                return;
            }
            if (command == "drag-end")
            {
                _dragging = false;
                SaveLocation();
                return;
            }
            if (command == "confirm:no")
            {
                _pendingLoadout = null; _pendingItems = null;
                return;
            }
            if (command == "confirm:yes") { _ = ApplyPreparedAsync(); return; }
            if (command == "quit:yes") { _ = QuitAsync(); return; }
            if (command.StartsWith("swap:", StringComparison.Ordinal))
            {
                int id;
                if (int.TryParse(command.Substring(5), out id)) _ = SwapAsync(id);
                return;
            }
            if (command == "apply:runes" || command == "apply:spells" || command == "apply:items")
                _ = PrepareAsync(command.Substring(6));
        }

        private static bool TryPoint(string value, out Point point)
        {
            point = Point.Empty;
            var values = value.Split(',');
            int x, y;
            if (values.Length != 2 || !int.TryParse(values[0], out x) ||
                !int.TryParse(values[1], out y)) return false;
            point = new Point(x, y);
            return true;
        }

        private void SaveLocation()
        {
            if (_settings == null) return;
            _settings.LeagueRuntimeCompanionX = Left;
            _settings.LeagueRuntimeCompanionY = Top;
            _settings.Save();
        }

        private async Task SwapAsync(int championId)
        {
            if (_busy || championId <= 0) return;
            var state = _controller.CurrentSnapshot;
            if (!state.SessionAvailable || !state.BenchEnabled ||
                state.BenchChampionIds == null || !state.BenchChampionIds.Contains(championId)) return;
            _busy = true;
            Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.WebSwapBusy) });
            try
            {
                var result = await _controller.TrySwapAsync(championId, state.SwapRoute, _lifetime.Token);
                Post(new { type = "status", message = result != null && result.Success ?
                    Copy(LeagueRuntimeCompanionUiTextKeys.WebSwapSuccess) : Copy(LeagueRuntimeCompanionUiTextKeys.WebSwapRejected) });
                await RefreshAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                AppLog.Info("Runtime Companion HTML swap failed: " + error.GetType().Name);
                Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.WebSwapFailed) });
            }
            finally { _busy = false; }
        }

        private async Task PrepareAsync(string target)
        {
            if (_busy) return;
            _pendingLoadout = null;
            _pendingItems = null;
            _busy = true;
            try
            {
                if (target == "items")
                {
                    _pendingItems = await _controller.PrepareItemSetAsync(_lifetime.Token);
                    if (_pendingItems == null || !_pendingItems.HasItems)
                    {
                        Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.ItemSetUnavailable) });
                        return;
                    }
                }
                else
                {
                    var kind = target == "runes" ? LeagueRuntimeCompanionApplyTarget.Runes :
                        LeagueRuntimeCompanionApplyTarget.SummonerSpells;
                    _pendingLoadout = await _controller.PrepareApplyAsync(kind, _lifetime.Token);
                    if (_pendingLoadout == null || !_pendingLoadout.IsUsable)
                    {
                        Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.WebApplyMissing) });
                        return;
                    }
                }
                var label = target == "items"
                    ? Copy(LeagueRuntimeCompanionUiTextKeys.WebItemsLabel)
                    : target == "runes"
                        ? Copy(LeagueRuntimeCompanionUiTextKeys.WebRunesLabel)
                        : Copy(LeagueRuntimeCompanionUiTextKeys.WebSpellsLabel);
                Post(new { type = "confirm",
                    title = Copy(LeagueRuntimeCompanionUiTextKeys.WebConfirmTitle),
                    message = string.Format(Copy(LeagueRuntimeCompanionUiTextKeys.WebConfirmFormat), label) });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AppLog.Info("Runtime Companion HTML apply preparation failed: " + e.GetType().Name);
                Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.WebPrepareFailed) });
            }
            finally { _busy = false; }
        }

        private async Task ApplyPreparedAsync()
        {
            if (_busy) return;
            var loadout = _pendingLoadout;
            var items = _pendingItems;
            _pendingLoadout = null;
            _pendingItems = null;
            if (loadout == null && items == null) return;
            _busy = true;
            try
            {
                if (loadout != null)
                {
                    var result = await _controller.ApplyPreparedAsync(loadout, _lifetime.Token);
                    Post(new { type = "status", message = result != null && result.AnyApplied ?
                        Copy(LeagueRuntimeCompanionUiTextKeys.WebApplySuccess) : Copy(LeagueRuntimeCompanionUiTextKeys.WebApplyRejected) });
                }
                else
                {
                    var result = await _controller.ApplyItemSetAsync(items, _lifetime.Token);
                    Post(new { type = "status", message = result != null && result.Succeeded ?
                        Copy(LeagueRuntimeCompanionUiTextKeys.ItemSetSucceeded) : Copy(LeagueRuntimeCompanionUiTextKeys.ItemSetFailed) });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AppLog.Info("Runtime Companion HTML apply failed: " + e.GetType().Name);
                Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.WebApplyFailed) });
            }
            finally { _busy = false; _ = RefreshAsync(); }
        }

        private async Task QuitAsync()
        {
            if (_busy || !_controller.SupportsChampSelectQuit) return;
            _busy = true;
            try
            {
                var result = await _controller.QuitChampSelectAsync(_lifetime.Token);
                Post(new { type = "status", message = result != null && result.Success ?
                    Copy(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectSucceeded) : Copy(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectBlocked) });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AppLog.Info("Runtime Companion HTML quit failed: " + e.GetType().Name);
                Post(new { type = "status", message = Copy(LeagueRuntimeCompanionUiTextKeys.QuitChampSelectFailed) });
            }
            finally { _busy = false; }
        }

        private void HandleClosed(object sender, FormClosedEventArgs args)
        {
            _startupTimer.Stop();
            _refreshTimer.Stop();
            _controller.SnapshotChanged -= HandleSnapshotChanged;
            _startupTimer.Dispose();
            _refreshTimer.Dispose();
            _iconGeneration++;
            try { _lifetime.Cancel(); }
            catch (ObjectDisposedException) { }
            _controller.Dispose();
            _lifetime.Dispose();
            if (_web.CoreWebView2 != null)
            {
                try { _web.CoreWebView2.Stop(); }
                catch (InvalidOperationException) { }
            }
            _web.Dispose();
        }
    }
}
