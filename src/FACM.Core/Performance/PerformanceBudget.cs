namespace FACM.Core.Performance;

public enum LeagueActivityLevel
{
    None = 0,
    Client = 1,
    Queueing = 2,
    ChampSelect = 3,
    InGame = 4
}

public sealed record PerformanceContext(LeagueActivityLevel LeagueActivity, bool UiVisible);

public sealed record PerformanceBudget(
    string Name,
    int NetworkConcurrency,
    int ImageDecodeConcurrency,
    int DiskIoConcurrency,
    int BackgroundCpuConcurrency,
    int MatchHistoryPrefetchCount,
    TimeSpan NonCriticalPollInterval,
    bool AllowBackgroundPrefetch,
    bool AllowMaintenanceWork,
    bool AllowVisualEnhancements)
{
    public PerformanceBudget Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Budget name is required.", nameof(Name));
        if (NetworkConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(NetworkConcurrency));
        if (ImageDecodeConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(ImageDecodeConcurrency));
        if (DiskIoConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(DiskIoConcurrency));
        if (BackgroundCpuConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(BackgroundCpuConcurrency));
        if (MatchHistoryPrefetchCount < 0) throw new ArgumentOutOfRangeException(nameof(MatchHistoryPrefetchCount));
        if (NonCriticalPollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(NonCriticalPollInterval));
        return this;
    }
}

public static class PerformancePolicy
{
    public static readonly PerformanceBudget Desktop = Budget("desktop", 4, 2, 2, 2, 20, 15, true, true, true);
    public static readonly PerformanceBudget Client = Budget("league-client", 3, 2, 2, 2, 12, 20, true, true, true);
    public static readonly PerformanceBudget Queueing = Budget("queueing", 2, 1, 1, 1, 4, 30, false, false, false);
    public static readonly PerformanceBudget ChampSelect = Budget("champ-select", 2, 1, 1, 1, 0, 45, false, false, false);
    public static readonly PerformanceBudget InGame = Budget("in-game", 1, 1, 1, 1, 0, 60, false, false, false);
    public static readonly PerformanceBudget Background = Budget("background", 1, 1, 1, 1, 0, 60, false, false, false);

    public static PerformanceBudget Resolve(PerformanceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.LeagueActivity == LeagueActivityLevel.InGame) return InGame;
        if (context.LeagueActivity == LeagueActivityLevel.ChampSelect) return ChampSelect;
        if (!context.UiVisible) return Background;
        if (context.LeagueActivity == LeagueActivityLevel.Queueing) return Queueing;
        if (context.LeagueActivity == LeagueActivityLevel.Client) return Client;
        return Desktop;
    }

    public static bool IsNoMoreAggressiveThan(PerformanceBudget candidate, PerformanceBudget ceiling)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ceiling);
        return candidate.NetworkConcurrency <= ceiling.NetworkConcurrency &&
               candidate.ImageDecodeConcurrency <= ceiling.ImageDecodeConcurrency &&
               candidate.DiskIoConcurrency <= ceiling.DiskIoConcurrency &&
               candidate.BackgroundCpuConcurrency <= ceiling.BackgroundCpuConcurrency &&
               candidate.MatchHistoryPrefetchCount <= ceiling.MatchHistoryPrefetchCount &&
               candidate.NonCriticalPollInterval >= ceiling.NonCriticalPollInterval &&
               (!candidate.AllowBackgroundPrefetch || ceiling.AllowBackgroundPrefetch) &&
               (!candidate.AllowMaintenanceWork || ceiling.AllowMaintenanceWork) &&
               (!candidate.AllowVisualEnhancements || ceiling.AllowVisualEnhancements);
    }

    private static PerformanceBudget Budget(
        string name,
        int network,
        int image,
        int disk,
        int cpu,
        int prefetch,
        int pollSeconds,
        bool backgroundPrefetch,
        bool maintenance,
        bool visual) =>
        new PerformanceBudget(
            name,
            network,
            image,
            disk,
            cpu,
            prefetch,
            TimeSpan.FromSeconds(pollSeconds),
            backgroundPrefetch,
            maintenance,
            visual).Validate();
}

public sealed class PerformanceBudgetProvider
{
    private readonly object _sync = new();
    private LeagueActivityLevel _leagueActivity;
    private bool _uiVisible = true;
    private PerformanceBudget _current = PerformancePolicy.Desktop;

    public event Action<PerformanceBudget>? BudgetChanged;

    public PerformanceBudget Current
    {
        get
        {
            lock (_sync) return _current;
        }
    }

    public void UpdateLeagueActivity(LeagueActivityLevel activity) => Update(activity, null);
    public void UpdateUiVisibility(bool visible) => Update(null, visible);

    private void Update(LeagueActivityLevel? activity, bool? visible)
    {
        PerformanceBudget? changed = null;
        lock (_sync)
        {
            if (activity.HasValue) _leagueActivity = activity.Value;
            if (visible.HasValue) _uiVisible = visible.Value;
            var next = PerformancePolicy.Resolve(new PerformanceContext(_leagueActivity, _uiVisible));
            if (!ReferenceEquals(next, _current))
            {
                _current = next;
                changed = next;
            }
        }
        if (changed is not null) BudgetChanged?.Invoke(changed);
    }
}
