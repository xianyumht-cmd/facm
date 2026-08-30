using System.Text;
using FACM.Core.League;
using FACM.Core.Performance;
using FACM.Infrastructure.League;

internal static class LeagueBuildAdvisorSmoke
{
    public static async Task RunAsync()
    {
        TestModeAndPositionRules();
        TestRankedPositionSelection();
        await TestRecommendationCacheAndInGamePolicyAsync();
    }

    private static void TestModeAndPositionRules()
    {
        Equal("aram", LeagueBuildAdvisorService.ResolveOpggMode(450, "ARAM"), "advisor ARAM mode");
        Equal("urf", LeagueBuildAdvisorService.ResolveOpggMode(0, "URF"), "advisor URF mode");
        Equal("ranked", LeagueBuildAdvisorService.ResolveOpggMode(420, "CLASSIC"), "advisor ranked mode");
        Equal(string.Empty, LeagueBuildAdvisorService.ResolveOpggMode(1700, "CHERRY"), "advisor unsupported mode");

        Equal("mid", LeagueBuildAdvisorService.ResolveOpggPosition("MIDDLE", "ranked"), "advisor mid position");
        Equal("adc", LeagueBuildAdvisorService.ResolveOpggPosition("BOTTOM", "ranked"), "advisor adc position");
        Equal("support", LeagueBuildAdvisorService.ResolveOpggPosition("UTILITY", "ranked"), "advisor support position");
        Equal("none", LeagueBuildAdvisorService.ResolveOpggPosition("MIDDLE", "aram"), "advisor non-ranked position");
        Equal("all", LeagueBuildAdvisorService.ResolveOpggPosition(null, "ranked"), "advisor unresolved ranked position");

        Equal(
            "/api/global/champions/ranked/99/mid?tier=all&version=14.17",
            LeagueBuildAdvisorService.BuildPath(99, "ranked", "mid", "14.17"),
            "advisor build path");
    }

    private static void TestRankedPositionSelection()
    {
        var payload = Utf8("""
            {"data":[{"id":99,"positions":[{"name":"MID","stats":{"role_rate":0.3,"play":200}},{"name":"TOP","stats":{"role_rate":0.3,"play":300}},{"name":"SUPPORT","stats":{"role_rate":0.2,"play":500}}]}]}
            """);
        Equal("top", LeagueBuildAdvisorService.ParsePrimaryRankedPosition(payload, 99), "advisor ranked-position tie break");
    }

