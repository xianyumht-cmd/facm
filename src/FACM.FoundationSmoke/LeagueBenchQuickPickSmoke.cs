using System.Text;
using FACM.Core.League;
using FACM.Infrastructure.League;

internal static class LeagueBenchQuickPickSmoke
{
    public static async Task RunAsync()
    {
        ValidateWriteAllowlist();
        ValidatePollingPolicy();
        ValidateLegacyParser();
        ValidateChampionIdentityParser();
        ValidateBenchSwapStripPresentation();
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
        Require(catalog[22].Name == "寒冰射手" && catalog[22].IconPath.EndsWith("ashe.png", StringComparison.Ordinal),
            "Preferred localized champion name or portrait was not selected.");
        Require(catalog[44].Name == "塔里克", "Champion name fallback field was not selected.");
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

        var dismissal = new LeagueBenchContextDismissal();
        dismissal.BeginNewContext();
        Require(dismissal.CanAutoShow(true), "A new actionable Bench context must auto-show.");
        dismissal.DismissCurrentContext();
        Require(!dismissal.CanAutoShow(true), "Manual strip dismissal must hold for the current context.");
        dismissal.ResetForMaterialCandidateChange();
        Require(dismissal.CanAutoShow(true), "A material candidate change must clear dismissal.");
        dismissal.DismissCurrentContext();
        dismissal.BeginNewContext();
        Require(dismissal.CanAutoShow(true), "A new Champ Select context must reopen the strip.");
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
}
