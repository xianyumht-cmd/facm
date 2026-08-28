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
    private readonly CancellationTokenSource _lifetime = new();
    private LeagueProductState _leagueState;
    private string _budgetName;
    private LeagueWorkbenchDashboardSnapshot _dashboard = LeagueWorkbenchDashboardSnapshot.Unavailable("not-loaded");
    private LeagueWorkbenchPlayerSnapshot _player = LeagueWorkbenchPlayerSnapshot.Unavailable("not-loaded");
    private LeagueWorkbenchLiveSnapshot _live = LeagueWorkbenchLiveSnapshot.Unavailable(string.Empty, "not-loaded");
    private bool _isRefreshing;
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
    public bool IsRefreshing => _isRefreshing;
    public bool HasRealDataSource => _dataSource is not null;

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

    private void SetRefreshing(bool refreshing)
    {
        if (_isRefreshing == refreshing) return;
        _isRefreshing = refreshing;
        OnPropertyChanged(nameof(IsRefreshing));
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
        _lifetime.Dispose();
    }
}
