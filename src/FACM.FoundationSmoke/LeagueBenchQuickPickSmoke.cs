using System.Text;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Core.State;
using FACM.Infrastructure.League;

internal static class LeagueBenchQuickPickSmoke
{
    public static async Task RunAsync()
    {
        ValidateWriteAllowlist();
        ValidatePollingPolicy();
        ValidateLegacyParser();
        ValidateChampionIdentityParser();
        await ValidateChampionIdentityFallbackAsync();
        ValidateBenchSwapStripPresentation();
        await ValidateProcessLevelBenchRuntimeLifecycleAsync();
        await ValidateTeamBuilderFallbackAndSingleWriteAsync();
        await ValidateVerificationFailureNeverRetriesWriteAsync();
        await ValidateTargetUnavailableSkipsReadbackAsync();
    }

    private static void ValidateWriteAllowlist()
    {
        var legacy = new LeagueWriteCommand(LeagueWriteCapability.SwapBenchChampionLegacy, 22, null);
        var teamBuilder = new LeagueWriteCommand(LeagueWriteCapability.SwapBenchChampionTeamBuilder, 33, null);
        Require(LeagueWriteTargetPolicy.Matches(legacy, "POST", "/lol-champ-select/v1/session/bench/swap/22"),
            "Legacy bench swap endpoint escaped the strict allowlist.");
        Require(LeagueWriteTargetPolicy.Matches(teamBuilder, "POST", "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/33"),
            "Team Builder bench swap endpoint escaped the strict allowlist.");

        try
        {
            LeagueWriteTargetPolicy.Resolve(new LeagueWriteCommand(LeagueWriteCapability.SwapBenchChampionLegacy, 0, null));
            throw new InvalidOperationException("Non-positive bench champion id should have been rejected.");
        }
        catch (ArgumentException)
        {
        }
    }

    private static void ValidatePollingPolicy()
    {
        Require(LeagueBenchQuickPickPolling.ResolveDelay(true, false, false) == TimeSpan.FromMilliseconds(100),
            "Active bench polling must preserve the 3.5 100ms cadence.");
        Require(LeagueBenchQuickPickPolling.ResolveDelay(false, false, false) == TimeSpan.FromMilliseconds(750),
            "Inactive bench polling must preserve the 3.5 750ms cadence.");
        Require(LeagueBenchQuickPickPolling.ResolveDelay(false, true, false) == TimeSpan.FromSeconds(5),
            "In-game bench polling must back off to 5 seconds.");
        Require(LeagueBenchQuickPickPolling.ResolveDelay(true, false, true) == TimeSpan.FromSeconds(1),
            "Hidden bench surface must back off to 1 second.");
    }

    private static void ValidateLegacyParser()
    {
        var state = LeagueBenchQuickPickService.ParseBenchState(Json("""
            {
              "benchEnabled": true,
              "isLegacyChampSelect": true,
              "localPlayerCellId": 2,
              "myTeam": [
                { "cellId": 1, "championId": 7 },
                { "cellId": 2, "championId": 11 }
              ],
              "benchChampions": [
                { "championId": 22 },
                { "championId": 44 }
              ],
              "benchChampionIds": [44, 99]
            }
            """));

        Require(state.SessionAvailable && state.BenchEnabled, "Legacy bench fixture was not recognized as active.");
        Require(state.SwapRoute == LeagueBenchSwapRoute.Legacy, "Legacy bench route was not preserved.");
        Require(state.LocalPlayerCellId == 2 && state.LocalChampionId == 11,
            "Local champion was not resolved from the local player cell.");
        Require(state.ChampionIds.SequenceEqual([22, 44, 99]),
            "Bench champion ids were not deduplicated in observed order.");
    }

