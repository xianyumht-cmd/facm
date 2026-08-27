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
    private LeagueProductState _leagueState;
    private string _budgetName;
    private bool _disposed;

    public LeagueWorkbenchViewModel(
        IProductStateReader productState,
        PerformanceBudgetProvider performance)
    {
        _productState = productState ?? throw new ArgumentNullException(nameof(productState));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
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
        _productState.Changed -= OnProductStateChanged;
        _performance.BudgetChanged -= OnBudgetChanged;
    }
}
