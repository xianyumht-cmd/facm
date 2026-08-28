using System.ComponentModel;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Core.Text;

namespace FACM.App.ViewModels;

public sealed class LeagueWorkbenchViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IProductStateReader _productState;
    private readonly PerformanceBudgetProvider _performance;
    private readonly ILeagueWorkbenchDataSource? _dataSource;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _advisorUiGate = new(1, 1);
    private readonly SemaphoreSlim _itemSetUiGate = new(1, 1);
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
    private bool _ownsProductServices;
    private bool _isRefreshing;
    private bool _isAdvisorRefreshing;
    private bool _isItemSetBusy;
    private bool _disposed;

    public LeagueWorkbenchViewModel(
        IProductStateReader productState,
        PerformanceBudgetProvider performance,
        ILeagueWorkbenchDataSource? dataSource = null)
    {
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _dataSource = dataSource;
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
    public bool HasRealDataSource => _dataSource is not null;
    public bool HasProductServices => _buildAdvisorService is not null && _itemSetService is not null;
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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dataSource = _dataSource;
        if (dataSource is null) return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!await _refreshGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            SetRefreshing(true);
            var dashboard = await dataSource.LoadDashboardAsync(linked.Token).ConfigureAwait(false);
            LeagueWorkbenchPlayerSnapshot player;
            if (dashboard.Account is null)
            {
                player = LeagueWorkbenchPlayerSnapshot.Unavailable("current-player-unavailable");
            }
            else
            {
                player = await dataSource.LoadCurrentPlayerAsync(0, 10, linked.Token).ConfigureAwait(false);
            }
            var live = await dataSource.LoadLiveAsync(linked.Token).ConfigureAwait(false);

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
                    SetAdvisor(await advisorService.RefreshAsync(false, linked.Token).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                    SetAdvisor(LeagueBuildAdvisorSnapshot.Unavailable(live.Phase, "advisor-refresh-failed"));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _dashboard = LeagueWorkbenchDashboardSnapshot.Unavailable("refresh-failed");
            _player = LeagueWorkbenchPlayerSnapshot.Unavailable("refresh-failed");
            _live = LeagueWorkbenchLiveSnapshot.Unavailable(_live.Phase, "refresh-failed");
            OnPropertyChanged(nameof(Dashboard));
            OnPropertyChanged(nameof(Player));
            OnPropertyChanged(nameof(Live));
        }
        finally
        {
            SetRefreshing(false);
            _refreshGate.Release();
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
        if (!await _advisorUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return;
        try
        {
            SetAdvisorRefreshing(true);
            SetAdvisor(await service.RefreshAsync(force, linked.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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
        if (!await _itemSetUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return null;
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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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
        if (!await _itemSetUiGate.WaitAsync(0, linked.Token).ConfigureAwait(false)) return null;
        try
        {
            SetItemSetBusy(true);
            var result = await service.ApplyAsync(plan, linked.Token).ConfigureAwait(false);
            _itemSetStatus = result.Detail;
            OnPropertyChanged(nameof(ItemSetStatus));
            return result;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
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

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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

        if (_ownsProductServices)
        {
            if (_itemSetService is IDisposable itemSetDisposable) itemSetDisposable.Dispose();
            if (_buildAdvisorService is IDisposable advisorDisposable) advisorDisposable.Dispose();
        }
        _itemSetService = null;
        _buildAdvisorService = null;
        _preparedItemSet = null;

        _refreshGate.Dispose();
        _advisorUiGate.Dispose();
        _itemSetUiGate.Dispose();
        _lifetime.Dispose();
    }
}