    private static void ValidateChampionIdentityParser()
    {
        var catalog = LeagueBenchQuickPickService.ParseChampionIdentities(Json("""
            [
              { "id": 22, "nameTRA": "寒冰射手", "alias": "Ashe", "squarePortraitPath": "/img/ashe.png" },
              { "id": 44, "name": "塔里克", "iconPath": "/img/taric.png" }
            ]
            """));

        Require(catalog.Count == 2, "Champion identity catalog did not parse all entries.");
        Require(catalog[22].Name == "寒冰射手" && catalog[22].Alias == "Ashe" &&
                catalog[22].IconPath.EndsWith("ashe.png", StringComparison.Ordinal),
            "Preferred localized champion name, alias or portrait was not selected.");
        Require(catalog[44].Name == "塔里克", "Champion name fallback field was not selected.");

        var detail = LeagueBenchQuickPickService.ParseChampionDetailIdentity(240, Json("""
            { "id": 240, "name": "Kled", "title": "暴怒骑士", "alias": "Kled", "squarePortraitPath": "/img/kled.png" }
            """));
        Require(detail is not null && detail.Name == "暴怒骑士" && detail.Alias == "Kled" &&
                detail.IconPath.EndsWith("kled.png", StringComparison.Ordinal),
            "Champion detail fallback did not prefer the localized title, alias and portrait.");
    }

    private static void ValidateBenchSwapStripPresentation()
    {
        var catalog = LeagueBenchQuickPickService.ParseChampionIdentities(Json("""
            [
              { "id": 37, "nameTRA": "琴女", "alias": "Sona", "squarePortraitPath": "/img/sona.png" },
              { "id": 236, "nameTRA": "卢锡安", "alias": "Lucian", "squarePortraitPath": "/img/lucian.png" }
            ]
            """));
        var candidates = LeagueBenchCandidatePresentation.Create([37, 236, 9999, 37], catalog);

        Require(candidates.Count == 3, "Bench candidate presentation did not preserve unique observed candidates.");
        Require(candidates[0].ChampionId == 37 && candidates[0].DisplayName == "琴女" &&
                candidates[0].PortraitSource.EndsWith("sona.png", StringComparison.Ordinal),
            "Known candidate 37 did not resolve to its portrait identity.");
        Require(candidates[1].ChampionId == 236 && candidates[1].DisplayName == "卢锡安" &&
                candidates[1].AccessibleName == "卢锡安 · Swap",
            "Known candidate 236 did not expose an accessible portrait identity.");
        Require(candidates[2].DisplayName == "Unknown champion" && candidates[2].IsActionable,
            "Unknown candidates must use a compact non-ID fallback while remaining actionable.");
        Require(!candidates.Any(candidate => candidate.DisplayName == "#37" || candidate.DisplayName == "#236"),
            "Raw champion ids must not be the primary candidate identity.");

        var live = new LeagueWorkbenchLiveSnapshot(
            LeagueWorkbenchDataState.Ready,
            "ChampSelect",
            0,
            null,
            0,
            string.Empty,
            1,
            string.Empty,
            0,
            string.Empty,
            0,
            true,
            LeagueBenchSwapRoute.Legacy,
            [],
            [],
            [37, 236],
            [],
            "ready",
            DateTimeOffset.UtcNow);
        Require(LeagueBenchSwapStripPolicy.IsEligible(live) &&
                LeagueBenchSwapStripPolicy.CountActionableCandidates(live) == 2,
            "Actionable Champ Select Bench state did not enable the strip.");
        Require(LeagueBenchSwapStripPolicy.IsEligible(live with { BenchChampionIds = [37] }),
            "A single actionable Bench candidate must enable the strip.");
        Require(!LeagueBenchSwapStripPolicy.IsEligible(live with { BenchChampionIds = [] }),
            "An empty Bench candidate list must not enable the strip.");
        Require(!LeagueBenchSwapStripPolicy.IsEligible(live with { BenchEnabled = false }),
            "A disabled Bench must not enable the strip.");
        Require(!LeagueBenchSwapStripPolicy.IsEligible(live with { Phase = "InProgress" }) &&
                !LeagueBenchSwapStripPolicy.IsEligible(live with { Phase = "Lobby" }),
            "In-game and Lobby states must not enable the Champ Select strip.");
        Require(LeagueBenchSwapStripPolicy.ResolveWidthDip(2) >= LeagueBenchSwapStripPolicy.MinimumWidthDip &&
                LeagueBenchSwapStripPolicy.ResolveWidthDip(1) == LeagueBenchSwapStripPolicy.MinimumWidthDip &&
                LeagueBenchSwapStripPolicy.ResolveWidthDip(4) > LeagueBenchSwapStripPolicy.ResolveWidthDip(2) &&
                LeagueBenchSwapStripPolicy.ResolveWidthDip(20) == LeagueBenchSwapStripPolicy.MaximumWidthDip,
            "Bench strip geometry must be content-driven and capped.");

        Require(LeagueBenchStripInteractionPolicy.SuppressOutsideDismissal(FacmSurfaceMode.ChampSelectStrip),
            "Outside click must preserve the latched Bench strip.");
        Require(LeagueBenchStripInteractionPolicy.SuppressCollapse(FacmSurfaceMode.ChampSelectStrip),
            "The normal collapse action must not collapse the latched Bench strip.");
        Require(LeagueBenchStripInteractionPolicy.PreserveAfterCandidateClick(FacmSurfaceMode.ChampSelectStrip),
            "Candidate click must preserve the Bench strip.");
        Require(LeagueBenchStripInteractionPolicy.PreserveAfterHandleClick(FacmSurfaceMode.ChampSelectStrip),
            "A simple F-handle click must preserve the Bench strip.");
        Require(!LeagueBenchStripInteractionPolicy.SuppressOutsideDismissal(FacmSurfaceMode.ControlMatrix),
            "Ordinary expanded surfaces must retain outside-click dismissal.");
    }

