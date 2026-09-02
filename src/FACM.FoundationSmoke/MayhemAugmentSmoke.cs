using System.Net;
using System.Text;
using FACM.Core.Mayhem;
using FACM.Infrastructure.Mayhem;

internal static class MayhemAugmentSmoke
{
    public static async Task RunAsync()
    {
        ValidateDecisionPolicy();
        ValidateRichParserRequiresIcon();
        ValidateFullRankedProjection();
        ValidateLegacyProjection();
        await ValidateTypedRichEnrichmentAsync();
        await ValidateTypedLegacyFallbackAsync();
    }

    private static void ValidateDecisionPolicy()
    {
        var rows = new[]
        {
            Row("A", 54, 20),
            Row("B", 58, 3),
            Row("C", 50, 35)
        };
        var routes = MayhemAugmentDecisionPolicy.BuildRoutes(rows);
        Require(routes.Count == 3, "Augment decision policy must keep three distinct 3.5 routes when data allows it.");
        Require(routes[0].Title == "稳定赢法" && routes[0].AugmentName == "C",
            "Stable route lost the FACM 3.5 72/28 win/popularity weighting.");
        Require(routes[1].Title == "高上限玩法" && routes[1].AugmentName == "B",
            "High-win route did not choose the highest remaining win-rate augment.");
        Require(routes[2].Title == "热门好上手" && routes[2].AugmentName == "A",
            "Popular route did not choose the highest remaining pick-rate augment after route de-duplication.");
        Require(Math.Abs(MayhemAugmentDecisionPolicy.StableScore(rows[0]) - 44.48d) < 0.0001d,
            "Stable route score no longer uses 0.72 win + 0.28 pick.");
        Require(Math.Abs(MayhemAugmentDecisionPolicy.StableScore(rows[2]) - 45.8d) < 0.0001d,
            "Stable route fixture no longer proves the weighted winner.");
    }

    private static void ValidateRichParserRequiresIcon()
    {
        const string html = """
            <script>{"augments":[
              {"name":"With Icon","performance":0.552,"popular":0.21,"games":1234,"rarity":"Prismatic","largeIcon":"https://cdn.test/a.png","description":"<b>strong</b> choice"},
              {"name":"No Icon","performance":0.62,"popular":0.5}
            ]}</script>
            """;
        var rows = MayhemAugmentEnrichmentService.ParseOpggRowsForSmoke(html);
        Require(rows.Count == 1 && rows[0].Name == "With Icon",
            "Rich augment parser must reject generic rows that do not carry a source icon.");
        Require(Math.Abs((rows[0].WinRate ?? 0) - 55.2d) < 0.001d &&
                Math.Abs((rows[0].PickRate ?? 0) - 21d) < 0.001d,
            "Rich augment percentage normalization changed from 3.5 behavior.");
        Require(rows[0].Rarity == "棱彩" && rows[0].Games == 1234,
            "Rich augment rarity/sample projection changed.");
        Require(rows[0].Description == "strong choice",
            "Rich augment description sanitization changed.");

        const string numericRarityHtml = """
            <script>self.__next_f.push([1,"59:[\"$\",\"$L5a\",null,{\"data\":[{\"name\":\"Silver\",\"rarity\":1,\"largeIcon\":\"https://cdn.test/silver.png\"},{\"name\":\"Gold\",\"rarity\":4,\"largeIcon\":\"https://cdn.test/gold.png\"},{\"name\":\"Prismatic\",\"rarity\":8,\"largeIcon\":\"https://cdn.test/prismatic.png\"}]}\"]"])</script>
            """;
        var numericRows = MayhemAugmentEnrichmentService.ParseOpggRowsForSmoke(numericRarityHtml);
        Require(numericRows.Select(row => row.Rarity).SequenceEqual(["白银", "黄金", "棱彩"]),
            "OP.GG numeric augment rarity values 1/4/8 must map to Silver/Gold/Prismatic.");

        const string nestedEscapedHtml = """
            <script>self.__next_f.push([1,"59:[\"$\",\"$L5a\",null,{\"data\":[{\"name\":\"Nested\",\"rarity\":4,\"largeIcon\":\"https://cdn.test/nested.png\",\"tooltip\":\"颜色<font color=\\\"#ffd138\\\">黄金</font>\"}]}]\"]"])</script>
            """;
        var nestedRows = MayhemAugmentEnrichmentService.ParseOpggRowsForSmoke(nestedEscapedHtml);
        Require(nestedRows.Count == 1 && nestedRows[0].Name == "Nested" && nestedRows[0].Rarity == "黄金",
            "OP.GG nested self.__next_f escaping must remain parseable when augment tooltips contain HTML attributes.");
    }

    private static void ValidateLegacyProjection()
    {
        const string html = """
            Best Augments for Ashe
            <a href="/augments/ice-cold"><img alt="Ice Cold" src="x"> 57.20%</a>
            <a href="/augments/fast-hands"><img alt="Fast Hands" src="x"> 53.10%</a>
            Augment Combos
            """;
        var result = new MayhemChampionResult { ChampionSlug = "ashe" };
        var count = MayhemAugmentEnrichmentService.ApplyHtmlForSmoke(result, html);
        Require(count == 2 && result.AugmentRows.Count == 2 && result.Augments.Count == 2,
            "Legacy ARAMMayhem augment fallback was not projected into the 4.0 result contract.");
        Require(result.AugmentRows.All(row => row.Rarity == "未知"),
            "Legacy fallback must not invent augment rarity.");
        Require(result.AugmentIconUrls.All(url => url.Contains("raw.communitydragon.org", StringComparison.OrdinalIgnoreCase)),
            "Legacy fallback must preserve the 3.5 Kiwi icon projection.");
    }

