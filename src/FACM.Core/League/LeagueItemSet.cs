namespace FACM.Core.League;

public sealed record LeagueItemSetBlock(
    string Title,
    IReadOnlyList<int> ItemIds);

public sealed record LeagueItemSetPlan(
    int ChampionId,
    string ChampionName,
    int QueueId,
    string Mode,
    string Position,
    string Version,
    string Uid,
    string Title,
    IReadOnlyList<LeagueItemSetBlock> Blocks)
{
    public int ItemCount => Blocks.Sum(block => block?.ItemIds?.Count ?? 0);
    public bool HasItems => Blocks.Any(block => block is not null && block.ItemIds.Count > 0);
}

public enum LeagueItemSetApplyState
{
    Success,
    Blocked,
    Failed
}

public sealed record LeagueItemSetApplyResult(
    LeagueItemSetApplyState State,
    string Detail,
    string TargetDirectory,
    string FileName,
    int RemovedOldFiles,
    bool CleanupWarning)
{
    public bool Succeeded => State == LeagueItemSetApplyState.Success;
}

/// <summary>
/// Explicit, user-driven item-set write boundary. Prepare is read-only; Apply revalidates the live
/// Champ Select context before any filesystem write and only manages FACM 4.0-owned files.
/// </summary>
public interface ILeagueItemSetService
{
    Task<LeagueItemSetPlan?> PrepareAsync(
        LeagueBuildAdvisorSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<LeagueItemSetApplyResult> ApplyAsync(
        LeagueItemSetPlan plan,
        CancellationToken cancellationToken = default);
}