    private static async Task ValidateChampionIdentityFallbackAsync()
    {
        var gateway = new FakeGateway
        {
            Reader = (path, _) => path switch
            {
                LeagueBenchQuickPickService.ChampionSummaryPath => null,
                LeagueBenchQuickPickService.ChampionDetailPathPrefix + "240.json" => Json("""
                    { "id": 240, "name": "Kled", "title": "暴怒骑士", "alias": "Kled", "squarePortraitPath": "/img/kled.png" }
                    """),
                _ => null
            }
        };

        using var service = new LeagueBenchQuickPickService(gateway, gateway);
        var identities = await service.LoadChampionIdentitiesAsync([240]);
        Require(identities.TryGetValue(240, out var identity) && identity.Name == "暴怒骑士" && identity.Alias == "Kled" &&
                identity.IconPath.EndsWith("kled.png", StringComparison.Ordinal),
            "Missing champion-summary data must fall back to the champion detail endpoint.");
        Require(gateway.PathReadCounts.TryGetValue(LeagueBenchQuickPickService.ChampionSummaryPath, out var summaryReads) && summaryReads == 1 &&
                gateway.PathReadCounts.TryGetValue(LeagueBenchQuickPickService.ChampionDetailPathPrefix + "240.json", out var detailReads) && detailReads == 1,
            "Champion identity fallback must use one summary read and one bounded detail read.");
    }

    private static async Task ValidateProcessLevelBenchRuntimeLifecycleAsync()
    {
        var gameflow = new FakeGameflow();
        var bench = new FakeBenchService
        {
            State = new LeagueBenchQuickPickState(true, true, 1, 10, LeagueBenchSwapRoute.Legacy, [22, 44])
        };
        using var runtime = new LeagueBenchRuntimeObserver(gameflow, bench);

        // No Workbench ViewModel is constructed or opened in this scenario. The observer alone must
        // activate the process-level state when ChampSelect candidates arrive.
        gameflow.Publish(Snapshot(LeagueProductState.ChampSelect, "ChampSelect"));
        await WaitUntilAsync(() => runtime.Current.IsLatched && runtime.Current.CandidateCount == 2,
            "Process-level Bench observer did not latch without opening Workbench.");
        Require(runtime.Current.ContextGeneration == 1, "The first ChampSelect context generation was not created.");
        Require(runtime.Current.LocalChampionId == 10,
            "Process-level Bench observer did not publish the authoritative local champion.");

        bench.State = bench.State with { LocalChampionId = 33, ChampionIds = [44, 77] };
        gameflow.Publish(Snapshot(LeagueProductState.ChampSelect, "ChampSelect"));
        await WaitUntilAsync(() => runtime.Current.ChampionIds.SequenceEqual([44, 77]),
            "Candidate changes did not update the latched runtime state in place.");
        await WaitUntilAsync(() => runtime.Current.LocalChampionId == 33,
            "A local champion change did not update the existing Bench context.");

        bench.State = bench.State with { ChampionIds = [] };
        gameflow.Publish(Snapshot(LeagueProductState.ChampSelect, "ChampSelect"));
        await WaitUntilAsync(() => runtime.Current.IsLatched && runtime.Current.CandidateCount == 0,
            "A temporary zero-candidate read collapsed the latched runtime state.");

        gameflow.Publish(Snapshot(LeagueProductState.InGame, "InProgress"));
        await WaitUntilAsync(() => !runtime.Current.IsChampSelect && !runtime.Current.IsLatched,
            "InGame did not clear the Bench runtime state.");

        bench.State = bench.State with { ChampionIds = [99] };
        gameflow.Publish(Snapshot(LeagueProductState.ChampSelect, "ChampSelect"));
        await WaitUntilAsync(() => runtime.Current.IsLatched && runtime.Current.ContextGeneration == 2,
            "A new ChampSelect context did not create a fresh latch.");

        gameflow.Publish(Snapshot(LeagueProductState.Lobby, "Lobby"));
        await WaitUntilAsync(() => !runtime.Current.IsChampSelect && !runtime.Current.IsLatched,
            "Lobby did not return the Bench runtime state to idle.");
    }

