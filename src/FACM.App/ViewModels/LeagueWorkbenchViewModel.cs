using System.ComponentModel;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.Settings;
using FACM.Core.State;
using FACM.Core.Text;

namespace FACM.App.ViewModels;

public sealed class LeagueWorkbenchViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IProductStateReader _productState;
    private readonly PerformanceBudgetProvider _performance;
    private readonly ILeagueWorkbenchDataSource? _dataSource;
    private readonly Action<LeagueWorkbenchDiagnostic>? _diagnosticReporter;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _advisorUiGate = new(1, 1);
    private readonly SemaphoreSlim _itemSetUiGate = new(1, 1);
    private readonly SemaphoreSlim _automationSettingsGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private LeagueProductState _leagueState;
    private string _budgetName;
    private LeagueWorkbenchDashboardSnapshot _dashboard = LeagueWorkbenchDashboardSnapshot.Unavailable("not-loaded");
    private LeagueWorkbenchPlayerSnapshot _player = LeagueWorkbenchPlayerSnapshot.Unavailable("not-loaded");
    private LeagueWorkbenchLiveSnapshot _live = LeagueWorkbenchLiveSnapshot.Unavailable(string.Empty, "not-loaded");
    private LeagueBuildAdvisorSnapshot _advisor = LeagueBuildAdvisorSnapshot.Unavailable(string.Empty, "not-loaded");
    private LeagueItemSetPlan? _preparedItemSet;
    private string _itemSetStatus = "not-ready";
    private ILeagueBuildAdvisorService? _buildAdvisorService;
    private ILeagueItemSetService? _itemSetService;
    private ISettings2Repository? _automationSettings;
    private ILeagueMatchmakingAutomationService? _matchmakingAutomation;
    private bool _ownsProductServices;
    private bool _isRefreshing;
    private bool _isAdvisorRefreshing;
    private bool _isItemSetBusy;
    private bool _isAutomationSettingsBusy;
    private bool _disposed;

    public LeagueWorkbenchViewModel(
        IProductStateReader productState,
        PerformanceBudgetProvider performance,
        ILeagueWorkbenchDataSource? dataSource = null,
        Action<LeagueWorkbenchDiagnostic>? diagnosticReporter = null)
    {
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _dataSource = dataSource;
        _diagnosticReporter = diagnosticReporter;
        _leagueState = _productState.Current.League;
        _budgetName = _performance.Current.Name;
        _productState.Changed += OnProductStateChanged;
        _performance.BudgetChanged += OnBudgetChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LeagueWorkbenchSection> Sections => LeagueWorkbenchCatalog.Sections;
    public LeagueProductState LeagueState => _leagueState;
    public string LeagueStateTextKey => ResolveStateTextKey(_leagueState);
    public string BudgetName => _budgetName;
    public LeagueWorkbenchDashboardSnapshot Dashboard => _dashboard;
    public LeagueWorkbenchPlayerSnapshot Player => _player;
    public LeagueWorkbenchLiveSnapshot Live => _live;
    public LeagueBuildAdvisorSnapshot Advisor => _advisor;
    public LeagueItemSetPlan? PreparedItemSet => _preparedItemSet;
    public string ItemSetStatus => _itemSetStatus;
    public bool IsRefreshing => _isRefreshing;
    public bool IsAdvisorRefreshing => _isAdvisorRefreshing;
    public bool IsItemSetBusy => _isItemSetBusy;
    public bool IsAutomationSettingsBusy => _isAutomationSettingsBusy;
    public bool HasRealDataSource => _dataSource is not null;
    public bool HasProductServices => _buildAdvisorService is not null && _itemSetService is not null;
    public bool HasMatchmakingAutomation => _automationSettings is not null && _matchmakingAutomation is not null;
    public bool AutoMatchmakingEnabled => _matchmakingAutomation?.AutoSearchEnabled ?? false;
    public bool AutoAcceptEnabled => _matchmakingAutomation?.AutoAcceptEnabled ?? false;
    public bool CanPrepareItemSet =>
        !_isItemSetBusy &&
        _itemSetService is not null &&
        _advisor.State == LeagueBuildAdvisorState.Ready &&
        _advisor.Recommendation is not null;

    internal ILeagueWorkbenchDataSource? DataSource => _dataSource;

    internal void ConfigureProductServices(
        ILeagueBuildAdvisorService buildAdvisorService,
        ILeagueItemSetService itemSetService,
        bool ownsServices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buildAdvisorService);
        ArgumentNullException.ThrowIfNull(itemSetService);
        if (HasProductServices) return;

        _buildAdvisorService = buildAdvisorService;
        _itemSetService = itemSetService;
        _ownsProductServices = ownsServices;
        OnPropertyChanged(nameof(HasProductServices));
        OnPropertyChanged(nameof(CanPrepareItemSet));
    }

    internal void ConfigureMatchmakingAutomation(
        ISettings2Repository settings,
        ILeagueMatchmakingAutomationService automation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(automation);
        if (HasMatchmakingAutomation) return;

        _automationSettings = settings;
        _matchmakingAutomation = automation;
        OnPropertyChanged(nameof(HasMatchmakingAutomation));
        OnPropertyChanged(nameof(AutoMatchmakingEnabled));
        OnPropertyChanged(nameof(AutoAcceptEnabled));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dataSource = _dataSource;
        var correlationId = LeagueDiagnosticContext.CreateCorrelationId();
        var refreshStartedUtc = DateTimeOffset.UtcNow;
        var refreshStartTimestamp = refreshStartedUtc;
        if (dataSource is null)
        {
            ReportWorkbenchDiagnostic(correlationId, "started", "refresh", "skipped", "no-data-source", refreshStartedUtc, refreshStartedUtc, 0);
            ReportWorkbenchDiagnostic(correlationId, "completed", "refresh", "skipped", "no-data-source", refreshStartedUtc, DateTimeOffset.UtcNow, 0);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (!await _refreshGate.WaitAsync(0, linked.Token).ConfigureAwait(false))
            {
                ReportWorkbenchDiagnostic(correlationId, "started", "refresh", "skipped", "busy", refreshStartedUtc, refreshStartedUtc, 0);
                ReportWorkbenchDiagnostic(correlationId, "completed", "refresh", "skipped", "busy", refreshStartedUtc, DateTimeOffset.UtcNow, 0);
                return;
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            ReportWorkbenchDiagnostic(correlationId, "started", "refresh", "cancelled", "cancelled-before-gate", refreshStartedUtc, refreshStartedUtc, 0);
            ReportWorkbenchDiagnostic(correlationId, "completed", "refresh", "cancelled", "cancelled-before-gate", refreshStartedUtc, DateTimeOffset.UtcNow, 0);
            return;
        }

        using var diagnosticScope = LeagueDiagnosticContext.Begin(correlationId, "workbench", "refresh");
        ReportWorkbenchDiagnostic(correlationId, "started", "refresh", "started", "started", refreshStartedUtc, refreshStartedUtc, 0);
        var refreshOutcome = "unhandled-exception";
        var refreshReason = "unhandled-exception";
        try
        {
            SetRefreshing(true);
            var dashboard = await RunObservedStageAsync(
                correlationId,
                "dashboard",
                () => dataSource.LoadDashboardAsync(linked.Token)).ConfigureAwait(false);
            LeagueWorkbenchPlayerSnapshot player;
            if (dashboard.Account is null)
            {
                ReportSkippedStage(correlationId, "player", "current-player-unavailable");
                player = LeagueWorkbenchPlayerSnapshot.Unavailable("current-player-unavailable");
            }
            else
            {
                player = await RunObservedStageAsync(
                    correlationId,
                    "player",
                    () => dataSource.LoadCurrentPlayerAsync(0, 10, linked.Token)).ConfigureAwait(false);
            }
            var live = await RunObservedStageAsync(
                correlationId,
                "live",
                () => dataSource.LoadLiveAsync(linked.Token)).ConfigureAwait(false);

            _dashboard = dashboard;
            _player = player;
            _live = live;
            OnPropertyChanged(nameof(Dashboard));
            OnPropertyChanged(nameof(Player));
            OnPropertyChanged(nameof(Live));

            var advisorService = _buildAdvisorService;
            if (advisorService is not null)
            {
                try
                {
                    SetAdvisor(await RunObservedStageAsync(
                        correlationId,
                        "advisor",
                        () => advisorService.RefreshAsync(false, linked.Token)).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    SetAdvisor(LeagueBuildAdvisorSnapshot.Unavailable(live.Phase, "advisor-refresh-failed"));
                }
            }
            else
            {
                ReportSkippedStage(correlationId, "advisor", "service-unavailable");
            }
            refreshOutcome = "success";
            refreshReason = "completed";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            refreshOutcome = "cancelled";
            refreshReason = "cancelled";
        }
        catch (Exception exception)
        {
            refreshOutcome = "failure";
            refreshReason = exception.GetType().Name;
            _dashboard = LeagueWorkbenchDashboardSnapshot.Unavailable("refresh-failed");
            _player = LeagueWorkbenchPlayerSnapshot.Unavailable("refresh-failed");
            _live = LeagueWorkbenchLiveSnapshot.Unavailable(_live.Phase, "refresh-failed");
            OnPropertyChanged(nameof(Dashboard));
            OnPropertyChanged(nameof(Player));
            OnPropertyChanged(nameof(Live));
        }
        finally
        {
            ReportWorkbenchDiagnostic(
                correlationId,
                "completed",
                "refresh",
                refreshOutcome,
                refreshReason,
                refreshStartedUtc,
                DateTimeOffset.UtcNow,
                ElapsedMilliseconds(refreshStartTimestamp));
            SetRefreshing(false);
            _refreshGate.Release();
        }
    }

    private async Task<T> RunObservedStageAsync<T>(
        string correlationId,
        string stage,
        Func<Task<T>> operation)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var startTimestamp = startedUtc;
        ReportWorkbenchDiagnostic(correlationId, "started", stage, "started", "started", startedUtc, startedUtc, 0);
        using var diagnosticScope = LeagueDiagnosticContext.Begin(correlationId, "workbench", stage);
        try
        {
            var result = await operation().ConfigureAwait(false);
            ReportWorkbenchDiagnostic(
                correlationId,
                "completed",
                stage,
                "success",
                "completed",
                startedUtc,
                DateTimeOffset.UtcNow,
                ElapsedMilliseconds(startTimestamp));
            return result;
        }
        catch (OperationCanceledException)
        {
            ReportWorkbenchDiagnostic(
                correlationId,
                "completed",
                stage,
                "cancelled",
                "cancelled",
                startedUtc,
                DateTimeOffset.UtcNow,
                ElapsedMilliseconds(startTimestamp));
            throw;
        }
        catch (Exception exception)
        {
            ReportWorkbenchDiagnostic(
                correlationId,
                "completed",
                stage,
                "failure",
                exception.GetType().Name,
                startedUtc,
                DateTimeOffset.UtcNow,
                ElapsedMilliseconds(startTimestamp));
            throw;
        }
    }

    private void ReportSkippedStage(string correlationId, string stage, string reason)
    {
        var timestamp = DateTimeOffset.UtcNow;
        ReportWorkbenchDiagnostic(correlationId, "started", stage, "skipped", reason, timestamp, timestamp, 0);
        ReportWorkbenchDiagnostic(correlationId, "completed", stage, "skipped", reason, timestamp, DateTimeOffset.UtcNow, 0);
    }

    private static long ElapsedMilliseconds(DateTimeOffset startedUtc) =>
        Math.Max(0L, (long)(DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds);

    private void ReportWorkbenchDiagnostic(
        string correlationId,
        string eventName,
        string stage,
        string outcome,
        string reason,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        long durationMs,
        Exception? exception = null)
    {
        try
        {
            _diagnosticReporter?.Invoke(new LeagueWorkbenchDiagnostic(
                correlationId,
                eventName,
                stage,
                outcome,
                reason,
                startedUtc,
                finishedUtc,
                durationMs,
                exception?.GetType().FullName ?? string.Empty,
                exception is null
                    ? string.Empty
                    : "0x" + exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                Environment.CurrentManagedThreadId,
                SynchronizationContext.Current?.GetType().FullName ?? string.Empty));
        }
        catch
        {
            // Diagnostics must never change Workbench behavior.
        }
    }

    public async Task RefreshBuildAdvisorAsync(
        bool force = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var service = _buildAdvisorService;
        if (service is null) return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (!await _advisorUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        try
        {
            SetAdvisorRefreshing(true);
            SetAdvisor(await service.RefreshAsync(force, linked.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetAdvisor(LeagueBuildAdvisorSnapshot.Unavailable(_live.Phase, "advisor-refresh-failed"));
        }
        finally
        {
            SetAdvisorRefreshing(false);
            _advisorUiGate.Release();
        }
    }

    public async Task<LeagueItemSetPlan?> PrepareItemSetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var service = _itemSetService;
        if (service is null || !CanPrepareItemSet) return null;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (!await _itemSetUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return null;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            SetItemSetBusy(true);
            var plan = await service.PrepareAsync(_advisor, linked.Token).ConfigureAwait(false);
            _preparedItemSet = plan;
            _itemSetStatus = plan is null ? "prepare-unavailable" : "prepared";
            OnPropertyChanged(nameof(PreparedItemSet));
            OnPropertyChanged(nameof(ItemSetStatus));
            return plan;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            _preparedItemSet = null;
            _itemSetStatus = "prepare-failed";
            OnPropertyChanged(nameof(PreparedItemSet));
            OnPropertyChanged(nameof(ItemSetStatus));
            return null;
        }
        finally
        {
            SetItemSetBusy(false);
            _itemSetUiGate.Release();
        }
    }

    public async Task<LeagueItemSetApplyResult?> ApplyItemSetAsync(
        LeagueItemSetPlan plan,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plan);
        var service = _itemSetService;
        if (service is null) return null;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            if (!await _itemSetUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return null;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            SetItemSetBusy(true);
            var result = await service.ApplyAsync(plan, linked.Token).ConfigureAwait(false);
            _itemSetStatus = result.Detail;
            OnPropertyChanged(nameof(ItemSetStatus));
            return result;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            _itemSetStatus = "apply-failed";
            OnPropertyChanged(nameof(ItemSetStatus));
            return null;
        }
        finally
        {
            SetItemSetBusy(false);
            _itemSetUiGate.Release();
        }
    }

    public Task<bool> SetAutoMatchmakingEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SaveAutomationSettingsAsync(enabled, null, cancellationToken);

    public Task<bool> SetAutoAcceptEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SaveAutomationSettingsAsync(null, enabled, cancellationToken);

    private async Task<bool> SaveAutomationSettingsAsync(
        bool? autoMatchmaking,
        bool? autoAccept,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = _automationSettings;
        var automation = _matchmakingAutomation;
        if (settings is null || automation is null) return false;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _automationSettingsGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            SetAutomationSettingsBusy(true);
            var updated = await settings.UpdateAsync(
                document =>
                {
                    if (autoMatchmaking.HasValue)
                        document.League.AutoMatchmakingEnabled = autoMatchmaking.Value;
                    if (autoAccept.HasValue)
                        document.League.AutoAcceptEnabled = autoAccept.Value;
                },
                allowRecoveryRebuild: false,
                cancellationToken: linked.Token).ConfigureAwait(false);
            if (!updated.Persisted)
            {
                OnPropertyChanged(nameof(AutoMatchmakingEnabled));
                OnPropertyChanged(nameof(AutoAcceptEnabled));
                return false;
            }

            automation.Configure(
                updated.Settings.League.AutoMatchmakingEnabled,
                updated.Settings.League.AutoAcceptEnabled,
                "ui-settings-persisted");
            OnPropertyChanged(nameof(AutoMatchmakingEnabled));
            OnPropertyChanged(nameof(AutoAcceptEnabled));
            return true;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            OnPropertyChanged(nameof(AutoMatchmakingEnabled));
            OnPropertyChanged(nameof(AutoAcceptEnabled));
            return false;
        }
        finally
        {
            SetAutomationSettingsBusy(false);
            _automationSettingsGate.Release();
        }
    }

    private void OnProductStateChanged(object? sender, ProductStateChangedEventArgs args)
    {
        if (args.Previous.League == args.Current.League) return;
        _leagueState = args.Current.League;
        OnPropertyChanged(nameof(LeagueState));
        OnPropertyChanged(nameof(LeagueStateTextKey));
    }

    private void OnBudgetChanged(PerformanceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (string.Equals(_budgetName, budget.Name, StringComparison.Ordinal)) return;
        _budgetName = budget.Name;
        OnPropertyChanged(nameof(BudgetName));
    }

    private void SetAdvisor(LeagueBuildAdvisorSnapshot snapshot)
    {
        _advisor = snapshot ?? LeagueBuildAdvisorSnapshot.Unavailable(_live.Phase, "advisor-null");
        if (_advisor.State != LeagueBuildAdvisorState.Ready) _preparedItemSet = null;
        OnPropertyChanged(nameof(Advisor));
        OnPropertyChanged(nameof(PreparedItemSet));
        OnPropertyChanged(nameof(CanPrepareItemSet));
    }

    private void SetRefreshing(bool refreshing)
    {
        if (_isRefreshing == refreshing) return;
        _isRefreshing = refreshing;
        OnPropertyChanged(nameof(IsRefreshing));
    }

    private void SetAdvisorRefreshing(bool refreshing)
    {
        if (_isAdvisorRefreshing == refreshing) return;
        _isAdvisorRefreshing = refreshing;
        OnPropertyChanged(nameof(IsAdvisorRefreshing));
    }

    private void SetItemSetBusy(bool busy)
    {
        if (_isItemSetBusy == busy) return;
        _isItemSetBusy = busy;
        OnPropertyChanged(nameof(IsItemSetBusy));
        OnPropertyChanged(nameof(CanPrepareItemSet));
    }

    private void SetAutomationSettingsBusy(bool busy)
    {
        if (_isAutomationSettingsBusy == busy) return;
        _isAutomationSettingsBusy = busy;
        OnPropertyChanged(nameof(IsAutomationSettingsBusy));
    }

    private void OnPropertyChanged(string propertyName)
    {
        if (_disposed) return;
        var handlers = PropertyChanged?.GetInvocationList();
        if (handlers is null) return;

        var args = new PropertyChangedEventArgs(propertyName);
        foreach (var callback in handlers.OfType<PropertyChangedEventHandler>())
        {
            try
            {
                callback(this, args);
            }
            catch (Exception exception)
            {
                var now = DateTimeOffset.UtcNow;
                ReportWorkbenchDiagnostic(
                    LeagueDiagnosticContext.Current?.CorrelationId ?? LeagueDiagnosticContext.CreateCorrelationId(),
                    "property-change-failed",
                    "ui-notification",
                    "failure",
                    propertyName + ":" + (callback.Method.DeclaringType?.Name ?? "handler"),
                    now,
                    now,
                    0,
                    exception);
            }
        }
    }

    public static string ResolveStateTextKey(LeagueProductState state) => state switch
    {
        LeagueProductState.NotRunning => UiTextKeys.LeagueStateNotRunning,
        LeagueProductState.Connecting => UiTextKeys.LeagueStateConnecting,
        LeagueProductState.Lobby => UiTextKeys.LeagueStateLobby,
        LeagueProductState.Matchmaking => UiTextKeys.LeagueStateMatchmaking,
        LeagueProductState.ReadyCheck => UiTextKeys.LeagueStateReadyCheck,
        LeagueProductState.ChampSelect => UiTextKeys.LeagueStateChampSelect,
        LeagueProductState.InGame => UiTextKeys.LeagueStateInGame,
        LeagueProductState.PostGame => UiTextKeys.LeagueStatePostGame,
        LeagueProductState.ClientError => UiTextKeys.LeagueStateClientError,
        _ => UiTextKeys.LeagueStateClientError
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _productState.Changed -= OnProductStateChanged;
        _performance.BudgetChanged -= OnBudgetChanged;

        var itemSetService = _itemSetService;
        var advisorService = _buildAdvisorService;
        var disposeOwnedServices = _ownsProductServices;
        _itemSetService = null;
        _buildAdvisorService = null;
        _automationSettings = null;
        _matchmakingAutomation = null;
        _preparedItemSet = null;
        PropertyChanged = null;

        // Synchronous Window.Closed cannot await in-flight refresh/apply operations. Do not destroy
        // semaphores underneath their finally blocks; cancel first and dispose owned transports only
        // after all three product-operation gates have become idle.
        if (disposeOwnedServices)
            _ = DisposeOwnedProductServicesWhenIdleAsync(itemSetService, advisorService);
    }

    private async Task DisposeOwnedProductServicesWhenIdleAsync(
        ILeagueItemSetService? itemSetService,
        ILeagueBuildAdvisorService? advisorService)
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _advisorUiGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await _itemSetUiGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (itemSetService is IDisposable itemSetDisposable) itemSetDisposable.Dispose();
                    if (advisorService is IDisposable advisorDisposable) advisorDisposable.Dispose();
                }
                finally
                {
                    _itemSetUiGate.Release();
                }
            }
            finally
            {
                _advisorUiGate.Release();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
