using System.Globalization;
using System.Text;
using System.Text.Json;
using FACM.Core.League;

namespace FACM.Infrastructure.League;

/// <summary>
/// FACM 3.5.15 rune/spell application rebuilt over FACM 4.0's shared League gateway. Preparation is
/// read-only; Apply revalidates Champ Select/champion/queue immediately before the first LCU write.
/// Only FACM-owned rune pages may be reused, and success requires stable readback rather than a 2xx.
/// </summary>
public sealed class LeagueBuildLoadoutService : ILeagueBuildLoadoutService, IDisposable
{
    internal const string MySelectionPath = "/lol-champ-select/v1/session/my-selection";
    internal const string PerkInventoryPath = "/lol-perks/v1/inventory";
    internal const string PerkPagesPath = "/lol-perks/v1/pages";
    internal const string PerkCurrentPagePath = "/lol-perks/v1/currentpage";
    internal const string OwnedRunePagePrefix = "[FACM]";
    internal const int FlashSpellId = 4;

    internal static readonly TimeSpan RuneSettleDelay = TimeSpan.FromMilliseconds(180);
    internal static readonly TimeSpan SpellStableConfirmationDelay = TimeSpan.FromMilliseconds(180);
    internal static readonly TimeSpan[] SpellVerificationDelays =
    [
        TimeSpan.FromMilliseconds(90),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(360),
        TimeSpan.FromMilliseconds(500)
    ];

    private readonly ILeagueWorkbenchDataSource _workbench;
    private readonly ILeagueReadGateway _read;
    private readonly ILeagueWriteGateway _write;
    private readonly IOpggBuildSource _opgg;
    private readonly bool _ownsOpgg;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private bool _disposed;

