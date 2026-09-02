using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeagueRecommendedAutoApplySmoke
{
    private const int CycleStressIterations = 24;

    public static async Task RunAsync()
    {
        ValidateFlashSlotPreservation();
        ValidateFingerprintStability();
        await ValidateDisabledDoesNoWorkAsync();
        await ValidateStableContextAppliesAtMostOnceAsync();
        await ValidateBlockedLoadoutStopsItemSetWriteAsync();
        await ValidateRepeatedChampSelectCyclesAsync();
    }

    private static void ValidateFlashSlotPreservation()
    {
        var spell1 = 7;
        var spell2 = LeagueBuildLoadoutService.FlashSpellId;
        LeagueBuildLoadoutService.PreserveFlashSlot(
            LeagueBuildLoadoutService.FlashSpellId,
            14,
            ref spell1,
            ref spell2);
        Require(spell1 == LeagueBuildLoadoutService.FlashSpellId && spell2 == 7,
            "Recommended loadout did not preserve Flash in slot 1.");

        spell1 = LeagueBuildLoadoutService.FlashSpellId;
        spell2 = 14;
        LeagueBuildLoadoutService.PreserveFlashSlot(
            7,
            LeagueBuildLoadoutService.FlashSpellId,
            ref spell1,
            ref spell2);
        Require(spell1 == 14 && spell2 == LeagueBuildLoadoutService.FlashSpellId,
            "Recommended loadout did not preserve Flash in slot 2.");
    }

    private static void ValidateFingerprintStability()
    {
        var first = ReadyAdvisor(
            22,
            "15.17",
            [
                new LeagueBuildAdvisorRow("runes", "A / B", ""),
                new LeagueBuildAdvisorRow("summoner-spells", "Flash / Heal", "")
            ]);
        var reordered = ReadyAdvisor(
            22,
            "15.17",
            [
                new LeagueBuildAdvisorRow("summoner-spells", "Flash / Heal", ""),
                new LeagueBuildAdvisorRow("runes", "A / B", "")
            ]);
        var changed = ReadyAdvisor(
            22,
            "15.18",
            [
                new LeagueBuildAdvisorRow("runes", "A / B", ""),
                new LeagueBuildAdvisorRow("summoner-spells", "Flash / Heal", "")
            ]);

        var left = LeagueRecommendedAutoApplyService.BuildFingerprint(first);
        var right = LeagueRecommendedAutoApplyService.BuildFingerprint(reordered);
        Require(!string.IsNullOrWhiteSpace(left), "Recommended auto-apply fingerprint was empty for a ready build.");
        Require(string.Equals(left, right, StringComparison.Ordinal),
            "Recommended auto-apply fingerprint depended on provider row ordering.");
        Require(!string.Equals(left, LeagueRecommendedAutoApplyService.BuildFingerprint(changed), StringComparison.Ordinal),
            "Recommended auto-apply fingerprint did not change with the recommendation version.");
    }

    private static async Task ValidateDisabledDoesNoWorkAsync()
    {
        var clock = DateTimeOffset.Parse("2026-08-28T09:00:00Z");
        var gameflow = new FakeGameflow();
        var advisor = new FakeAdvisor { Snapshot = ReadyAdvisor() };
        var loadout = new FakeLoadout();
        var itemSets = new FakeItemSets();
        using var service = new LeagueRecommendedAutoApplyService(
            advisor,
            loadout,
            itemSets,
            gameflow,
            () => clock);

        service.Configure(false);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);

        Require(advisor.Calls == 0, "Disabled recommended auto-apply still read Build Advisor data.");
        Require(loadout.ApplyCalls == 0 && itemSets.ApplyCalls == 0,
            "Disabled recommended auto-apply performed a write.");
    }

    private static async Task ValidateStableContextAppliesAtMostOnceAsync()
    {
        var clock = DateTimeOffset.Parse("2026-08-28T09:00:00Z");
        var gameflow = new FakeGameflow();
        var advisor = new FakeAdvisor { Snapshot = ReadyAdvisor() };
        var loadout = new FakeLoadout();
        var itemSets = new FakeItemSets();
        using var service = new LeagueRecommendedAutoApplyService(
            advisor,
            loadout,
            itemSets,
            gameflow,
            () => clock);

        service.Configure(true);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        Require(loadout.ApplyCalls == 0 && itemSets.ApplyCalls == 0,
            "Recommended auto-apply wrote before the Champ Select context stabilized.");
        Require(service.LastStatus.State == "stabilizing",
            "First stable-context observation did not enter stabilizing state.");

        clock = clock.AddSeconds(2);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        Require(loadout.ApplyCalls == 1 && itemSets.ApplyCalls == 1,
            "Stable recommended context did not apply loadout and item set exactly once.");
        Require(service.LastStatus.State == "success",
            "Successful recommended auto-apply was not reported as success.");

        clock = clock.AddSeconds(2);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        Require(loadout.ApplyCalls == 1 && itemSets.ApplyCalls == 1,
            "The same recommended fingerprint was written more than once.");
        Require(service.LastStatus.State == "already-attempted",
            "Repeated stable fingerprint was not recorded as already-attempted.");

        clock = clock.AddSeconds(1);
        gameflow.CurrentValue = Lobby(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        clock = clock.AddSeconds(1);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        clock = clock.AddSeconds(2);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        Require(loadout.ApplyCalls == 2 && itemSets.ApplyCalls == 2,
            "A new Champ Select cycle did not release the previous recommendation fingerprint.");
    }

    private static async Task ValidateBlockedLoadoutStopsItemSetWriteAsync()
    {
        var clock = DateTimeOffset.Parse("2026-08-28T09:00:00Z");
        var gameflow = new FakeGameflow();
        var advisor = new FakeAdvisor { Snapshot = ReadyAdvisor() };
        var loadout = new FakeLoadout
        {
            ApplyResult = new LeagueBuildLoadoutApplyResult(
                "blocked",
                "not-started",
                "not-started",
                "champion-changed",
                false,
                false,
                0)
        };
        var itemSets = new FakeItemSets();
        using var service = new LeagueRecommendedAutoApplyService(
            advisor,
            loadout,
            itemSets,
            gameflow,
            () => clock);

        service.Configure(true);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
        clock = clock.AddSeconds(2);
        gameflow.CurrentValue = Champ(clock);
        await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);

        Require(loadout.ApplyCalls == 1, "Blocked loadout fixture was not attempted once.");
        Require(itemSets.ApplyCalls == 0,
            "Item-set disk write continued after the League loadout context was blocked.");
        Require(service.LastStatus.State == "blocked" && service.LastStatus.Detail == "champion-changed",
            "Blocked recommended transaction did not surface the loadout revalidation reason.");
    }

    private static async Task ValidateRepeatedChampSelectCyclesAsync()
    {
        var clock = DateTimeOffset.Parse("2026-08-28T10:00:00Z");
        var gameflow = new FakeGameflow();
        var advisor = new FakeAdvisor { Snapshot = ReadyAdvisor() };
        var loadout = new FakeLoadout();
        var itemSets = new FakeItemSets();
        using var service = new LeagueRecommendedAutoApplyService(
            advisor,
            loadout,
            itemSets,
            gameflow,
            () => clock);
        service.Configure(true);

        for (var cycle = 0; cycle < CycleStressIterations; cycle++)
        {
            gameflow.CurrentValue = Lobby(clock);
            await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);

            clock = clock.AddSeconds(1);
            gameflow.CurrentValue = Champ(clock);
            await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
            Require(loadout.ApplyCalls == cycle && itemSets.ApplyCalls == cycle,
                "Repeated Champ Select cycle wrote before stabilization at cycle " + cycle + ".");

            clock = clock.AddSeconds(2);
            gameflow.CurrentValue = Champ(clock);
            await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
            Require(loadout.ApplyCalls == cycle + 1 && itemSets.ApplyCalls == cycle + 1,
                "Repeated Champ Select cycle did not apply exactly once at cycle " + cycle + ".");

            clock = clock.AddSeconds(1);
            gameflow.CurrentValue = Champ(clock);
            await service.EvaluateForSmokeTestAsync(gameflow.CurrentValue);
            Require(loadout.ApplyCalls == cycle + 1 && itemSets.ApplyCalls == cycle + 1,
                "Repeated stable observation duplicated writes at cycle " + cycle + ".");
            Require(service.LastStatus.State == "already-attempted",
                "Repeated stable observation lost already-attempted state at cycle " + cycle + ".");

            clock = clock.AddSeconds(1);
        }

        Require(loadout.ApplyCalls == CycleStressIterations && itemSets.ApplyCalls == CycleStressIterations,
            "Repeated Champ Select stress did not preserve one-write-per-cycle behavior.");
    }

    private static LeagueBuildAdvisorSnapshot ReadyAdvisor(
        int championId = 22,
        string version = "15.17",
        IReadOnlyList<LeagueBuildAdvisorRow>? rows = null)
    {
        rows ??=
        [
            new LeagueBuildAdvisorRow("runes", "A / B", ""),
            new LeagueBuildAdvisorRow("summoner-spells", "Flash / Heal", "")
        ];
        return new LeagueBuildAdvisorSnapshot(
            LeagueBuildAdvisorState.Ready,
            "ChampSelect",
            420,
            championId,
            "Ashe",
            "ranked",
            "adc",
            "OP.GG Global",
            version,
            false,
            new LeagueBuildRecommendation("T2", 8, 0.51, 0.08, 0.02, rows),
            "ready",
            DateTimeOffset.UtcNow);
    }

    private static LeagueGameflowSnapshot Champ(DateTimeOffset now) =>
        new(now, LeagueConnectionState.Connected, "ChampSelect", LeagueProductState.ChampSelect, LeagueActivityLevel.ChampSelect);

    private static LeagueGameflowSnapshot Lobby(DateTimeOffset now) =>
        new(now, LeagueConnectionState.Connected, "Lobby", LeagueProductState.Lobby, LeagueActivityLevel.Client);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeGameflow : ILeagueGameflowObservationSource
    {
        public LeagueGameflowSnapshot? CurrentValue { get; set; }
        public LeagueGameflowSnapshot? Current => CurrentValue;
        public event EventHandler<LeagueGameflowChangedEventArgs>? Changed;
        public event EventHandler<LeagueGameflowChangedEventArgs>? Observed;

        public void Raise(LeagueGameflowSnapshot snapshot)
        {
            var previous = CurrentValue;
            CurrentValue = snapshot;
            Changed?.Invoke(this, new LeagueGameflowChangedEventArgs(previous, snapshot));
            Observed?.Invoke(this, new LeagueGameflowChangedEventArgs(previous, snapshot));
        }
    }

    private sealed class FakeAdvisor : ILeagueBuildAdvisorService
    {
        public LeagueBuildAdvisorSnapshot Snapshot { get; set; } = ReadyAdvisor();
        public int Calls { get; private set; }

        public Task<LeagueBuildAdvisorSnapshot> RefreshAsync(
            bool force = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeLoadout : ILeagueBuildLoadoutService
    {
        public int PrepareCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public LeagueBuildLoadoutApplyResult ApplyResult { get; set; } =
            new("success", "applied", "applied", string.Empty, true, true, 99);

        public Task<LeagueBuildLoadoutPlan?> PrepareAsync(
            LeagueBuildAdvisorSnapshot advisor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCalls++;
            LeagueBuildLoadoutPlan plan = new(
                advisor.ChampionId,
                advisor.ChampionName,
                advisor.QueueId,
                advisor.Mode,
                advisor.Position,
                advisor.Version,
                4,
                7,
                8000,
                8100,
                [8005, 8009, 9103, 8014],
                [8139, 8135],
                [5005, 5008, 5002],
                "Flash / Heal",
                "Precision / Domination");
            return Task.FromResult<LeagueBuildLoadoutPlan?>(plan);
        }

        public Task<LeagueBuildLoadoutApplyResult> ApplyAsync(
            LeagueBuildLoadoutPlan plan,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            return Task.FromResult(ApplyResult);
        }
    }

    private sealed class FakeItemSets : ILeagueItemSetService
    {
        public int PrepareCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<LeagueItemSetPlan?> PrepareAsync(
            LeagueBuildAdvisorSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCalls++;
            LeagueItemSetPlan plan = new(
                snapshot.ChampionId,
                snapshot.ChampionName,
                snapshot.QueueId,
                snapshot.Mode,
                snapshot.Position,
                snapshot.Version,
                "facm4-smoke",
                "[OP.GG] Ashe",
                [new LeagueItemSetBlock("Core", [3006, 3031, 3094])]);
            return Task.FromResult<LeagueItemSetPlan?>(plan);
        }

        public Task<LeagueItemSetApplyResult> ApplyAsync(
            LeagueItemSetPlan plan,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            return Task.FromResult(new LeagueItemSetApplyResult(
                LeagueItemSetApplyState.Success,
                "success",
                "C:\\League\\Config\\Global\\Recommended",
                "facm4-smoke.json",
                0,
                false));
        }
    }
}
