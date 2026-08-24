using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FACM.League;

namespace FACM.Mayhem
{
    internal static class MayhemSourceSmokeTest
    {
        public static int Run()
        {
            try
            {
                LeaguePublicDataTransport.ValidateForSmokeTest();
                ValidateRichAugmentFixture();

                var noLeagueClient = new NoLeagueClientApi();
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
                {
                    var result = OpggMayhemService.QueryAsync("yasuo", cancellation.Token).GetAwaiter().GetResult();
                    if (result == null) throw new InvalidOperationException("Mayhem query returned null.");
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) throw new InvalidOperationException(result.ErrorMessage);
                    if (string.IsNullOrWhiteSpace(result.ChampionName)) throw new InvalidOperationException("Champion name is missing.");
                    if (string.IsNullOrWhiteSpace(result.Patch)) throw new InvalidOperationException("Patch is missing.");
                    if (string.IsNullOrWhiteSpace(result.Tier)) throw new InvalidOperationException("Tier is missing.");
                    if (!result.WinRate.HasValue) throw new InvalidOperationException("Win rate is missing.");
                    if (!result.Rank.HasValue) throw new InvalidOperationException("Rank is missing.");
                    if (string.IsNullOrWhiteSpace(result.SkillOrder) || result.SkillOrder.IndexOf("Q", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Skill order is missing.");
                    if (result.CoreItems == null || result.CoreItems.Count < 3)
                        throw new InvalidOperationException("Core items are incomplete.");
                    if (result.TopTen == null || result.TopTen.Count < 10)
                        throw new InvalidOperationException("Top-ten ranking is incomplete.");

                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        RiotGameDataService.EnrichAsync(result, noLeagueClient, cancellation.Token).GetAwaiter().GetResult();
                        var skillsReady = result.SkillIconUrls != null &&
                                          result.SkillIconUrls.Count >= 4 &&
                                          new[] { "Q", "W", "E", "R" }.All(key =>
                                              result.SkillIconUrls.ContainsKey(key) &&
                                              !string.IsNullOrWhiteSpace(result.SkillIconUrls[key]));
                        if (skillsReady) break;
                        if (attempt < 2) Thread.Sleep(450);
                    }

                    var baseProbe = new MayhemChampionResult
                    {
                        ChampionName = "萨勒芬妮",
                        ChampionSlug = "seraphine",
                        Patch = result.Patch,
                        BalanceSummary = "Mayhem probe"
                    };
                    OpggAramBaseBalanceService.EnrichAsync(baseProbe, cancellation.Token).GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(baseProbe.BaseBalanceStatus) ||
                        string.Equals(baseProbe.BaseBalanceStatus, "unavailable", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Live Seraphine base ARAM balance source is unavailable or no longer parseable. " +
                            "status=" + (baseProbe.BaseBalanceStatus ?? "<null>") +
                            "; error=" + (baseProbe.BaseBalanceErrorClass ?? "<null>"));
                    if (!string.Equals(baseProbe.BaseBalanceStatus, "syncing", StringComparison.OrdinalIgnoreCase) && !baseProbe.BaseBalanceComplete)
                        throw new InvalidOperationException(
                            "Live Seraphine base ARAM balance source returned a non-complete state. " +
                            "status=" + baseProbe.BaseBalanceStatus +
                            "; error=" + (baseProbe.BaseBalanceErrorClass ?? "<null>"));
                    if (string.IsNullOrWhiteSpace(baseProbe.BaseBalanceSummary) ||
                        baseProbe.BalanceSummary.IndexOf("基础 ARAM", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("Base ARAM balance was not composed into the card summary.");

                    MayhemRankedAugmentService.EnrichAsync(result, cancellation.Token).GetAwaiter().GetResult();

                    if (string.IsNullOrWhiteSpace(result.ChampionIconUrl))
                        throw new InvalidOperationException("Champion image URL is missing.");
                    if (result.SkillIconUrls == null ||
                        result.SkillIconUrls.Count < 4 ||
                        !new[] { "Q", "W", "E", "R" }.All(key =>
                            result.SkillIconUrls.ContainsKey(key) &&
                            !string.IsNullOrWhiteSpace(result.SkillIconUrls[key])))
                        throw new InvalidOperationException("Skill image URLs are incomplete after retries.");
                    if (result.TopTen.Count(item => !string.IsNullOrWhiteSpace(item.IconUrl)) < 8)
                        throw new InvalidOperationException("Top-ten champion image URLs are incomplete.");
                    if (result.Augments == null || result.Augments.Count < 5 || result.Augments.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("Ranked augment names are incomplete.");
                    if (result.AugmentRows == null || result.AugmentRows.Count < 5)
                        throw new InvalidOperationException("Rich ranked augment rows are incomplete.");

                    // The current ARAM Mayhem page can expose the full augment catalog while omitting
                    // per-augment performance/popularity. In that state FACM must keep the rich list
                    // and deliberately show no inferred decision route rather than inventing statistics.
                    var hasDecisionStats = result.AugmentRows.Any(row => row != null && (row.WinRate.HasValue || row.PickRate.HasValue));
                    if (hasDecisionStats && (result.AugmentRoutes == null || result.AugmentRoutes.Count < 3))
                        throw new InvalidOperationException("Augment decision routes are incomplete despite available statistics.");
                    if (!hasDecisionStats && result.AugmentRoutes != null && result.AugmentRoutes.Count != 0)
                        throw new InvalidOperationException("Augment decision routes must stay empty when live statistics are absent.");

                    if (result.AugmentIconUrls == null || result.AugmentIconUrls.Count < 5 || result.AugmentIconUrls.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("Ranked augment image URLs are incomplete.");

                    using (var image = MayhemCardRenderer.RenderForSmokeTest(result))
                    {
                        if (image.Width != MayhemCardRenderer.CardWidth || image.Height != MayhemCardRenderer.CardHeight)
                            throw new InvalidOperationException("Mayhem image card dimensions are invalid.");
                        using (var stream = new MemoryStream())
                        {
                            image.Save(stream, ImageFormat.Png);
                            if (stream.Length < 20000)
                                throw new InvalidOperationException("Mayhem image card PNG is unexpectedly empty.");
                        }
                    }
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 5;
            }
        }

        private static void ValidateRichAugmentFixture()
        {
            const string html = "<script>{\"augments\":[" +
                "{\"id\":\"1001\",\"rank\":1,\"name\":\"测试棱彩\",\"rarity\":\"prismatic\",\"performance\":0.6123,\"popular\":0.2234,\"games\":12345,\"description\":\"造成额外伤害\",\"largeIcon\":\"https://raw.communitydragon.org/latest/game/assets/test.png\"}," +
                "{\"id\":\"1002\",\"rank\":2,\"name\":\"测试黄金\",\"rarity\":\"gold\",\"performance\":57.2,\"popular\":19.8,\"sampleCount\":8888,\"description\":\"获得额外属性\",\"smallIcon\":\"https://raw.communitydragon.org/latest/game/assets/test2.png\"}," +
                "{\"id\":\"1003\",\"rank\":3,\"name\":\"测试白银\",\"rarity\":\"silver\",\"performance\":0.544,\"popular\":0.31,\"totalGames\":6000,\"description\":\"提高容错\",\"icon\":\"https://raw.communitydragon.org/latest/game/assets/test3.png\"}]} </script>";
            var rows = MayhemRankedAugmentService.ParseOpggRowsForSmokeTest(html);
            if (rows.Count != 3) throw new InvalidOperationException("Rich augment fixture did not parse three rows.");
            if (rows[0].Rarity != "棱彩" || !rows[0].WinRate.HasValue || Math.Abs(rows[0].WinRate.Value - 61.23) > 0.01)
                throw new InvalidOperationException("Rich augment rarity or percentage normalization failed.");
            if (string.IsNullOrWhiteSpace(rows[0].IconUrl) || string.IsNullOrWhiteSpace(rows[1].IconUrl) || rows[1].Games != 8888 || rows[2].Games != 6000)
                throw new InvalidOperationException("Rich augment OP.GG icon or sample aliases failed.");
            var result = new MayhemChampionResult();
            if (MayhemRankedAugmentService.ApplyFromHtmlForSmokeTest(result, html) != 3)
                throw new InvalidOperationException("Rich augment fixture was not applied.");
            if (result.AugmentRoutes.Count != 3 || result.Augments[0] != "测试棱彩")
                throw new InvalidOperationException("Rich augment decision routes or names are invalid.");
        }

        private sealed class NoLeagueClientApi : ILeagueClientApi
        {
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}