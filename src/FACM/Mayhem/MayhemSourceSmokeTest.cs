using System;
using System.Collections.Generic;
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
                ValidateBuildDetailsFixture();
                ValidateLocalizedProjectionFixture();

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

                    var hasDecisionStats = result.AugmentRows.Any(row => row != null && (row.WinRate.HasValue || row.PickRate.HasValue));
                    if (hasDecisionStats && (result.AugmentRoutes == null || result.AugmentRoutes.Count < 3))
                        throw new InvalidOperationException("Augment decision routes are incomplete despite available statistics.");
                    if (!hasDecisionStats && result.AugmentRoutes != null && result.AugmentRoutes.Count != 0)
                        throw new InvalidOperationException("Augment decision routes must stay empty when live statistics are absent.");
                    if (result.AugmentRoutes != null &&
                        result.AugmentRoutes.Select(route => route.AugmentName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.AugmentRoutes.Count)
                        throw new InvalidOperationException("Augment decision routes must not repeat the same augment.");

                    if (result.AugmentIconUrls == null || result.AugmentIconUrls.Count < 5 || result.AugmentIconUrls.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("Ranked augment image URLs are incomplete.");

                    var liveStrength = MayhemCardRenderer.BuildStrengthTextForSmokeTest(result);
                    if (string.IsNullOrWhiteSpace(liveStrength) || liveStrength.IndexOf("#", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("Decision-card strength projection is incomplete.");
                    var liveCore = MayhemCardRenderer.BuildCorePathTextForSmokeTest(result);
                    if (string.IsNullOrWhiteSpace(liveCore) || string.Equals(liveCore, MayhemUiCopy.NoCoreBuild, StringComparison.Ordinal))
                        throw new InvalidOperationException("Decision-card core-build projection is incomplete.");

                    using (var image = MayhemCardRenderer.RenderForSmokeTest(result))
                    {
                        if (image.Width != MayhemCardRenderer.CardWidth || image.Height < 650 || image.Height > 1750)
                            throw new InvalidOperationException(
                                "Mayhem compact image card dimensions are invalid: " + image.Width + "x" + image.Height + ".");
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
            var result = new MayhemChampionResult
            {
                Tier = "S+",
                Rank = 6,
                WinRate = 55.83
            };
            if (MayhemRankedAugmentService.ApplyFromHtmlForSmokeTest(result, html) != 3)
                throw new InvalidOperationException("Rich augment fixture was not applied.");
            if (result.AugmentRoutes.Count != 3 || result.Augments[0] != "测试棱彩")
                throw new InvalidOperationException("Rich augment decision routes or names are invalid.");
            if (result.AugmentRoutes.Select(route => route.AugmentName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
                throw new InvalidOperationException("Rich augment fixture routes are not distinct.");

            var primary = MayhemCardRenderer.BuildPrimaryAugmentTextForSmokeTest(result);
            if (primary.IndexOf("测试棱彩", StringComparison.Ordinal) < 0 || primary.IndexOf("61.23%", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Decision-card primary augment projection lost the top statistical augment.");
            var strength = MayhemCardRenderer.BuildStrengthTextForSmokeTest(result);
            if (strength.IndexOf("S+", StringComparison.Ordinal) < 0 || strength.IndexOf("#6", StringComparison.Ordinal) < 0 || strength.IndexOf("55.83%", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Decision-card strength projection lost source-backed tier/rank/winrate.");
        }

        private static void ValidateBuildDetailsFixture()
        {
            const string html =
                "core_items_0" +
                "<img src=\"https://opgg-static.akamaized.net/item/3072.png\" alt=\"Bloodthirster\">" +
                "<img src=\"https://opgg-static.akamaized.net/item/3031.png\" alt=\"Infinity Edge\">" +
                "<img src=\"https://opgg-static.akamaized.net/item/3006.png\" alt=\"Berserker Greaves\">" +
                "core_items_1" +
                "<img src=\"https://opgg-static.akamaized.net/item/6673.png\" alt=\"Immortal Shieldbow\">" +
                "<img src=\"https://opgg-static.akamaized.net/item/3031.png\" alt=\"Infinity Edge\">" +
                "<img src=\"https://opgg-static.akamaized.net/item/3072.png\" alt=\"Bloodthirster\">" +
                "starter_items_0" +
                "<img src=\"https://opgg-static.akamaized.net/item/1055.png\" alt=\"Doran Blade\">" +
                "<img src=\"https://opgg-static.akamaized.net/item/2003.png\" alt=\"Health Potion\">" +
                "boots_0" +
                "<img src=\"https://opgg-static.akamaized.net/item/3006.png\" alt=\"Berserker Greaves\">" +
                "SummonerSpells Table" +
                "<img src=\"https://opgg-static.akamaized.net/images/lol/spell/SummonerFlash.png\" alt=\"Flash\">" +
                "<img src=\"https://opgg-static.akamaized.net/images/lol/spell/SummonerSnowball.png\" alt=\"Mark\">" +
                "SkillOrder Table" +
                "<img alt=\"Steel Tempest\" src=\"https://opgg-static.akamaized.net/images/lol/spell/YasuoQ.png\"><strong>Q</strong>" +
                "<img alt=\"Sweeping Blade\" src=\"https://opgg-static.akamaized.net/images/lol/spell/YasuoE.png\"><strong>E</strong>" +
                "<img alt=\"Wind Wall\" src=\"https://opgg-static.akamaized.net/images/lol/spell/YasuoW.png\"><strong>W</strong>";

            var result = new MayhemChampionResult { SkillOrder = "Q > E > W" };
            MayhemBuildDetailsService.ApplyHtmlForSmokeTest(result, html);
            if (result.CoreBuilds == null || result.CoreBuilds.Count != 2 || result.CoreBuilds.Any(build => build.Items.Count < 3))
                throw new InvalidOperationException("Compact build fixture did not parse two core paths.");
            if (result.StarterItems.Count < 2 || result.BootItems.Count != 1 || result.SummonerSpells.Count != 2)
                throw new InvalidOperationException("Compact build fixture starter/boots/summoner projection is incomplete.");
            if (result.SkillPriority.Count != 3 || string.Join(string.Empty, result.SkillPriority.Select(skill => skill.Key)) != "QEW")
                throw new InvalidOperationException("Compact build fixture skill priority is incomplete.");

            var coreText = MayhemCardRenderer.BuildCorePathTextForSmokeTest(result);
            if (coreText.IndexOf("Bloodthirster", StringComparison.OrdinalIgnoreCase) < 0 || coreText.IndexOf("Infinity Edge", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Decision-card core path must remain readable even when item images are unavailable.");

            using (var image = MayhemCardRenderer.RenderForSmokeTest(result))
            {
                if (image.Width != MayhemCardRenderer.CardWidth || image.Height < 650 || image.Height > 1750)
                    throw new InvalidOperationException("Decision-card build fixture rendered invalid dimensions.");
            }
        }

        private static void ValidateLocalizedProjectionFixture()
        {
            var result = new MayhemChampionResult
            {
                ChampionName = "亚索",
                ChampionSlug = "yasuo",
                CoreBuilds = new List<MayhemBuildPath>
                {
                    new MayhemBuildPath
                    {
                        Rank = 1,
                        Items = new List<MayhemBuildItem>
                        {
                            new MayhemBuildItem { Id = "3031", Name = "Infinity Edge", IconUrl = "https://opgg-static.akamaized.net/item/3031.png" }
                        }
                    }
                },
                StarterItems = new List<MayhemBuildItem>
                {
                    new MayhemBuildItem { Id = "3031", Name = "Infinity Edge", IconUrl = "https://opgg-static.akamaized.net/item/3031.png" }
                },
                SummonerSpells = new List<MayhemBuildItem>
                {
                    new MayhemBuildItem { Name = "Flash", IconUrl = "https://opgg-static.akamaized.net/images/lol/spell/SummonerFlash.png" }
                },
                SkillPriority = new List<MayhemSkillPriority>
                {
                    new MayhemSkillPriority { Key = "Q", Name = "Steel Tempest", IconUrl = "https://opgg-static.akamaized.net/images/lol/spell/YasuoQ.png" }
                },
                AugmentRows = new List<MayhemAugmentRow>
                {
                    new MayhemAugmentRow
                    {
                        Id = "195",
                        Rank = 1,
                        Name = "Draw Your Sword",
                        Slug = "draw-your-sword",
                        WinRate = 63.38,
                        IconUrl = "https://opgg-static.akamaized.net/augment/draw-your-sword.png"
                    }
                },
                AugmentRoutes = new List<MayhemDecisionRoute>
                {
                    new MayhemDecisionRoute { Title = MayhemUiCopy.StableRoute, AugmentName = "Draw Your Sword" }
                }
            };

            const string itemsJson = "[{\"id\":3031,\"name\":\"无尽之刃\",\"iconPath\":\"/lol-game-data/assets/v1/items/icons2d/3031.png\"}]";
            const string augmentsJson = "[{\"id\":195,\"apiName\":\"Augment_DrawYourSword\",\"name\":\"拔剑出鞘\",\"desc\":\"测试中文说明\",\"iconLarge\":\"assets/ux/kiwi/augments/icons/drawyoursword.png\"}]";
            const string summonersJson = "[{\"id\":4,\"name\":\"闪现\",\"iconPath\":\"/lol-game-data/assets/v1/summoner-spells/icons2d/summonerflash.png\"}]";
            const string championSummaryJson = "[{\"id\":157,\"alias\":\"Yasuo\",\"name\":\"亚索\",\"squarePortraitPath\":\"/lol-game-data/assets/v1/champion-icons/157.png\"}]";
            const string championDetailJson = "{\"spells\":[{\"spellKey\":\"Q\",\"name\":\"斩钢闪\",\"abilityIconPath\":\"/lol-game-data/assets/ASSETS/Characters/Yasuo/HUD/Icons2D/YasuoQ.png\"}]}";

            MayhemDecisionLocalizationService.ApplyFixtureForSmokeTest(
                result,
                itemsJson,
                augmentsJson,
                summonersJson,
                championSummaryJson,
                championDetailJson);

            if (result.CoreBuilds[0].Items[0].Name != "无尽之刃" || !result.CoreBuilds[0].Items[0].IconUrl.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localized item projection did not replace the OP.GG English item and icon.");
            if (result.AugmentRows[0].Name != "拔剑出鞘" || !result.AugmentRows[0].IconUrl.StartsWith("lcu:/lol-game-data/assets/ux/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localized augment projection did not use zh_CN name and normalized LCU iconLarge path.");
            if (result.AugmentRoutes[0].AugmentName != "拔剑出鞘")
                throw new InvalidOperationException("Localized augment route kept the stale English augment name.");
            if (result.SummonerSpells[0].Name != "闪现" || !result.SummonerSpells[0].IconUrl.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localized summoner spell projection is incomplete.");
            if (!result.ChampionIconUrl.StartsWith("lcu:", StringComparison.OrdinalIgnoreCase) ||
                !result.SkillIconUrls.ContainsKey("Q") || !result.SkillIconUrls["Q"].StartsWith("lcu:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localized champion or skill image projection is incomplete.");
            if (result.Augments.Count == 0 || result.Augments[0] != "拔剑出鞘" || result.AugmentIconUrls.Count == 0 || !result.AugmentIconUrls[0].StartsWith("lcu:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Localized legacy augment projection was not synchronized for rendering.");
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
