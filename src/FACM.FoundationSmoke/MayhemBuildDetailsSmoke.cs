using System.Net;
using System.Text;
using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemBuildDetailsSmoke
{
    public static async Task RunAsync()
    {
        ValidateHtmlProjection();
        ValidateLegacyProjection();
        await ValidateTypedBuildRequestAsync();
        await ValidateExistingDetailsSkipNetworkAsync();
    }

    private static void ValidateHtmlProjection()
    {
        var result = new MayhemChampionResult { ChampionSlug = "ashe" };
        MayhemBuildDetailsService.ApplyHtmlForSmoke(result, DetailedFixture());

        Require(result.CoreBuilds.Count == 2,
            "Detailed build must preserve the FACM 3.5 maximum of two core paths.");
        Require(result.CoreBuilds[0].Items.Count == 5 && result.CoreBuilds[1].Items.Count == 3,
            "Detailed core build item limits changed from 3.5 behavior.");
        Require(result.CoreItems.SequenceEqual(new[] { "Core A", "Core B", "Core C", "Core D", "Core E" }),
            "Legacy CoreItems projection must follow the first rich build path.");
        Require(result.StarterItems.Count == 3 && result.BootItems.Count == 1,
            "Starter/boot limits changed from 3.5 behavior.");
        Require(result.SummonerSpells.Count == 2 &&
                result.SummonerSpells.All(item => item.IconUrl.Contains("/spell/Summoner", StringComparison.OrdinalIgnoreCase)),
            "Detailed build did not keep exactly two summoner spells.");
        Require(result.SkillPriority.Select(item => item.Key).SequenceEqual(new[] { "Q", "W", "E" }),
            "Detailed build skill priority must keep three distinct non-R skills in source order.");
    }

    private static void ValidateLegacyProjection()
    {
        var result = new MayhemChampionResult
        {
            CoreItems = ["Legacy A", "Legacy B"],
            CoreItemIconUrls = ["https://cdn.test/item/a.png", "https://cdn.test/item/b.png"],
            SkillOrder = "Q → W → E → R → Q → W"
        };
        MayhemBuildDetailsService.ApplyHtmlForSmoke(result, string.Empty);

        Require(result.CoreBuilds.Count == 1 && result.CoreBuilds[0].Items.Count == 2,
            "Legacy CoreItems were not projected into rich build path #1.");
        Require(result.SkillPriority.Select(item => item.Key).SequenceEqual(new[] { "Q", "W", "E" }),
            "Legacy skill-order fallback must ignore R and keep the first three distinct basic skills.");
    }

    private static async Task ValidateTypedBuildRequestAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new RouteHandler(request =>
            {
                Require(request.RequestUri!.AbsolutePath.EndsWith("/ashe/build", StringComparison.OrdinalIgnoreCase),
                    "Detailed build attempted a resource other than typed MayhemBuild.");
                return Html(DetailedFixture());
            });
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var service = new MayhemBuildDetailsService(transport);
            var result = new MayhemChampionResult { ChampionSlug = "ashe" };
            await service.EnrichAsync(result);

            Require(handler.Calls == 1, "Detailed build must use exactly one typed public-data request.");
            Require(result.BuildSourceRoute == "direct" && !result.BuildSourceStale && result.BuildSourceStatus == "ok",
                "Detailed build source route/status was not preserved.");
            Require(result.CoreBuilds.Count == 2 && result.StarterItems.Count == 3,
                "Typed detailed build enrichment did not populate the result contract.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task ValidateExistingDetailsSkipNetworkAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-build-skip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new RouteHandler(_ => throw new InvalidOperationException("Network should not be used."));
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var service = new MayhemBuildDetailsService(transport);
            var result = new MayhemChampionResult
            {
                ChampionSlug = "ashe",
                CoreBuilds = [new MayhemBuildPath { Rank = 1, Items = [new MayhemBuildItem { Name = "Already Here" }] }],
                SkillOrder = "E W Q R"
            };
            await service.EnrichAsync(result);

            Require(handler.Calls == 0,
                "Existing detailed build data must short-circuit before public-data transport.");
            Require(result.SkillPriority.Select(item => item.Key).SequenceEqual(new[] { "E", "W", "Q" }),
                "Existing detailed build must still receive the legacy skill-priority fallback.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string DetailedFixture() => """
        core_items_0
        {"metaId":1001,"src":"https://cdn.test/item/1001.png","alt":"Core A"}
        {"metaId":1002,"src":"https://cdn.test/item/1002.png","alt":"Core B"}
        {"metaId":1003,"src":"https://cdn.test/item/1003.png","alt":"Core C"}
        {"metaId":1004,"src":"https://cdn.test/item/1004.png","alt":"Core D"}
        {"metaId":1005,"src":"https://cdn.test/item/1005.png","alt":"Core E"}
        core_items_1
        {"metaId":2001,"src":"https://cdn.test/item/2001.png","alt":"Alt A"}
        {"metaId":2002,"src":"https://cdn.test/item/2002.png","alt":"Alt B"}
        {"metaId":2003,"src":"https://cdn.test/item/2003.png","alt":"Alt C"}
        starter_items_0
        {"metaId":3001,"src":"https://cdn.test/item/3001.png","alt":"Starter A"}
        {"metaId":3002,"src":"https://cdn.test/item/3002.png","alt":"Starter B"}
        {"metaId":3003,"src":"https://cdn.test/item/3003.png","alt":"Starter C"}
        boots_0
        {"metaId":4001,"src":"https://cdn.test/item/4001.png","alt":"Boot A"}
        SummonerSpells Table
        <img src="https://cdn.test/spell/SummonerFlash.png" alt="Flash">
        <img alt="Mark" src="https://cdn.test/spell/SummonerSnowball.png">
        SkillOrder Table
        <img alt="Volley" src="https://cdn.test/spell/ashe-w.png"><strong>Q</strong>
        <img src="https://cdn.test/spell/ashe-q.png" alt="Focus"><strong>W</strong>
        <img alt="Hawk" src="https://cdn.test/spell/ashe-e.png"><strong>E</strong>
        <img alt="Arrow" src="https://cdn.test/spell/ashe-r.png"><strong>R</strong>
        """;

    private static HttpResponseMessage Html(string value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(value))
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
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
