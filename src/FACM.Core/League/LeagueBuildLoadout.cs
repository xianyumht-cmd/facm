namespace FACM.Core.League;

public sealed record LeagueBuildLoadoutPlan(
    int ChampionId,
    string ChampionName,
    int QueueId,
    string Mode,
    string Position,
    string Version,
    int Spell1Id,
    int Spell2Id,
    int PrimaryStyleId,
    int SecondaryStyleId,
    IReadOnlyList<int> PrimaryRuneIds,
    IReadOnlyList<int> SecondaryRuneIds,
    IReadOnlyList<int> StatModIds,
    string SpellPreview,
    string RunePreview)
{
    public bool HasSpells => Spell1Id > 0 && Spell2Id > 0;
    public bool HasRunes => PrimaryStyleId > 0 && SecondaryStyleId > 0 &&
                            PrimaryRuneIds.Count > 0 && SecondaryRuneIds.Count > 0;

    public IReadOnlyList<int> SelectedPerkIds =>
        PrimaryRuneIds.Concat(SecondaryRuneIds).Concat(StatModIds).Where(id => id > 0).ToArray();
}

public sealed record LeagueBuildLoadoutApplyResult(
    string Status,
    string RuneStatus,
    string SpellStatus,
    string BlockReason,
    bool RunesApplied,
    bool SpellsApplied,
    long CreatedRunePageId)
{
    public bool AnyApplied => RunesApplied || SpellsApplied;
}

/// <summary>
/// Read-only preparation followed by an explicit, revalidated LCU write. UI must obtain user
/// confirmation between PrepareAsync and ApplyAsync. Auto-apply may call ApplyAsync only through
/// its separate stable-context policy.
/// </summary>
public interface ILeagueBuildLoadoutService
{
    Task<LeagueBuildLoadoutPlan?> PrepareAsync(
        LeagueBuildAdvisorSnapshot advisor,
        CancellationToken cancellationToken = default);

    Task<LeagueBuildLoadoutApplyResult> ApplyAsync(
        LeagueBuildLoadoutPlan plan,
        CancellationToken cancellationToken = default);
}
