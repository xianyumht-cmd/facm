namespace FACM.Core.State;

public enum ApplicationProductState
{
    Starting,
    Ready,
    Degraded,
    ShuttingDown
}

public enum LeagueProductState
{
    NotRunning,
    Connecting,
    Lobby,
    Matchmaking,
    ReadyCheck,
    ChampSelect,
    InGame,
    PostGame,
    ClientError
}

public enum ServiceHealth
{
    Unknown,
    Healthy,
    Degraded,
    Unavailable
}

public sealed record ProductEnvironmentState(
    string DistributionDirectory,
    bool? IsElevated,
    bool? NetworkAvailable)
{
    public static ProductEnvironmentState Unknown { get; } = new(string.Empty, null, null);
}

public sealed record ProductServicesState(
    ServiceHealth UpdateMetadata,
    ServiceHealth LeagueTransport,
    ServiceHealth PetHost)
{
    public static ProductServicesState Unknown { get; } = new(
        ServiceHealth.Unknown,
        ServiceHealth.Unknown,
        ServiceHealth.Unknown);
}

public sealed record ProductStateSnapshot(
    long Revision,
    DateTimeOffset TimestampUtc,
    ApplicationProductState Application,
    LeagueProductState League,
    ProductEnvironmentState Environment,
    ProductServicesState Services)
{
    public static ProductStateSnapshot CreateInitial(DateTimeOffset timestampUtc) => new(
        0,
        timestampUtc,
        ApplicationProductState.Starting,
        LeagueProductState.NotRunning,
        ProductEnvironmentState.Unknown,
        ProductServicesState.Unknown);
}

public sealed class ProductStateChangedEventArgs(
    ProductStateSnapshot previous,
    ProductStateSnapshot current,
    string reason) : EventArgs
{
    public ProductStateSnapshot Previous { get; } = previous;
    public ProductStateSnapshot Current { get; } = current;
    public string Reason { get; } = reason ?? string.Empty;
}

public interface IProductStateReader
{
    ProductStateSnapshot Current { get; }
    event EventHandler<ProductStateChangedEventArgs>? Changed;
}

public interface IProductStateWriter : IProductStateReader
{
    void SetApplication(ApplicationProductState state, string reason = "");
    void SetLeague(LeagueProductState state, string reason = "");
    void SetEnvironment(ProductEnvironmentState state, string reason = "");
    void SetServices(ProductServicesState state, string reason = "");
}

public sealed class ProductStateStore : IProductStateWriter
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private ProductStateSnapshot _current;

    public ProductStateStore(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _current = ProductStateSnapshot.CreateInitial(_utcNow());
    }

    public ProductStateSnapshot Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public event EventHandler<ProductStateChangedEventArgs>? Changed;

    public void SetApplication(ApplicationProductState state, string reason = "") =>
        Update(current => current with { Application = state }, reason);

    public void SetLeague(LeagueProductState state, string reason = "") =>
        Update(current => current with { League = state }, reason);

    public void SetEnvironment(ProductEnvironmentState state, string reason = "")
    {
        ArgumentNullException.ThrowIfNull(state);
        Update(current => current with { Environment = state }, reason);
    }

    public void SetServices(ProductServicesState state, string reason = "")
    {
        ArgumentNullException.ThrowIfNull(state);
        Update(current => current with { Services = state }, reason);
    }

    private void Update(Func<ProductStateSnapshot, ProductStateSnapshot> change, string reason)
    {
        ProductStateSnapshot previous;
        ProductStateSnapshot next;
        EventHandler<ProductStateChangedEventArgs>? handler;

        lock (_gate)
        {
            previous = _current;
            var candidate = change(previous);
            if (candidate.Application == previous.Application &&
                candidate.League == previous.League &&
                Equals(candidate.Environment, previous.Environment) &&
                Equals(candidate.Services, previous.Services))
            {
                return;
            }

            next = candidate with
            {
                Revision = checked(previous.Revision + 1),
                TimestampUtc = _utcNow()
            };
            _current = next;
            handler = Changed;
        }

        // Subscribers are deliberately invoked outside the state lock. UI, logging or adapter
        // callbacks must never block writers or create lock-order coupling.
        handler?.Invoke(this, new ProductStateChangedEventArgs(previous, next, reason));
    }
}