    private static LeagueGameflowSnapshot Snapshot(LeagueProductState productState, string phase) =>
        new(
            DateTimeOffset.UtcNow,
            LeagueConnectionState.Connected,
            phase,
            productState,
            productState == LeagueProductState.ChampSelect
                ? LeagueActivityLevel.ChampSelect
                : productState == LeagueProductState.InGame
                    ? LeagueActivityLevel.InGame
                    : LeagueActivityLevel.Client);

    private static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Require(condition(), message);
    }

    private static async Task ValidateTeamBuilderFallbackAndSingleWriteAsync()
    {
        var gateway = new FakeGateway
        {
            WriteResult = new LeagueWriteResult(204, Array.Empty<byte>())
        };
        gateway.Reader = (path, call) => path switch
        {
            LeagueBenchQuickPickService.ChampSelectSessionPath => Json("""
                {
                  "benchEnabled": true,
                  "isLegacyChampSelect": false,
                  "localPlayerCellId": 3,
                  "myTeam": [{ "cellId": 3, "championId": 10 }]
                }
                """),
            LeagueBenchQuickPickService.TeamBuilderChampSelectSessionPath when call == 1 => Json("""
                {
                  "benchEnabled": true,
                  "localPlayerCellId": 3,
                  "myTeam": [{ "cellId": 3, "championId": 10 }],
                  "benchChampionIds": [33, 55]
                }
                """),
            LeagueBenchQuickPickService.TeamBuilderChampSelectSessionPath => Json("""
                {
                  "benchEnabled": true,
                  "localPlayerCellId": 3,
                  "myTeam": [{ "cellId": 3, "championId": 33 }],
                  "benchChampionIds": [55]
                }
                """),
            _ => null
        };

        using var service = new LeagueBenchQuickPickService(gateway, gateway);
        var observed = await service.RefreshAsync();
        Require(observed.SwapRoute == LeagueBenchSwapRoute.TeamBuilder && observed.ChampionIds.SequenceEqual([33, 55]),
            "Team Builder compatibility fallback did not supply the bench list.");

        var result = await service.TrySwapAsync(33);
        Require(result.Status == LeagueBenchSwapStatus.Success,
            "Team Builder swap was not verified after the successful write.");
        Require(gateway.Commands.Count == 1,
            "One bench click emitted more than one write request.");
        Require(gateway.Commands[0].Capability == LeagueWriteCapability.SwapBenchChampionTeamBuilder &&
                gateway.Commands[0].ResourceId == 33,
            "Observed Team Builder route was not reused by the manual swap.");
    }

    private static async Task ValidateVerificationFailureNeverRetriesWriteAsync()
    {
        var gateway = new FakeGateway
        {
            WriteResult = new LeagueWriteResult(200, Array.Empty<byte>()),
            Reader = (path, _) => path == LeagueBenchQuickPickService.ChampSelectSessionPath
                ? Json("""
                    {
                      "benchEnabled": true,
                      "isLegacyChampSelect": true,
                      "localPlayerCellId": 1,
                      "myTeam": [{ "cellId": 1, "championId": 10 }],
                      "benchChampionIds": [44]
                    }
                    """)
                : null
        };

        using var service = new LeagueBenchQuickPickService(gateway, gateway);
        _ = await service.RefreshAsync();
        var result = await service.TrySwapAsync(44);
        Require(result.Status == LeagueBenchSwapStatus.VerificationFailed,
            "Unchanged local champion should fail bounded read-back verification.");
        Require(gateway.Commands.Count == 1,
            "Verification failure incorrectly retried the bench POST.");
        Require(gateway.PathReadCounts.TryGetValue(LeagueBenchQuickPickService.ChampSelectSessionPath, out var reads) && reads == 4,
            "Verification must use exactly three bounded read-backs after the initial observation.");
    }

    private static async Task ValidateTargetUnavailableSkipsReadbackAsync()
    {
        var gateway = new FakeGateway
        {
            WriteResult = new LeagueWriteResult(409, Array.Empty<byte>()),
            Reader = (path, _) => path == LeagueBenchQuickPickService.ChampSelectSessionPath
                ? Json("""
                    {
                      "benchEnabled": true,
                      "isLegacyChampSelect": true,
                      "localPlayerCellId": 1,
                      "myTeam": [{ "cellId": 1, "championId": 10 }],
                      "benchChampionIds": [77]
                    }
                    """)
                : null
        };

        using var service = new LeagueBenchQuickPickService(gateway, gateway);
        _ = await service.RefreshAsync();
        var before = gateway.TotalReads;
        var result = await service.TrySwapAsync(77);
        Require(result.Status == LeagueBenchSwapStatus.TargetUnavailable && result.StatusCode == 409,
            "HTTP 409 must map to a stale/unavailable bench target.");
        Require(gateway.Commands.Count == 1 && gateway.TotalReads == before,
            "Rejected stale target should not trigger write retries or verification reads.");

        try
        {
            _ = await service.TrySwapAsync(0);
            throw new InvalidOperationException("Zero champion id should have been rejected before transport.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        Require(gateway.Commands.Count == 1, "Invalid champion id reached the write gateway.");
    }

    private static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeGateway : ILeagueReadGateway, ILeagueWriteGateway
    {
        public Func<string, int, byte[]?>? Reader { get; set; }
        public LeagueWriteResult? WriteResult { get; set; }
        public List<LeagueWriteCommand> Commands { get; } = [];
        public Dictionary<string, int> PathReadCounts { get; } = new(StringComparer.Ordinal);
        public int TotalReads { get; private set; }

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TotalReads++;
            PathReadCounts.TryGetValue(resourceKey, out var count);
            count++;
            PathReadCounts[resourceKey] = count;
            return Task.FromResult(Reader?.Invoke(resourceKey, count));
        }

        public Task<LeagueWriteResult?> ExecuteAsync(LeagueWriteCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult(WriteResult);
        }
    }

    private sealed class FakeGameflow : ILeagueGameflowObservationSource
    {
        public LeagueGameflowSnapshot? Current { get; private set; }

        public event EventHandler<LeagueGameflowChangedEventArgs>? Changed;
        public event EventHandler<LeagueGameflowChangedEventArgs>? Observed;

        public void Publish(LeagueGameflowSnapshot snapshot)
        {
            var previous = Current;
            Current = snapshot;
            var args = new LeagueGameflowChangedEventArgs(previous, snapshot);
            Changed?.Invoke(this, args);
            Observed?.Invoke(this, args);
        }
    }

    private sealed class FakeBenchService : ILeagueBenchQuickPickService
    {
        public LeagueBenchQuickPickState State { get; set; } = LeagueBenchQuickPickState.Unavailable;

        public Task<LeagueBenchQuickPickState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(State);
        }

        public void SetSwapRoute(LeagueBenchSwapRoute route) { }

        public Task<IReadOnlyDictionary<int, LeagueChampionIdentity>> LoadChampionIdentitiesAsync(
            IReadOnlyCollection<int> championIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, LeagueChampionIdentity>>(
                new Dictionary<int, LeagueChampionIdentity>());

        public Task<byte[]?> LoadChampionIconAsync(int championId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task<LeagueBenchSwapResult> TrySwapAsync(int championId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeagueBenchSwapResult(
                LeagueBenchSwapStatus.SessionUnavailable,
                championId,
                0,
                0));
    }
}