    public LeagueBuildLoadoutService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway read,
        ILeagueWriteGateway write)
        : this(workbench, read, write, new OpggBuildHttpSource(), ownsOpgg: true)
    {
    }

    internal LeagueBuildLoadoutService(
        ILeagueWorkbenchDataSource workbench,
        ILeagueReadGateway read,
        ILeagueWriteGateway write,
        IOpggBuildSource opgg,
        bool ownsOpgg = false,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _opgg = opgg ?? throw new ArgumentNullException(nameof(opgg));
        _ownsOpgg = ownsOpgg;
        _delay = delay ?? Task.Delay;
    }

    public async Task<LeagueBuildLoadoutPlan?> PrepareAsync(
        LeagueBuildAdvisorSnapshot advisor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsUsableAdvisor(advisor)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            var bytes = await _opgg.TryGetBytesAsync(
                LeagueBuildAdvisorService.BuildPath(advisor.ChampionId, advisor.Mode, advisor.Position, advisor.Version),
                timeout.Token).ConfigureAwait(false);
            var parsed = ParsePlan(bytes, advisor);
            return parsed is not null && (parsed.HasRunes || parsed.HasSpells) ? parsed : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<LeagueBuildLoadoutApplyResult> ApplyAsync(
        LeagueBuildLoadoutPlan plan,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(plan);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var live = await _workbench.LoadLiveAsync(cancellationToken).ConfigureAwait(false);
            var blockReason = ValidateContext(live, plan);
            if (!string.IsNullOrEmpty(blockReason))
            {
                return Result(
                    "blocked",
                    plan.HasRunes ? "not-started" : "not-available",
                    plan.HasSpells ? "not-started" : "not-available",
                    blockReason,
                    false,
                    false,
                    0);
            }

            var runeStatus = plan.HasRunes ? "not-started" : "not-available";
            var spellStatus = plan.HasSpells ? "not-started" : "not-available";
            var runesApplied = false;
            var spellsApplied = false;
            long createdRunePageId = 0;

            if (plan.HasRunes)
            {
                var runeResult = await ApplyRunesAsync(plan, cancellationToken).ConfigureAwait(false);
                runeStatus = runeResult.Status;
                runesApplied = runeResult.Applied;
                createdRunePageId = runeResult.CreatedPageId;
            }

            if (plan.HasSpells)
            {
                var spellResult = await ApplySpellsAsync(plan, cancellationToken).ConfigureAwait(false);
                spellStatus = spellResult.Status;
                spellsApplied = spellResult.Applied;
            }

            var allExpectedSucceeded = (!plan.HasRunes || runesApplied) && (!plan.HasSpells || spellsApplied);
            var status = allExpectedSucceeded && (plan.HasRunes || plan.HasSpells)
                ? "success"
                : runesApplied || spellsApplied ? "partial" : "failed";
            return Result(status, runeStatus, spellStatus, string.Empty, runesApplied, spellsApplied, createdRunePageId);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    internal LeagueBuildLoadoutPlan? ParsePlan(byte[]? bytes, LeagueBuildAdvisorSnapshot advisor)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;

        var spell1 = 0;
        var spell2 = 0;
        if (TryGetFirstObject(data, "summoner_spells", out var spells))
        {
            var ids = ReadIntArray(spells, "ids");
            if (ids.Count >= 2)
            {
                spell1 = ids[0];
                spell2 = ids[1];
            }
        }

        var primaryStyle = 0;
        var secondaryStyle = 0;
        var primaryRunes = new List<int>();
        var secondaryRunes = new List<int>();
        var statMods = new List<int>();
        if (TryResolveRuneBuild(data, out var rune))
        {
            primaryStyle = ReadInt(rune, "primary_page_id");
            secondaryStyle = ReadInt(rune, "secondary_page_id");
            primaryRunes.AddRange(ReadIntArray(rune, "primary_rune_ids"));
            secondaryRunes.AddRange(ReadIntArray(rune, "secondary_rune_ids"));
            statMods.AddRange(ReadIntArray(rune, "stat_mod_ids"));
        }

        return new LeagueBuildLoadoutPlan(
            advisor.ChampionId,
            advisor.ChampionName,
            advisor.QueueId,
            advisor.Mode,
            advisor.Position,
            advisor.Version,
            spell1,
            spell2,
            primaryStyle,
            secondaryStyle,
            primaryRunes,
            secondaryRunes,
            statMods,
            FindRecommendation(advisor.Recommendation, "summoner-spells"),
            FindRecommendation(advisor.Recommendation, "runes"));
    }

    internal static void PreserveFlashSlot(
        int oldSpell1Id,
        int oldSpell2Id,
        ref int newSpell1Id,
        ref int newSpell2Id)
    {
        if (newSpell1Id != FlashSpellId && newSpell2Id != FlashSpellId) return;
        if (oldSpell1Id == FlashSpellId && newSpell2Id == FlashSpellId)
            (newSpell1Id, newSpell2Id) = (newSpell2Id, newSpell1Id);
        else if (oldSpell2Id == FlashSpellId && newSpell1Id == FlashSpellId)
            (newSpell1Id, newSpell2Id) = (newSpell2Id, newSpell1Id);
    }

    private async Task<RuneApplyResult> ApplyRunesAsync(
        LeagueBuildLoadoutPlan plan,
        CancellationToken cancellationToken)
    {
        var inventoryBytes = await _read.TryGetBytesAsync(PerkInventoryPath, cancellationToken).ConfigureAwait(false);
        var canAdd = ReadBooleanProperty(inventoryBytes, "canAddCustomPage");
        if (inventoryBytes is null) return new RuneApplyResult(false, "inventory-unavailable", 0);

        var pagesBytes = await _read.TryGetBytesAsync(PerkPagesPath, cancellationToken).ConfigureAwait(false);
        var pages = ParsePages(pagesBytes);
        var pageName = BuildRunePageName(plan);
        var owned = pages.FirstOrDefault(page =>
            string.Equals(page.Name, pageName, StringComparison.OrdinalIgnoreCase));
        if (owned is null && !canAdd)
            owned = pages.FirstOrDefault(page => page.Name.StartsWith(OwnedRunePagePrefix, StringComparison.OrdinalIgnoreCase));

        var pageId = owned?.Id ?? 0;
        var reused = pageId > 0;
        if (!reused && !canAdd)
            return new RuneApplyResult(false, pagesBytes is null ? "pages-unavailable" : "no-capacity", 0);

        long createdPageId = 0;
        if (!reused)
        {
            var createJson = JsonSerializer.Serialize(new
            {
                name = pageName,
                isEditable = true,
                primaryStyleId = plan.PrimaryStyleId.ToString(CultureInfo.InvariantCulture)
            });
            var created = await _write.ExecuteAsync(
                new LeagueWriteCommand(LeagueWriteCapability.CreatePerkPage, null, createJson),
                cancellationToken).ConfigureAwait(false);
            if (created is null || !created.IsSuccessStatusCode)
                return new RuneApplyResult(false, "create-failed", 0);
            pageId = ReadId(created.Body);
            if (pageId <= 0) return new RuneApplyResult(false, "create-response-invalid", 0);
            createdPageId = pageId;
        }

        var selected = plan.SelectedPerkIds;
        var pageJson = JsonSerializer.Serialize(new
        {
            id = pageId,
            isRecommendationOverride = false,
            isTemporary = false,
            name = pageName,
            primaryStyleId = plan.PrimaryStyleId,
            subStyleId = plan.SecondaryStyleId,
            selectedPerkIds = selected
        });
        var updated = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.UpdatePerkPage, pageId, pageJson),
            cancellationToken).ConfigureAwait(false);
        if (updated is null || !updated.IsSuccessStatusCode)
            return new RuneApplyResult(false, "update-failed", createdPageId);

        if (!await SelectRunePageAsync(pageId, cancellationToken).ConfigureAwait(false))
            return new RuneApplyResult(false, "select-failed", createdPageId);

        if (!await VerifyRuneSelectionSettledAsync(
                pageId,
                plan.PrimaryStyleId,
                plan.SecondaryStyleId,
                selected,
                cancellationToken).ConfigureAwait(false))
        {
            // Match 3.5: retry only the page-selection intent once, never recreate/update repeatedly.
            if (!await SelectRunePageAsync(pageId, cancellationToken).ConfigureAwait(false) ||
                !await VerifyRuneSelectionSettledAsync(
                    pageId,
                    plan.PrimaryStyleId,
                    plan.SecondaryStyleId,
                    selected,
                    cancellationToken).ConfigureAwait(false))
                return new RuneApplyResult(false, "verify-failed", createdPageId);
        }

        return new RuneApplyResult(true, reused ? "applied-reused-owned-page" : "applied", createdPageId);
    }

    private async Task<SpellApplyResult> ApplySpellsAsync(
        LeagueBuildLoadoutPlan plan,
        CancellationToken cancellationToken)
    {
        var beforeBytes = await _read.TryGetBytesAsync(MySelectionPath, cancellationToken).ConfigureAwait(false);
        var before = ParseSpellSelection(beforeBytes);
        if (before is null) return new SpellApplyResult(false, "selection-unavailable");

        var spell1 = plan.Spell1Id;
        var spell2 = plan.Spell2Id;
        PreserveFlashSlot(before.Value.Spell1, before.Value.Spell2, ref spell1, ref spell2);
        if (before.Value.Spell1 == spell1 && before.Value.Spell2 == spell2)
            return new SpellApplyResult(true, "already-applied");

        var payload = JsonSerializer.Serialize(new { spell1Id = spell1, spell2Id = spell2 });
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(LeagueWriteCapability.ApplyMySelection, null, payload),
            cancellationToken).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
            return new SpellApplyResult(false, "write-failed");

        foreach (var delay in SpellVerificationDelays)
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);
            var current = ParseSpellSelection(
                await _read.TryGetBytesAsync(MySelectionPath, cancellationToken).ConfigureAwait(false));
            if (current is null || current.Value.Spell1 != spell1 || current.Value.Spell2 != spell2) continue;

            await _delay(SpellStableConfirmationDelay, cancellationToken).ConfigureAwait(false);
            var settled = ParseSpellSelection(
                await _read.TryGetBytesAsync(MySelectionPath, cancellationToken).ConfigureAwait(false));
            if (settled is not null && settled.Value.Spell1 == spell1 && settled.Value.Spell2 == spell2)
                return new SpellApplyResult(true, "applied");
        }

        return new SpellApplyResult(false, "verify-failed");
    }

    private async Task<bool> SelectRunePageAsync(long pageId, CancellationToken cancellationToken)
    {
        var response = await _write.ExecuteAsync(
            new LeagueWriteCommand(
                LeagueWriteCapability.SetCurrentPerkPage,
                null,
                pageId.ToString(CultureInfo.InvariantCulture)),
            cancellationToken).ConfigureAwait(false);
        return response is not null && response.IsSuccessStatusCode;
    }

    private async Task<bool> VerifyRuneSelectionSettledAsync(
        long pageId,
        int primaryStyleId,
        int secondaryStyleId,
        IReadOnlyList<int> selected,
        CancellationToken cancellationToken)
    {
        await _delay(RuneSettleDelay, cancellationToken).ConfigureAwait(false);
        var current = await _read.TryGetBytesAsync(PerkCurrentPagePath, cancellationToken).ConfigureAwait(false);
        var pages = await _read.TryGetBytesAsync(PerkPagesPath, cancellationToken).ConfigureAwait(false);
        if (!VerifyCurrentRunePage(current, pageId) ||
            !VerifyRunePage(pages, pageId, primaryStyleId, secondaryStyleId, selected))
            return false;

        await _delay(RuneSettleDelay, cancellationToken).ConfigureAwait(false);
        current = await _read.TryGetBytesAsync(PerkCurrentPagePath, cancellationToken).ConfigureAwait(false);
        return VerifyCurrentRunePage(current, pageId);
    }

    internal static bool VerifyCurrentRunePage(byte[]? bytes, long pageId)
    {
        using var document = ParseDocument(bytes);
        if (document is null) return false;
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Number && root.TryGetInt64(out var numeric)) return numeric == pageId;
        if (root.ValueKind == JsonValueKind.String && long.TryParse(root.GetString(), out numeric)) return numeric == pageId;
        return root.ValueKind == JsonValueKind.Object && ReadLong(root, "id") == pageId;
    }

    internal static bool VerifyRunePage(
        byte[]? bytes,
        long pageId,
        int primaryStyleId,
        int secondaryStyleId,
        IReadOnlyList<int> selected)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array) return false;
        foreach (var page in document.RootElement.EnumerateArray())
        {
            if (page.ValueKind != JsonValueKind.Object || ReadLong(page, "id") != pageId) continue;
            if (ReadInt(page, "primaryStyleId") != primaryStyleId || ReadInt(page, "subStyleId") != secondaryStyleId)
                return false;
            var actual = ReadIntArray(page, "selectedPerkIds").Where(id => id > 0).ToHashSet();
            return selected.Where(id => id > 0).All(actual.Contains);
        }
        return false;
    }

    private static string ValidateContext(LeagueWorkbenchLiveSnapshot live, LeagueBuildLoadoutPlan plan)
    {
        if (live.State == LeagueWorkbenchDataState.Unavailable ||
            !string.Equals(live.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            return "champ-select-required";
        var championId = ResolveChampionId(live);
        if (plan.ChampionId <= 0 || championId != plan.ChampionId) return "champion-changed";
        if (plan.QueueId > 0 && live.Queue?.QueueId > 0 && live.Queue.QueueId != plan.QueueId) return "queue-changed";
        return string.Empty;
    }

    private static int ResolveChampionId(LeagueWorkbenchLiveSnapshot live)
    {
        var local = live.Players.FirstOrDefault(player => player.IsLocalPlayer);
        if (local?.ChampionId > 0) return local.ChampionId;
        if (local?.ChampionPickIntent > 0) return local.ChampionPickIntent;
        return live.LocalActionChampionId > 0 ? live.LocalActionChampionId : 0;
    }

    private static bool IsUsableAdvisor(LeagueBuildAdvisorSnapshot advisor) =>
        advisor.State == LeagueBuildAdvisorState.Ready &&
        string.Equals(advisor.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase) &&
        advisor.ChampionId > 0 && advisor.Recommendation is not null &&
        !string.IsNullOrWhiteSpace(advisor.Mode) && !string.IsNullOrWhiteSpace(advisor.Version);

    private static string FindRecommendation(LeagueBuildRecommendation? recommendation, string category) =>
        recommendation?.Rows.FirstOrDefault(row => string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase))
            ?.Recommendation ?? string.Empty;

    private static string BuildRunePageName(LeagueBuildLoadoutPlan plan)
    {
        var champion = string.IsNullOrWhiteSpace(plan.ChampionName)
            ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
            : plan.ChampionName.Trim();
        var name = OwnedRunePagePrefix + " " + champion;
        if (!string.IsNullOrWhiteSpace(plan.Mode)) name += " " + plan.Mode.Trim();
        if (!string.IsNullOrWhiteSpace(plan.Position) && !string.Equals(plan.Position, "none", StringComparison.OrdinalIgnoreCase))
            name += " " + plan.Position.Trim();
        return name.Length <= 100 ? name : name[..100];
    }

    private static List<RunePageRow> ParsePages(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        var result = new List<RunePageRow>();
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var id = ReadLong(row, "id");
            var name = ReadString(row, "name");
            if (id > 0) result.Add(new RunePageRow(id, name));
        }
        return result;
    }

    private static (int Spell1, int Spell2)? ParseSpellSelection(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var spell1 = ReadInt(document.RootElement, "spell1Id");
        var spell2 = ReadInt(document.RootElement, "spell2Id");
        return spell1 > 0 && spell2 > 0 ? (spell1, spell2) : null;
    }

    private static bool ReadBooleanProperty(byte[]? bytes, string property)
    {
        using var document = ParseDocument(bytes);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(property, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static long ReadId(byte[]? bytes)
    {
        using var document = ParseDocument(bytes);
        return document is not null && document.RootElement.ValueKind == JsonValueKind.Object
            ? ReadLong(document.RootElement, "id")
            : 0;
    }

    private static bool TryResolveRuneBuild(JsonElement data, out JsonElement build)
    {
        build = default;
        JsonElement runePage;
        if (!TryGetFirstObject(data, "runes", out runePage) && !TryGetFirstObject(data, "rune_pages", out runePage))
            return false;
        build = TryGetFirstObject(runePage, "builds", out var inner) ? inner : runePage;
        return build.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetFirstObject(JsonElement source, string property, out JsonElement value)
    {
        value = default;
        if (!source.TryGetProperty(property, out var rows)) return false;
        if (rows.ValueKind == JsonValueKind.Object)
        {
            value = rows;
            return true;
        }
        if (rows.ValueKind != JsonValueKind.Array) return false;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object)
            {
                value = row;
                return true;
            }
        }
        return false;
    }

    private static List<int> ReadIntArray(JsonElement source, string property)
    {
        var result = new List<int>();
        if (!source.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return result;
        foreach (var value in values.EnumerateArray())
        {
            var number = value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric)
                ? numeric
                : value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out numeric)
                    ? numeric
                    : 0;
            if (number > 0) result.Add(number);
        }
        return result;
    }

    private static int ReadInt(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static long ReadLong(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) ? number : 0;
    }

    private static string ReadString(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static JsonDocument? ParseDocument(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { return null; }
    }

    private static LeagueBuildLoadoutApplyResult Result(
        string status,
        string runeStatus,
        string spellStatus,
        string blockReason,
        bool runesApplied,
        bool spellsApplied,
        long createdRunePageId) =>
        new(status, runeStatus, spellStatus, blockReason, runesApplied, spellsApplied, createdRunePageId);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _applyGate.Dispose();
        if (_ownsOpgg && _opgg is IDisposable disposable) disposable.Dispose();
    }

    private sealed record RunePageRow(long Id, string Name);
    private sealed record RuneApplyResult(bool Applied, string Status, long CreatedPageId);
    private sealed record SpellApplyResult(bool Applied, string Status);
}
