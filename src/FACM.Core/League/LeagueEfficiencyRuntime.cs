namespace FACM.Core.League;

public sealed record LeagueEfficiencyRuntimeState(
    string ExitGameHotkey,
    string CloseLobbyHotkey,
    string Status,
    string Detail,
    bool IsRecoveryReadOnly,
    bool IsBusy)
{
    public static LeagueEfficiencyRuntimeState Initial { get; } =
        new(string.Empty, string.Empty, "initializing", string.Empty, false, false);
}

public sealed class LeagueEfficiencyRuntimeStateChangedEventArgs(LeagueEfficiencyRuntimeState state) : EventArgs
{
    public LeagueEfficiencyRuntimeState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}

public interface ILeagueEfficiencyRuntime : IDisposable
{
    LeagueEfficiencyRuntimeState State { get; }
    event EventHandler<LeagueEfficiencyRuntimeStateChangedEventArgs>? StateChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<bool> UpdateBindingsAsync(
        string exitGameHotkey,
        string closeLobbyHotkey,
        CancellationToken cancellationToken = default);

    Task<LeagueEfficiencyActionResult> RunActionAsync(
        LeagueEfficiencyAction action,
        CancellationToken cancellationToken = default);
}