    private static void ValidateFullRankedProjection()
    {
        var rows = Enumerable.Range(1, 13)
            .Select(index => new MayhemAugmentRow
            {
                Id = "augment-" + index,
                Rank = index,
                Name = "强化 " + index,
                Rarity = index <= 7 ? "棱彩" : "黄金",
                IconUrl = "https://cdn.test/augment-" + index + ".png"
            })
            .Append(new MayhemAugmentRow
            {
                Id = "augment-1",
                Rank = 99,
                Name = "重复强化",
                Rarity = "棱彩",
                IconUrl = "https://cdn.test/duplicate.png"
            })
            .ToArray();

        var normalized = MayhemAutomaticGuideProjection.NormalizeAugments(rows);
        Require(normalized.Count == 13, "Automatic guide projection must retain the full ranked augment set without duplicate IDs.");
        Require(MayhemAutomaticGuideProjection.PageCount(normalized, "棱彩") == 2,
            "Prismatic augment pagination must expose the partial second page.");
        Require(MayhemAutomaticGuideProjection.Page(normalized, "棱彩", 1).Count == 1,
            "Prismatic partial page count changed.");
        Require(MayhemAutomaticGuideProjection.Page(normalized, "黄金", 0).Count == 6,
            "Gold augment page must keep the configured six-icon page size.");
        Require(MayhemAutomaticGuideProjection.PageCount(normalized, "白银") == 0,
            "Unavailable rarity must have no pages.");
        Require(MayhemAutomaticGuideProjection.IsCurrentGeneration(4, 4) &&
                !MayhemAutomaticGuideProjection.IsCurrentGeneration(3, 4),
            "Automatic guide stale-generation guard changed.");
        Require(normalized.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == normalized.Count,
            "Automatic guide projection emitted duplicate augment IDs.");
    }

    private static async Task ValidateTypedRichEnrichmentAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-rich-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new RouteHandler(request =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/ashe/augments", StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                var rows = string.Join(',', Enumerable.Range(1, 13).Select(index =>
                    "{\"id\":\"rich-" + index + "\",\"name\":\"Rich " + index + "\",\"performance\":" +
                    (50 + index) + ",\"popular\":20,\"rarity\":\"" + (index <= 7 ? "Prismatic" : "Gold") +
                    "\",\"largeIcon\":\"https://cdn.test/rich-" + index + ".png\"}"));
                var html = "<script>{\"augments\":[" + rows + "]}</script>";
                return Html(html);
            });
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var service = new MayhemAugmentEnrichmentService(transport);
            var result = new MayhemChampionResult { ChampionSlug = "ashe" };
            await service.EnrichAsync(result);

            Require(handler.Calls == 1, "Rich augment enrichment should stop after one successful typed source request.");
            Require(result.AugmentRows.Count == 13 && result.AugmentRows[0].Name == "Rich 1",
                "Typed rich augment enrichment did not preserve the full ranked result.");
            Require(result.AugmentRows.Count(row => row.Rarity == "棱彩") == 7 &&
                    result.AugmentRows.Count(row => row.Rarity == "黄金") == 6,
                "Typed rich augment enrichment changed rarity partitioning.");
            Require(result.AugmentSourceRoute == "direct" && !result.AugmentSourceStale,
                "Direct rich augment source state was not preserved.");
            Require(result.AugmentSourceUrl.EndsWith("/ashe/augments", StringComparison.OrdinalIgnoreCase),
                "Rich augment source URL was not derived from the typed resource resolver.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task ValidateTypedLegacyFallbackAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "facm4-mayhem-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new RouteHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/ashe/augments", StringComparison.OrdinalIgnoreCase))
                    return Html("<html>no rich augment array</html>");
                if (path.Contains("/build/ashe", StringComparison.OrdinalIgnoreCase))
                    return Html("Best Augments for Ashe <a href=\"/augments/fallback-one\"><img alt=\"Fallback One\"> 54.4%</a> Augment Combos");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            using var transport = new MayhemCachedPublicDataTransport(root, handler);
            var service = new MayhemAugmentEnrichmentService(transport);
            var result = new MayhemChampionResult { ChampionSlug = "ashe" };
            await service.EnrichAsync(result);

            Require(handler.Calls == 2, "Empty rich source must perform exactly one typed ranking fallback request.");
            Require(result.AugmentRows.Count == 1 && result.AugmentRows[0].Name == "Fallback One",
                "Typed ranking fallback did not populate legacy augment data.");
            Require(result.AugmentSourceUrl.Contains("arammayhem.com/build/ashe", StringComparison.OrdinalIgnoreCase),
                "Legacy fallback source did not remain the typed ARAMMayhem build resource.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static MayhemAugmentRow Row(string name, double win, double pick) => new()
    {
        Name = name,
        WinRate = win,
        PickRate = pick
    };

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
