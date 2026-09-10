using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueBuildAdvisorSmokeTest
    {
        public static void Validate()
        {
            ValidateParsingAndCaching();
            ValidateUnresolvedRankedPosition();
            ValidateModeAndPositionMapping();
            ValidateCancellation();
            Require(LeagueBuildAdvisorUiBridge.HasTrayAccessForSmokeTest(), "Build Advisor lost tray access contract.");
        }

        private static void ValidateParsingAndCaching()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            var budgets = new PerformanceBudgetProvider();
            using (var service = new LeagueBuildAdvisorDataService(lcu, budgets, opgg))
            {
                var first = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(first != null && first.Recommendation != null, "Build Advisor did not parse the first OP.GG recommendation.");
                Require(first.ChampionId == 53, "Build Advisor did not resolve the selected local champion.");
                Require(first.ChampionName == "蒸汽机器人", "Build Advisor did not resolve local champion metadata.");
                Require(first.Mode == "ranked" && first.Position == "jungle", "Build Advisor mapped ranked position incorrectly.");
                Require(first.Source == "OP.GG Global", "Build Advisor must label Tencent fallback data as OP.GG Global.");
                Require(first.Version == "16.16", "Build Advisor did not parse OP.GG version metadata.");

                var runeRows = first.Recommendation.Rows.Where(row => row.Category == "runes").ToList();
                var spellRows = first.Recommendation.Rows.Where(row => row.Category == "summoner-spells").ToList();
                var starterRows = first.Recommendation.Rows.Where(row => row.Category == "starter-items").ToList();
                var coreRows = first.Recommendation.Rows.Where(row => row.Category == "core-items").ToList();
                var skillRows = first.Recommendation.Rows.Where(row => row.Category == "skills").ToList();

                Require(runeRows.Count == 2 && runeRows[0].Recommendation.Contains("电刑") && runeRows[1].Recommendation.Contains("奥术彗星"),
                    "Build Advisor did not preserve ordered rune alternatives.");
                Require(spellRows.Count == 2 && spellRows[0].Recommendation.Contains("闪现") && spellRows[0].Recommendation.Contains("惩戒"),
                    "Build Advisor did not preserve ordered summoner-spell alternatives.");
                Require(spellRows[0].Evidence.Contains("win 54.2%") && spellRows[0].Evidence.Contains("1,200 games"),
                    "Build Advisor did not project available win/sample evidence.");
                Require(spellRows[1].Evidence.IndexOf("win 0.0%", StringComparison.OrdinalIgnoreCase) < 0,
                    "Missing OP.GG win evidence was incorrectly rendered as a zero-percent win rate.");
                Require(starterRows.Count == 2 && starterRows[0].Recommendation.Contains("多兰之戒") && starterRows[1].Recommendation.Contains("多兰之刃"),
                    "Build Advisor did not preserve ordered starter-item alternatives.");
                Require(coreRows.Count == 3 && coreRows[0].Recommendation.Contains("卢登") && coreRows[2].Recommendation.Contains("振奋盔甲"),
                    "Build Advisor alternative cap/order contract drifted.");
                Require(skillRows.Count == 2 && skillRows[0].Recommendation == "Q > E > W",
                    "Build Advisor did not preserve ordered skill alternatives.");

                Require(first.Recommendation.WinRate.HasValue && first.Recommendation.PickRate.HasValue && first.Recommendation.BanRate.HasValue,
                    "Build Advisor lost champion summary rates used by Runtime Companion.");
                Require(opgg.Paths.Count == 2, "First Build Advisor refresh must request one version and one build payload.");
                Require(opgg.Paths.Last() == "/api/global/champions/ranked/53/jungle?tier=all&version=16.16",
                    "Build Advisor did not bind the OP.GG build request to tier/version.");

                var second = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(second.Recommendation != null && second.FromCache, "Repeated Build Advisor refresh did not use the 10-minute cache.");
                Require(second.Recommendation.Rows.Count == first.Recommendation.Rows.Count,
                    "Build Advisor cache clone lost alternative recommendation rows.");
                Require(opgg.Paths.Count == 2, "Repeated identical Build Advisor refresh caused OP.GG fan-out.");

                lcu.ChampionId = 145;
                var changed = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(changed.ChampionId == 145 && changed.Recommendation != null, "Champion change did not refresh the recommendation.");
                Require(opgg.Paths.Count == 3, "Champion change must reuse version cache and request only the new build.");

                var beforeGame = opgg.Paths.Count;
                lcu.Phase = "InProgress";
                var inGame = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(opgg.Paths.Count == beforeGame, "In-game Build Advisor sent a new OP.GG request.");
                Require(inGame.Activity == LeagueActivityLevel.InGame, "Build Advisor lost Gameflow activity mapping.");

                Require(!lcu.Paths.Any(path => path.IndexOf("match-history", StringComparison.OrdinalIgnoreCase) >= 0),
                    "Build Advisor must not fan out to match history or scouting endpoints.");
                Require(lcu.Paths.All(path => path.StartsWith("/", StringComparison.Ordinal)),
                    "Build Advisor LCU fixture saw an invalid request path.");
            }
        }

        private static void ValidateUnresolvedRankedPosition()
        {
            var lcu = new FakeLeagueApi { AssignedPosition = null, ChampionId = 157 };
            var opgg = new FakeOpggApi();
            var budgets = new PerformanceBudgetProvider();
            using (var service = new LeagueBuildAdvisorDataService(lcu, budgets, opgg))
            {
                var first = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(first.Recommendation != null, "Unknown Tencent ranked position did not recover to a usable OP.GG build.");
                Require(first.Position == "mid", "Build Advisor did not infer the champion's primary OP.GG ranked position.");
                Require(opgg.Paths.Count == 3, "Unknown ranked position must use version + one champion list + one build request.");
                Require(opgg.Paths[1] == "/api/global/champions/ranked?tier=all&version=16.16",
                    "Build Advisor did not use the version-bound OP.GG champion list for lane inference.");
                Require(opgg.Paths[2] == "/api/global/champions/ranked/157/mid?tier=all&version=16.16",
                    "Build Advisor still sent an unresolved ranked position to OP.GG.");

                var count = opgg.Paths.Count;
                var repeated = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(repeated.Recommendation != null && repeated.FromCache,
                    "Inferred ranked position did not participate in the build cache.");
                Require(opgg.Paths.Count == count,
                    "Repeated unresolved Tencent position caused extra OP.GG lane/build requests.");

                lcu.Phase = "InProgress";
                var beforeGame = opgg.Paths.Count;
                var inGame = service.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Require(inGame.Recommendation != null && inGame.FromCache,
                    "In-game cache-only lookup lost a build learned from an inferred ranked position.");
                Require(opgg.Paths.Count == beforeGame,
                    "In-game unresolved position triggered a forbidden OP.GG request.");
            }
        }

        private static void ValidateModeAndPositionMapping()
        {
            Require(LeagueBuildAdvisorDataService.ResolveOpggMode(450, null) == "aram", "ARAM queue mapping changed.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggMode(2400, "KIWI") == null, "Global ARAM Mayhem must not reuse ordinary ARAM builds.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggMode(3270, "ARAM") == null, "Tencent ARAM Mayhem must not reuse ordinary ARAM builds.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggMode(420, null) == "ranked", "Ranked queue mapping changed.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggPosition("UTILITY", "ranked") == "support", "Support position mapping changed.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggPosition("BOTTOM", "ranked") == "adc", "ADC position mapping changed.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggPosition(null, "ranked") == "all", "Unknown ranked position must remain an internal unresolved sentinel.");
            Require(LeagueBuildAdvisorDataService.ResolveOpggPosition("TOP", "aram") == "none", "Non-ranked mode must not invent a lane position.");
            Require(LeagueBuildAdvisorDataService.BuildPath(53, "ranked", "jungle", "16.16") ==
                    "/api/global/champions/ranked/53/jungle?tier=all&version=16.16",
                "OP.GG build path contract changed.");
        }

        private static void ValidateCancellation()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            var budgets = new PerformanceBudgetProvider();
            using (var service = new LeagueBuildAdvisorDataService(lcu, budgets, opgg))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    service.RefreshAsync(false, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Build Advisor ignored cancellation.");
                }
                catch (OperationCanceledException)
                {
                    // Expected: form close must stop the request chain immediately.
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeLeagueApi : ILeagueClientApi
        {
            public string Phase { get; set; } = "ChampSelect";
            public int ChampionId { get; set; } = 53;
            public string AssignedPosition { get; set; } = "JUNGLE";
            public List<string> Paths { get; } = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (path == LeagueDashboardPhaseService.PhasePath)
                    return Bytes("\"" + Phase + "\"");
                if (path == LeagueLiveDataService.ChampSelectSessionPath)
                {
                    var position = AssignedPosition == null ? string.Empty : "\"assignedPosition\":\"" + AssignedPosition + "\",";
                    return Bytes("{\"gameId\":123,\"queueId\":420,\"localPlayerCellId\":1,\"myTeam\":[{\"cellId\":1,\"puuid\":\"local-puuid\"," + position + "\"championId\":" + ChampionId + ",\"championPickIntent\":" + ChampionId + "}],\"theirTeam\":[],\"actions\":[]}");
                }
                if (path == LeagueLiveDataService.GameflowSessionPath)
                    return Bytes("{\"phase\":\"InProgress\",\"map\":{\"id\":11,\"gameMode\":\"CLASSIC\"},\"gameData\":{\"gameId\":123,\"queue\":{\"id\":420,\"gameMode\":\"CLASSIC\"},\"teamOne\":[{\"puuid\":\"local-puuid\",\"championId\":" + ChampionId + "}],\"teamTwo\":[]}}");
                if (path == LeagueBuildAdvisorDataService.ChampionSummaryPath)
                    return Bytes("[{\"id\":53,\"name\":\"蒸汽机器人\"},{\"id\":145,\"name\":\"虚空之女\"},{\"id\":157,\"name\":\"疾风剑豪\"},{\"id\":64,\"name\":\"盲僧\"}]");
                if (path == LeagueBuildAdvisorDataService.ItemsPath)
                    return Bytes("[{\"id\":1056,\"name\":\"多兰之戒\"},{\"id\":1055,\"name\":\"多兰之刃\"},{\"id\":3020,\"name\":\"法师之靴\"},{\"id\":3117,\"name\":\"疾行之靴\"},{\"id\":6655,\"name\":\"卢登伴侣\"},{\"id\":3071,\"name\":\"黑色切割者\"},{\"id\":3065,\"name\":\"振奋盔甲\"},{\"id\":3089,\"name\":\"灭世者的死亡之帽\"}]");
                if (path == LeagueBuildAdvisorDataService.SummonerSpellsPath)
                    return Bytes("[{\"id\":4,\"name\":\"闪现\"},{\"id\":11,\"name\":\"惩戒\"},{\"id\":12,\"name\":\"传送\"}]");
                if (path == LeagueBuildAdvisorDataService.PerksPath)
                    return Bytes("[{\"id\":8112,\"name\":\"电刑\"},{\"id\":8143,\"name\":\"突然冲击\"},{\"id\":8347,\"name\":\"饼干配送\"},{\"id\":8214,\"name\":\"奥术彗星\"},{\"id\":8226,\"name\":\"法力流系带\"}]");
                return Task.FromResult<byte[]>(null);
            }

            private static Task<byte[]> Bytes(string text)
            {
                return Task.FromResult(Encoding.UTF8.GetBytes(text));
            }
        }

        private sealed class FakeOpggApi : IOpggBuildApi
        {
            public List<string> Paths { get; } = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (path.EndsWith("/versions", StringComparison.Ordinal))
                    return Bytes("{\"data\":[\"16.16\",\"16.15\"]}");

                if (path.StartsWith("/api/global/champions/ranked?tier=", StringComparison.Ordinal))
                {
                    return Bytes("{\"data\":[{\"id\":157,\"positions\":[{\"name\":\"TOP\",\"stats\":{\"play\":100,\"role_rate\":0.21}},{\"name\":\"MID\",\"stats\":{\"play\":900,\"role_rate\":0.79}}]}]}");
                }

                return Bytes("{\"data\":{\"summary\":{\"average_stats\":{\"win_rate\":0.512,\"pick_rate\":0.073,\"ban_rate\":0.021,\"tier_data\":{\"tier\":2,\"rank\":17}}},\"summoner_spells\":[{\"ids\":[4,11],\"play\":1200,\"win\":650,\"pick_rate\":0.66},{\"ids\":[4,12],\"play\":300,\"pick_rate\":0.18}],\"runes\":[{\"primary_rune_ids\":[8112,8143],\"secondary_rune_ids\":[8347],\"stat_mod_ids\":[],\"play\":900,\"win\":500,\"pick_rate\":0.55},{\"primary_rune_ids\":[8214,8226],\"secondary_rune_ids\":[8347],\"stat_mod_ids\":[],\"play\":220,\"pick_rate\":0.13}],\"starter_items\":[{\"ids\":[1056],\"play\":800,\"win_rate\":0.51,\"pick_rate\":0.48},{\"ids\":[1055],\"play\":260,\"pick_rate\":0.16}],\"boots\":[{\"ids\":[3020],\"play\":700,\"pick_rate\":0.42},{\"ids\":[3117],\"play\":190,\"pick_rate\":0.11}],\"core_items\":[{\"ids\":[6655],\"play\":600,\"pick_rate\":0.36},{\"ids\":[3071],\"play\":310,\"pick_rate\":0.19},{\"ids\":[3065],\"play\":180,\"pick_rate\":0.11},{\"ids\":[3089],\"play\":100,\"pick_rate\":0.06}],\"skill_masteries\":[{\"ids\":[\"Q\",\"E\",\"W\"],\"play\":1000,\"pick_rate\":0.61},{\"ids\":[\"Q\",\"W\",\"E\"],\"play\":280,\"pick_rate\":0.17}],\"counters\":[{\"champion_id\":64,\"play\":321,\"win\":144}]},\"meta\":{\"version\":\"16.16\"}}");
            }

            private static Task<byte[]> Bytes(string text)
            {
                return Task.FromResult(Encoding.UTF8.GetBytes(text));
            }
        }
    }
}
