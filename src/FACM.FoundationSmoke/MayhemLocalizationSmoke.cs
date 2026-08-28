using System.Net;
using System.Text;
using FACM.Core.League;
using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemLocalizationSmoke
{
    public static async Task RunAsync()
    {
        ValidateFixtureProjection();
        await ValidateLcuFirstAndCacheAsync();
        await ValidateCommunityDragonFallbackAsync();
    }

    private static void ValidateFixtureProjection()
    {
        var result = SeedResult();
        MayhemDecisionLocalizationService.ApplyFixtureForSmoke(
            result,
            ItemsJson(),
            AugmentsJson(),
            SummonersJson(),
            ChampionSummaryJson(),
            ChampionDetailJson());

        Require(result.CoreBuilds[0].Items[0].Name == "无尽之刃" &&
                result.CoreBuilds[0].Items[0].IconUrl.StartsWith("lcu:/lol-game-data/assets/", StringComparison.Ordinal),
            "Localized item name/icon projection changed from 3.5 behavior.");
        Require(result.AugmentRows[0].Name == "冰霜幽灵" &&
                result.AugmentRows[0].Description == "命中后减速敌人" &&
                result.AugmentRows[0].IconUrl.StartsWith("lcu:/lol-game-data/assets/", StringComparison.Ordinal),
            "Localized augment name/description/icon projection changed.");
        Require(result.AugmentRoutes[0].AugmentName == "冰霜幽灵",
            "Decision route did not follow the localized augment rename.");
        Require(result.SummonerSpells[0].Name == "闪现" &&
                result.SummonerSpells[0].IconUrl.StartsWith("lcu:/lol-game-data/assets/", StringComparison.Ordinal),
            "Localized summoner spell projection changed.");
        Require(result.ChampionIconUrl.StartsWith("lcu:/lol-game-data/assets/", StringComparison.Ordinal),
            "Champion portrait was not projected to an LCU asset reference.");
        Require(result.SkillPriority[0].Name == "射手的专注" &&
                result.SkillPriority[0].IconUrl.StartsWith("lcu:/lol-game-data/assets/", StringComparison.Ordinal) &&
                result.SkillIconUrls.TryGetValue("Q", out var q) && q.StartsWith("lcu:/", StringComparison.Ordinal),
            "Localized champion skill name/icon projection changed.");
        Require(result.CoreItems.SequenceEqual(new[] { "无尽之刃" }) && result.Augments.SequenceEqual(new[] { "冰霜幽灵" }),
            "Legacy item/augment lists were not reprojected after localization.");
    }

    private static async Task ValidateLcuFirstAndCacheAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-l10n-lcu-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 8, 28, 11, 30, 0, TimeSpan.Zero);
        try
        {
            var league = new FakeLeagueReadGateway(LocalResources());
            var publicHandler = new CountingHandler(_ =>
                throw new InvalidOperationException("CommunityDragon must not be used when LCU data is available."));
            using var publicData = new MayhemCachedPublicDataTransport(root, publicHandler, () => now);
            var service = new MayhemDecisionLocalizationService(league, publicData, () => now);

            var first = SeedResult();
            await service.EnrichAsync(first);
            Require(league.Calls == 5,
                "Localization must read four fixed LCU catalogs plus one champion detail from the shared gateway.");
            Require(publicHandler.Calls == 0,
                "Localization did not preserve LCU-first behavior.");
            Require(league.Paths.All(path => path.StartsWith("/lol-game-data/assets/v1/", StringComparison.Ordinal)),
                "Localization attempted an LCU path outside the fixed game-data asset root.");

            var second = SeedResult();
            await service.EnrichAsync(second);
            Require(league.Calls == 5 && publicHandler.Calls == 0,
                "20-minute localization cache did not short-circuit repeated LCU/public reads.");
            Require(second.AugmentRows[0].Name == "冰霜幽灵" && second.SkillPriority[0].Name == "射手的专注",
                "Cached localized catalogs were not reapplied correctly.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task ValidateCommunityDragonFallbackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-l10n-public-" + Guid.NewGuid().ToString("N"));
        try
        {
            var league = new FakeLeagueReadGateway(new Dictionary<string, byte[]>());
            var publicHandler = new CountingHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/items.json", StringComparison.OrdinalIgnoreCase)) return Json(ItemsJson());
                if (path.EndsWith("/cherry-augments.json", StringComparison.OrdinalIgnoreCase)) return Json(AugmentsJson());
                if (path.EndsWith("/summoner-spells.json", StringComparison.OrdinalIgnoreCase)) return Json(SummonersJson());
                if (path.EndsWith("/champion-summary.json", StringComparison.OrdinalIgnoreCase)) return Json(ChampionSummaryJson());
                if (path.EndsWith("/champions/22.json", StringComparison.OrdinalIgnoreCase)) return Json(ChampionDetailJson());
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            using var publicData = new MayhemCachedPublicDataTransport(root, publicHandler);
            var service = new MayhemDecisionLocalizationService(league, publicData);
            var result = SeedResult();
            await service.EnrichAsync(result);

            Require(league.Calls == 5,
                "CommunityDragon fallback must only happen after each fixed LCU resource was attempted.");
            Require(publicHandler.Calls == 5,
                "CommunityDragon fallback must request the same five typed localization resources.");
            Require(result.CoreBuilds[0].Items[0].Name == "无尽之刃" &&
                    result.AugmentRows[0].Name == "冰霜幽灵" &&
                    result.SummonerSpells[0].Name == "闪现",
                "Typed CommunityDragon fallback did not produce the same localization projection as LCU.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static MayhemChampionResult SeedResult() => new()
    {
        ChampionSlug = "ashe",
        ChampionName = "Ashe",
        CoreBuilds =
        [
            new MayhemBuildPath
            {
                Rank = 1,
                Items = [new MayhemBuildItem { Id = "3031", Name = "Infinity Edge", IconUrl = "https://cdn.test/item/3031.png" }]
            }
        ],
        SummonerSpells = [new MayhemBuildItem { Name = "Flash", IconUrl = "https://cdn.test/spell/SummonerFlash.png" }],
        SkillPriority = [new MayhemSkillPriority { Key = "Q", Name = "Q" }],
        AugmentRows =
        [
            new MayhemAugmentRow { Id = "77", Name = "Frost Wraith", Slug = "frost-wraith", IconUrl = "https://cdn.test/frost-wraith.png" }
        ],
        AugmentRoutes = [new MayhemDecisionRoute { Title = "稳定赢法", AugmentName = "Frost Wraith" }]
    };

    private static Dictionary<string, byte[]> LocalResources() => new(StringComparer.Ordinal)
    {
        ["/lol-game-data/assets/v1/items.json"] = Bytes(ItemsJson()),
        ["/lol-game-data/assets/v1/cherry-augments.json"] = Bytes(AugmentsJson()),
        ["/lol-game-data/assets/v1/summoner-spells.json"] = Bytes(SummonersJson()),
        ["/lol-game-data/assets/v1/champion-summary.json"] = Bytes(ChampionSummaryJson()),
        ["/lol-game-data/assets/v1/champions/22.json"] = Bytes(ChampionDetailJson())
    };

    private static string ItemsJson() => """
        [{"id":3031,"name":"Infinity Edge","nameTRA":"无尽之刃","iconPath":"/lol-game-data/assets/v1/items/icons2d/3031.png"}]
        """;

    private static string AugmentsJson() => """
        [{"id":77,"apiName":"frost-wraith","name":"Frost Wraith","nameTRA":"冰霜幽灵","descTRA":"<b>命中后减速敌人</b>","augmentSmallIconPath":"/lol-game-data/assets/v1/cherry/augments/frost-wraith.png"}]
        """;

    private static string SummonersJson() => """
        [{"apiName":"SummonerFlash","name":"Flash","nameTRA":"闪现","iconPath":"/lol-game-data/assets/v1/summoner-spells/icons2d/SummonerFlash.png"}]
        """;

    private static string ChampionSummaryJson() => """
        [{"id":22,"alias":"Ashe","name":"Ashe","nameTRA":"艾希","squarePortraitPath":"/lol-game-data/assets/v1/champion-icons/22.png"}]
        """;

    private static string ChampionDetailJson() => """
        {"spells":[{"spellKey":"Q","name":"Ranger's Focus","nameTRA":"射手的专注","abilityIconPath":"/lol-game-data/assets/v1/champion-spells/ashe-q.png"}]}
        """;

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Bytes(value))
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeLeagueReadGateway(IReadOnlyDictionary<string, byte[]> resources) : ILeagueReadGateway
    {
        public int Calls { get; private set; }
        public List<string> Paths { get; } = [];

        public Task<byte[]?> TryGetBytesAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Paths.Add(resourceKey);
            return Task.FromResult(resources.TryGetValue(resourceKey, out var bytes) ? bytes : null);
        }
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(route(request));
        }
    }
}
