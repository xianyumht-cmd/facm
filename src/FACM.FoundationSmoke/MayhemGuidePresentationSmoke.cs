using FACM.Core.Mayhem;

internal static class MayhemGuidePresentationSmoke
{
    public static void Run()
    {
        ValidateCompleteGuide();
        ValidateEmptySectionsAreOmitted();
        ValidateSecondaryFixtures();
    }

    private static void ValidateCompleteGuide()
    {
        var result = new MayhemChampionResult
        {
            Query = "洛",
            ChampionName = "幻翎",
            ChampionSlug = "rakan",
            Patch = "26.17",
            Tier = "C",
            Rank = 154,
            WinRate = 45.48,
            PickRate = 1.74,
            SampleSize = 12400,
            UpdatedAtUtc = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero),
            MayhemBalanceSummary = "Damage Taken -5% · Ability Haste +10",
            SourceNote = "排行：内部源；版本核验：腾讯官网已校验",
            SummonerSpells =
            [
                new MayhemBuildItem { Name = "闪现" },
                new MayhemBuildItem { Name = "标记" }
            ],
            RuneRecommendation = new MayhemRuneRecommendation
            {
                PrimaryTree = "主宰",
                Keystone = "电刑",
                PrimaryRunes = ["恶意中伤", "眼球收集器", "终极猎人"],
                SecondaryTree = "启迪",
                SecondaryRunes = ["神奇之鞋", "饼干配送"],
                StatShards = ["攻击速度", "自适应力", "生命值"]
            },
            SkillPriority =
            [
                new MayhemSkillPriority { Key = "W", Name = "盛大登场" },
                new MayhemSkillPriority { Key = "E", Name = "轻舞成双" },
                new MayhemSkillPriority { Key = "Q", Name = "微光飞翎" }
            ],
            SkillOrder = "W → E → Q → Q → W → E → W → W → R → W",
            StarterItems = [new MayhemBuildItem { Name = "巨人腰带" }, new MayhemBuildItem { Name = "生命药水" }],
            BootItems = [new MayhemBuildItem { Name = "水银之靴" }],
            CoreBuilds =
            [
                new MayhemBuildPath { Rank = 1, Items = [new MayhemBuildItem { Name = "末日寒冬" }, new MayhemBuildItem { Name = "钢铁烈阳之匣" }] },
                new MayhemBuildPath { Rank = 2, Items = [new MayhemBuildItem { Name = "心之钢" }, new MayhemBuildItem { Name = "无终恨意" }] }
            ],
            TopTen =
            [
                new MayhemTopChampion { Name = "艾希", WinRate = 55.1 },
                new MayhemTopChampion { Name = "萨勒芬妮", WinRate = 54.8 }
            ]
        };

        var guide = MayhemGuidePresentation.Create(result);
        var body = string.Join("\n", guide.Sections.Select(section => section.Title + "\n" + section.Body));
        Require(guide.QueryTitle == "洛" && guide.OfficialName == "幻翎", "manual query and official champion identity");
        Require(guide.ModeTitle == "当前模式：海克斯大乱斗", "localized mode title");
        Require(guide.Sections.Select(section => section.Key).SequenceEqual(
            new[] { "strength", "mode", "summoners", "runes", "skills", "build", "ranking", "data" }),
            "complete guide section order");
        Require(body.Contains("推荐召唤师技能\n闪现 · 标记", StringComparison.Ordinal), "localized summoner recommendation");
        Require(body.Contains("主升：W（盛大登场） · E（轻舞成双）", StringComparison.Ordinal), "localized skill recommendation");
        Require(body.Contains("核心装备：末日寒冬 · 钢铁烈阳之匣", StringComparison.Ordinal), "localized core build recommendation");
        Require(body.Contains("模式调整\n承受伤害 -5% · 技能急速 +10", StringComparison.Ordinal), "localized mode adjustment");
        Require(!body.Contains("Mayhem", StringComparison.OrdinalIgnoreCase) &&
                !body.Contains("Hexdata", StringComparison.OrdinalIgnoreCase) &&
                !body.Contains("CommunityDragon", StringComparison.OrdinalIgnoreCase) &&
                !body.Contains("暂无", StringComparison.Ordinal),
            "normal guide must not expose internal or empty-state copy");
    }

    private static void ValidateEmptySectionsAreOmitted()
    {
        var guide = MayhemGuidePresentation.Create(new MayhemChampionResult
        {
            Query = "洛",
            ChampionName = "幻翎",
            ChampionSlug = "rakan",
            WinRate = 45.48
        });
        var keys = guide.Sections.Select(section => section.Key).ToArray();
        Require(keys.SequenceEqual(new[] { "strength", "data" }), "empty optional guide sections are omitted");
        Require(guide.Sections.All(section => !string.IsNullOrWhiteSpace(section.Body)), "guide has no empty section body");
    }

    private static void ValidateSecondaryFixtures()
    {
        var fixtures = new[]
        {
            ("寒冰", "艾希", "ashe"),
            ("光辉", "光辉女郎", "lux"),
            ("石头人", "熔岩巨兽", "malphite"),
            ("琴女", "琴瑟仙女", "sona")
        };
        foreach (var fixture in fixtures)
        {
            var guide = MayhemGuidePresentation.Create(new MayhemChampionResult
            {
                Query = fixture.Item1,
                ChampionName = fixture.Item2,
                ChampionSlug = fixture.Item3,
                CoreBuilds = [new MayhemBuildPath { Items = [new MayhemBuildItem { Name = "守护者号角" }] }]
            });
            Require(guide.QueryTitle == fixture.Item1 && guide.OfficialName == fixture.Item2,
                "secondary champion identity " + fixture.Item3);
            Require(guide.Sections.Any(section => section.Key == "build"),
                "secondary champion build projection " + fixture.Item3);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
