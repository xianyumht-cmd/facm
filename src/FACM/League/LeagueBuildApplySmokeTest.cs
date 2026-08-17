using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueBuildApplySmokeTest
    {
        public static void Validate()
        {
            ValidatePreparationIsReadOnly();
            ValidateRankedOptionsAreDistinct();
            ValidateHappyPathAndFlashSlot();
            ValidateRuneCapacityFailsClosed();
            ValidateContextDriftBlocksAllWrites();
            ValidatePartialFailureIsHonest();
            ValidateCancellation();
            ValidateUiFallbacks();
        }

        private static void ValidatePreparationIsReadOnly()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                Require(plan != null && plan.HasSpells && plan.HasRunes, "Gate 2 did not parse a complete OP.GG apply plan.");
                Require(plan.OptionRank == 1, "Compatibility PrepareAsync must keep automatic apply on OP.GG rank #1.");
                Require(plan.PrimaryStyleId == 8000 && plan.SecondaryStyleId == 8100,
                    "Gate 2 lost OP.GG rune style IDs required by LCU.");
                Require(plan.PrimaryRuneIds.SequenceEqual(new[] { 8005, 9111, 9104, 8014 }),
                    "Gate 2 parsed primary rune IDs incorrectly.");
                Require(plan.SecondaryRuneIds.SequenceEqual(new[] { 8139, 8135 }),
                    "Gate 2 parsed secondary rune IDs incorrectly.");
                Require(plan.StatModIds.SequenceEqual(new[] { 5005, 5008, 5001 }),
                    "Gate 2 parsed stat mod IDs incorrectly.");
                Require(lcu.Writes.Count == 0, "Preparing or previewing Gate 2 produced an LCU write before confirmation.");
                Require(opgg.Paths.Count == 1 && opgg.Paths[0].Contains("/ranked/157/mid"),
                    "Gate 2 preparation did not reuse the accepted Build Advisor context.");
            }
        }

        private static void ValidateRankedOptionsAreDistinct()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg))
            {
                var options = service.PrepareOptionsAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                Require(options.Count == 3, "Manual Gate 2 UI must expose the top three real OP.GG choices when the payload provides them.");
                Require(options.Select(row => row.OptionRank).SequenceEqual(new[] { 1, 2, 3 }),
                    "Ranked OP.GG choices lost their stable #1/#2/#3 order.");
                Require(options[0].Spell1Id == 11 && options[1].Spell1Id == 14 && options[2].Spell1Id == 12,
                    "Ranked choices collapsed into duplicate summoner-spell data.");
                Require(options[0].PrimaryRuneIds[0] == 8005 && options[1].PrimaryRuneIds[0] == 8008 && options[2].PrimaryRuneIds[0] == 8021,
                    "Ranked choices collapsed into duplicate rune data.");
                Require(Math.Abs((options[0].RunePickRate ?? 0) - 0.60) < 0.0001 &&
                        Math.Abs((options[1].RunePickRate ?? 0) - 0.24) < 0.0001 &&
                        Math.Abs((options[2].RunePickRate ?? 0) - 0.11) < 0.0001,
                    "Ranked choice evidence lost OP.GG pick-rate data.");
                Require(opgg.Paths.Count == 1,
                    "Preparing three manual choices must use one OP.GG payload, not three network requests.");
                Require(lcu.Writes.Count == 0,
                    "Preparing three manual choices crossed the LCU write boundary before confirmation.");
            }
        }

        private static void ValidateHappyPathAndFlashSlot()
        {
            var lcu = new FakeLeagueApi
            {
                Spell1Id = 4,
                Spell2Id = 14,
                CanAddCustomPage = true
            };
            var opgg = new FakeOpggApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();

                Require(result.Status == "success" && result.RunesApplied && result.SpellsApplied,
                    "Gate 2 happy path did not report full verified success.");
                Require(result.CreatedRunePageId == FakeLeagueApi.CreatedPageId,
                    "Gate 2 did not retain the newly created FACM rune page ID.");
                Require(lcu.Writes.Count == 4, "Gate 2 happy path must use exactly POST page + PUT page + PUT current + PATCH spells.");
                RequireWrite(lcu, "POST", LeagueBuildApplyService.PerkCreatePath);
                RequireWrite(lcu, "PUT", LeagueBuildApplyService.PerkPagesPath + "/" + FakeLeagueApi.CreatedPageId);
                RequireWrite(lcu, "PUT", LeagueBuildApplyService.PerkCurrentPagePath);
                var spellWrite = RequireWrite(lcu, "PATCH", LeagueBuildApplyService.MySelectionPath);
                Require(spellWrite.Json.Contains("\"spell1Id\":4") && spellWrite.Json.Contains("\"spell2Id\":11"),
                    "Gate 2 did not preserve the user's existing Flash-on-D slot.");
                Require(lcu.Spell1Id == 4 && lcu.Spell2Id == 11,
                    "Gate 2 spell read-back fixture did not reach the verified target.");
                Require(lcu.Writes.All(call =>
                    call.Path.IndexOf("actions", StringComparison.OrdinalIgnoreCase) < 0 &&
                    call.Path.IndexOf("reroll", StringComparison.OrdinalIgnoreCase) < 0 &&
                    call.Path.IndexOf("skin", StringComparison.OrdinalIgnoreCase) < 0),
                    "Gate 2 touched a forbidden Champ Select write endpoint.");
            }
        }

        private static void ValidateRuneCapacityFailsClosed()
        {
            var lcu = new FakeLeagueApi
            {
                CanAddCustomPage = false,
                Spell1Id = 14,
                Spell2Id = 4
            };
            var opgg = new FakeOpggApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();

                Require(result.Status == "partial" && result.RuneSkippedNoCapacity && !result.RunesApplied && result.SpellsApplied,
                    "Full rune inventory must skip runes while allowing an independently verified spell apply.");
                Require(!lcu.Writes.Any(call => call.Path.StartsWith(LeagueBuildApplyService.PerkPagesPath, StringComparison.OrdinalIgnoreCase)),
                    "Gate 2 overwrote or modified an existing rune page when custom page capacity was full.");
                Require(!lcu.ReadPaths.Any(path => string.Equals(path, LeagueBuildApplyService.PerkPagesPath, StringComparison.OrdinalIgnoreCase)),
                    "Gate 2 inspected existing rune pages after learning that no safe custom slot was available.");
                Require(lcu.Writes.Count == 1 && lcu.Writes[0].Method == "PATCH",
                    "Full rune inventory should leave only the explicit summoner spell write.");
                Require(lcu.Spell1Id == 11 && lcu.Spell2Id == 4,
                    "Gate 2 did not preserve the existing Flash-on-F slot.");
            }
        }

        private static void ValidateContextDriftBlocksAllWrites()
        {
            var lcu = new FakeLeagueApi { Phase = "InProgress" };
            var opgg = new FakeOpggApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champ-select-required",
                    "Leaving Champ Select did not fail closed before Gate 2 writes.");
                Require(lcu.Writes.Count == 0, "Gate 2 wrote to LCU after phase drift to In Game.");
            }

            var changedChampion = new FakeLeagueApi { ChampionId = 145 };
            using (var service = new LeagueBuildApplyService(changedChampion, changedChampion, new PerformanceBudgetProvider(), new FakeOpggApi()))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champion-changed",
                    "Champion drift did not fail closed before Gate 2 writes.");
                Require(changedChampion.Writes.Count == 0, "Gate 2 wrote a stale champion loadout.");
            }
        }

        private static void ValidatePartialFailureIsHonest()
        {
            var lcu = new FakeLeagueApi { FailSpellPatch = true };
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), new FakeOpggApi()))
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "partial" && result.RunesApplied && !result.SpellsApplied,
                    "Gate 2 falsely reported full success after the spell PATCH failed.");
                Require(result.SpellStatus == "write-failed", "Gate 2 lost the scoped spell failure reason.");
            }
        }

        private static void ValidateCancellation()
        {
            var lcu = new FakeLeagueApi();
            using (var service = new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), new FakeOpggApi()))
            using (var cancellation = new CancellationTokenSource())
            {
                var plan = service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                cancellation.Cancel();
                try
                {
                    service.ApplyAsync(plan, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Gate 2 ignored caller cancellation.");
                }
                catch (OperationCanceledException)
                {
                    // Expected: closing the apply form cancels before any write.
                }
                Require(lcu.Writes.Count == 0, "Cancelled Gate 2 apply produced an LCU write.");
            }
        }

        private static void ValidateUiFallbacks()
        {
            foreach (var pair in LeagueBuildApplyUiTextKeys.DefaultsForSmokeTest())
                Require(!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value),
                    "Ranked apply UI fallback contains a blank key or label.");
            string itemSetMenu;
            Require(LeagueItemSetUiTextKeys.TryGetDefault(LeagueItemSetUiTextKeys.Menu, out itemSetMenu) &&
                    !string.IsNullOrWhiteSpace(itemSetMenu),
                "League Hub item-set navigation fallback must never render as a blank button.");
        }

        private static LeagueBuildAdvisorSnapshot CreateSnapshot()
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "summoner-spells",
                Recommendation = "惩戒 · 闪现"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "runes",
                Recommendation = "致命节奏 · 凯旋 · 欢欣 · 坚毅不倒 · 猛然冲击 · 寻宝猎人"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "starter-items",
                Recommendation = "多兰之刃 · 生命药水",
                Evidence = "pick 62.0% · 1200 games"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "boots",
                Recommendation = "狂战士胫甲",
                Evidence = "pick 55.0% · 1000 games"
            });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow
            {
                Category = "core-items",
                Recommendation = "破败王者之刃 · 无尽之刃",
                Evidence = "pick 41.0% · 750 games"
            });
            return new LeagueBuildAdvisorSnapshot
            {
                Connected = true,
                Phase = "ChampSelect",
                Activity = LeagueActivityLevel.ChampSelect,
                BudgetName = "champ-select",
                QueueId = 420,
                ChampionId = 157,
                ChampionName = "疾风剑豪",
                Mode = "ranked",
                Position = "mid",
                Source = "OP.GG Global",
                Version = "16.16",
                Status = "ready",
                Recommendation = recommendation
            };
        }

        private static WriteCall RequireWrite(FakeLeagueApi lcu, string method, string path)
        {
            var call = lcu.Writes.FirstOrDefault(row => row.Method == method && row.Path == path);
            Require(call != null, "Missing Gate 2 write contract: " + method + " " + path);
            return call;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeOpggApi : IOpggBuildApi
        {
            public List<string> Paths { get; } = new List<string>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                return Bytes("{\"data\":{" +
                    "\"summoner_spells\":[" +
                    "{\"ids\":[11,4],\"play\":1000,\"pick_rate\":0.70}," +
                    "{\"ids\":[14,4],\"play\":410,\"pick_rate\":0.26}," +
                    "{\"ids\":[12,4],\"play\":180,\"pick_rate\":0.12}]," +
                    "\"runes\":[" +
                    "{\"primary_page_id\":8000,\"secondary_page_id\":8100,\"primary_rune_ids\":[8005,9111,9104,8014],\"secondary_rune_ids\":[8139,8135],\"stat_mod_ids\":[5005,5008,5001],\"play\":900,\"pick_rate\":0.60}," +
                    "{\"primary_page_id\":8000,\"secondary_page_id\":8400,\"primary_rune_ids\":[8008,9111,9104,8014],\"secondary_rune_ids\":[8444,8451],\"stat_mod_ids\":[5005,5008,5001],\"play\":360,\"pick_rate\":0.24}," +
                    "{\"primary_page_id\":8000,\"secondary_page_id\":8200,\"primary_rune_ids\":[8021,9111,9104,8014],\"secondary_rune_ids\":[8233,8236],\"stat_mod_ids\":[5005,5008,5001],\"play\":165,\"pick_rate\":0.11}]}}");
            }
        }

        private sealed class WriteCall
        {
            public string Method { get; set; }
            public string Path { get; set; }
            public string Json { get; set; }
        }

        private sealed class FakeLeagueApi : ILeagueClientApi, ILeagueClientWriteApi
        {
            public const int CreatedPageId = 77;
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
            private string _runePageJson;

            public string Phase { get; set; } = "ChampSelect";
            public int ChampionId { get; set; } = 157;
            public int QueueId { get; set; } = 420;
            public int Spell1Id { get; set; } = 4;
            public int Spell2Id { get; set; } = 14;
            public bool CanAddCustomPage { get; set; } = true;
            public bool FailSpellPatch { get; set; }
            public List<string> ReadPaths { get; } = new List<string>();
            public List<WriteCall> Writes { get; } = new List<WriteCall>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadPaths.Add(path);
                if (path == LeagueDashboardPhaseService.PhasePath)
                    return Bytes("\"" + Phase + "\"");
                if (path == LeagueLiveDataService.ChampSelectSessionPath)
                {
                    return Bytes("{\"gameId\":123,\"queueId\":" + QueueId + ",\"localPlayerCellId\":1,\"myTeam\":[{\"cellId\":1,\"puuid\":\"local-puuid\",\"assignedPosition\":\"MIDDLE\",\"championId\":" + ChampionId + ",\"championPickIntent\":" + ChampionId + "}],\"theirTeam\":[],\"actions\":[]}");
                }
                if (path == LeagueLiveDataService.GameflowSessionPath)
                    return Bytes("{\"phase\":\"InProgress\",\"map\":{\"id\":11,\"gameMode\":\"CLASSIC\"},\"gameData\":{\"gameId\":123,\"queue\":{\"id\":" + QueueId + ",\"gameMode\":\"CLASSIC\"},\"teamOne\":[{\"puuid\":\"local-puuid\",\"championId\":" + ChampionId + "}],\"teamTwo\":[]}}");
                if (path == LeagueBuildApplyService.PerkInventoryPath)
                    return Bytes("{\"canAddCustomPage\":" + (CanAddCustomPage ? "true" : "false") + "}");
                if (path == LeagueBuildApplyService.MySelectionPath)
                    return Bytes("{\"spell1Id\":" + Spell1Id + ",\"spell2Id\":" + Spell2Id + "}");
                if (path == LeagueBuildApplyService.PerkPagesPath)
                    return Bytes(_runePageJson == null ? "[]" : "[" + _runePageJson + "]");
                return Task.FromResult<byte[]>(null);
            }

            public Task<LeagueClientWriteResponse> TrySendJsonAsync(
                string method,
                string path,
                string json,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var call = new WriteCall { Method = method, Path = path, Json = json ?? string.Empty };
                Writes.Add(call);

                if (method == "POST" && path == LeagueBuildApplyService.PerkCreatePath)
                    return Success("{\"id\":" + CreatedPageId + "}");

                if (method == "PUT" && path == LeagueBuildApplyService.PerkPagesPath + "/" + CreatedPageId)
                {
                    _runePageJson = json;
                    return Success("{}");
                }

                if (method == "PUT" && path == LeagueBuildApplyService.PerkCurrentPagePath)
                    return Success("{}");

                if (method == "PATCH" && path == LeagueBuildApplyService.MySelectionPath)
                {
                    if (FailSpellPatch) return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 500, Body = BytesValue("{}") });
                    var parsed = _json.DeserializeObject(json) as Dictionary<string, object>;
                    Spell1Id = ReadInt(parsed, "spell1Id");
                    Spell2Id = ReadInt(parsed, "spell2Id");
                    return Success("{}");
                }

                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 404, Body = BytesValue("{}") });
            }

            private static Task<LeagueClientWriteResponse> Success(string json)
            {
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 200, Body = BytesValue(json) });
            }

            private static int ReadInt(Dictionary<string, object> source, string key)
            {
                object value;
                int parsed;
                return source != null && source.TryGetValue(key, out value) &&
                       int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)
                    ? parsed
                    : 0;
            }
        }

        private static Task<byte[]> Bytes(string text)
        {
            return Task.FromResult(BytesValue(text));
        }

        private static byte[] BytesValue(string text)
        {
            return Encoding.UTF8.GetBytes(text ?? string.Empty);
        }
    }
}