    private static async Task TestRecommendationCacheAndInGamePolicyAsync()
    {
        var champSelect = CreateLive("ChampSelect");
        var workbench = new FakeWorkbenchDataSource { Live = champSelect };
        var lcu = new FakeLeagueReadGateway();
        lcu.Responses[LeagueBuildAdvisorService.ChampionSummaryPath] = Utf8("[{\"id\":99,\"name\":\"Lux\"},{\"id\":55,\"name\":\"Katarina\"}]");
        lcu.Responses[LeagueBuildAdvisorService.ItemsPath] = Utf8("[{\"id\":1056,\"name\":\"Doran's Ring\"},{\"id\":3020,\"name\":\"Sorcerer's Shoes\"},{\"id\":3089,\"name\":\"Rabadon's Deathcap\"}]");
        lcu.Responses[LeagueBuildAdvisorService.SummonerSpellsPath] = Utf8("[{\"id\":4,\"name\":\"Flash\"},{\"id\":14,\"name\":\"Ignite\"}]");
        lcu.Responses[LeagueBuildAdvisorService.PerksPath] = Utf8("[{\"id\":8112,\"name\":\"Electrocute\"},{\"id\":8139,\"name\":\"Taste of Blood\"}]");

        var opgg = new FakeOpggBuildSource();
        opgg.Responses["/api/global/champions/ranked/versions"] = Utf8("{\"data\":[\"14.17\"]}");
        var buildPath = LeagueBuildAdvisorService.BuildPath(99, "ranked", "mid", "14.17");
        opgg.Responses[buildPath] = Utf8("""
            {
              "data": {
                "summary": {"average_stats":{"win_rate":0.52,"pick_rate":0.11,"ban_rate":0.04,"tier_data":{"tier":1,"rank":3}}},
                "summoner_spells": [{"ids":[4,14],"pick_rate":0.4,"play":100}],
                "runes": [{"builds":[{"primary_rune_ids":[8112],"secondary_rune_ids":[8139],"pick_rate":0.3,"play":80}]}],
                "starter_items": [{"ids":[1056],"pick_rate":0.5,"play":90}],
                "boots": [{"ids":[3020],"pick_rate":0.6,"play":120}],
                "core_items": [{"ids":[3089],"pick_rate":0.2,"play":70}],
                "skill_masteries": [{"ids":["Q","E","W"],"play":60}],
                "counters": [{"champion_id":55,"play":25}]
              }
            }
            """);

        var performance = new PerformanceBudgetProvider();
        using var service = new LeagueBuildAdvisorService(workbench, lcu, performance, opgg);

        var first = await service.RefreshAsync();
        Equal(LeagueBuildAdvisorState.Ready, first.State, "advisor first ready state");
        Equal("Lux", first.ChampionName, "advisor champion catalog");
        Equal("ranked", first.Mode, "advisor first mode");
        Equal("mid", first.Position, "advisor first position");
        Equal("14.17", first.Version, "advisor version");
        True(!first.FromCache, "advisor first result must not be cache");
        True(first.Recommendation is not null, "advisor recommendation present");
        Equal("T1", first.Recommendation?.Tier, "advisor tier");
        Equal(7, first.Recommendation?.Rows.Count ?? 0, "advisor recommendation rows");
        True(first.Recommendation!.Rows.Any(row => row.Category == "summoner-spells" && row.Recommendation.Contains("Flash", StringComparison.Ordinal)), "advisor spell names");
        True(first.Recommendation.Rows.Any(row => row.Category == "runes" && row.Recommendation.Contains("Electrocute", StringComparison.Ordinal)), "advisor rune names");
        True(first.Recommendation.Rows.Any(row => row.Category == "core-items" && row.Recommendation.Contains("Rabadon's Deathcap", StringComparison.Ordinal)), "advisor item names");
        Equal(4, lcu.Calls, "advisor catalog read count");
        Equal(2, opgg.Paths.Count, "advisor first OP.GG requests");

        var cached = await service.RefreshAsync();
        Equal(LeagueBuildAdvisorState.Ready, cached.State, "advisor cached ready state");
        True(cached.FromCache, "advisor second result must use build cache");
        Equal(4, lcu.Calls, "advisor catalog cache suppresses LCU reload");
        Equal(2, opgg.Paths.Count, "advisor version/build cache suppresses OP.GG reload");

        workbench.Live = CreateLive("InProgress");
        var inGame = await service.RefreshAsync();
        Equal(LeagueBuildAdvisorState.InGameCache, inGame.State, "advisor in-game cache state");
        True(inGame.FromCache, "advisor in-game recommendation comes from cache");
        Equal(2, opgg.Paths.Count, "advisor in-game must not call OP.GG");
        Equal(4, lcu.Calls, "advisor in-game must not load LCU catalog");

        var noCacheWorkbench = new FakeWorkbenchDataSource { Live = CreateLive("InProgress") };
        var noCacheLcu = new FakeLeagueReadGateway();
        var noCacheOpgg = new FakeOpggBuildSource();
        using var noCacheService = new LeagueBuildAdvisorService(noCacheWorkbench, noCacheLcu, new PerformanceBudgetProvider(), noCacheOpgg);
        var noCache = await noCacheService.RefreshAsync();
        Equal(LeagueBuildAdvisorState.InGameNoCache, noCache.State, "advisor in-game no-cache state");
        Equal(0, noCacheLcu.Calls, "advisor cold in-game must not read LCU catalogs");
        Equal(0, noCacheOpgg.Paths.Count, "advisor cold in-game must not call OP.GG");
    }

    private static LeagueWorkbenchLiveSnapshot CreateLive(string phase)
    {
        var player = new LeagueWorkbenchLivePlayer(
            "ally",
            1,
            true,
            "PUUID-1",
            42,
            "FACM",
            "CN1",
            "FACM",
            "MIDDLE",
            "SOLO",
            99,
            0,
            4,
            14);
        return new LeagueWorkbenchLiveSnapshot(
            LeagueWorkbenchDataState.Ready,
            phase,
            303,
            new LeagueWorkbenchQueue(420, "Ranked Solo", "CLASSIC"),
            11,
            "Summoner's Rift",
            1,
            phase == "ChampSelect" ? "BAN_PICK" : string.Empty,
            phase == "ChampSelect" ? 12000 : 0,
            phase == "ChampSelect" ? "pick" : string.Empty,
            99,
            false,
            LeagueBenchSwapRoute.Legacy,
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            [player],
            "ready",
            DateTimeOffset.Parse("2026-08-28T08:00:00Z"));
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + " failed.");
    }

    private sealed class FakeWorkbenchDataSource : ILeagueWorkbenchDataSource
    {
        public LeagueWorkbenchLiveSnapshot Live { get; set; } = LeagueWorkbenchLiveSnapshot.Unavailable(string.Empty, "unset");

        public Task<LeagueWorkbenchDashboardSnapshot> LoadDashboardAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LeagueWorkbenchDashboardSnapshot.Unavailable("not-used"));
        }

        public Task<LeagueWorkbenchPlayerSnapshot> LoadCurrentPlayerAsync(
            int startIndex = 0,
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LeagueWorkbenchPlayerSnapshot.Unavailable("not-used"));
        }

        public Task<LeagueWorkbenchLiveSnapshot> LoadLiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Live);
        }
    }

    private sealed class FakeLeagueReadGateway : ILeagueReadGateway
    {
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);
        public int Calls { get; private set; }

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Responses.TryGetValue(resourceKey, out var value) ? value : null);
        }
    }

    private sealed class FakeOpggBuildSource : IOpggBuildSource
    {
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);
        public List<string> Paths { get; } = [];

        public Task<byte[]?> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(path);
            return Task.FromResult(Responses.TryGetValue(path, out var value) ? value : null);
        }
    }
}
