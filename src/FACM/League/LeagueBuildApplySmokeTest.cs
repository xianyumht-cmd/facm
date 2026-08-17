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
            ValidateHappyPathAndFlashSlot();
            ValidateRuneCapacityFailsClosed();
            ValidateContextDriftBlocksAllWrites();
            ValidateRuneFalseSuccessIsRejected();
            ValidateSpellFalseSuccessIsRejected();
            ValidateCancellation();
        }

        private static void ValidatePreparationIsReadOnly()
        {
            var lcu = new FakeLeagueApi();
            var opgg = new FakeOpggApi();
            using (var service = CreateService(lcu, opgg))
            {
                var plan = Prepare(service);
                Require(plan.HasSpells && plan.HasRunes, "Gate 2 did not parse a complete OP.GG apply plan.");
                Require(plan.PrimaryStyleId == 8000 && plan.SecondaryStyleId == 8100, "Gate 2 lost rune style IDs.");
                Require(plan.PrimaryRuneIds.SequenceEqual(new[] { 8005, 9111, 9104, 8014 }), "Primary runes parsed incorrectly.");
                Require(plan.SecondaryRuneIds.SequenceEqual(new[] { 8139, 8135 }), "Secondary runes parsed incorrectly.");
                Require(plan.StatModIds.SequenceEqual(new[] { 5005, 5008, 5001 }), "Stat mods parsed incorrectly.");
                Require(lcu.Writes.Count == 0, "Preparing Gate 2 produced an LCU write before confirmation.");
                Require(opgg.Paths.Count == 1 && opgg.Paths[0].Contains("/ranked/157/mid"), "Preparation lost the accepted OP.GG context.");
            }
        }

        private static void ValidateHappyPathAndFlashSlot()
        {
            var lcu = new FakeLeagueApi { Spell1Id = 4, Spell2Id = 14 };
            using (var service = CreateService(lcu, new FakeOpggApi()))
            {
                var result = service.ApplyAsync(Prepare(service), CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "success" && result.RunesApplied && result.SpellsApplied,
                    "Gate 2 happy path did not report full settled success.");
                Require(lcu.CurrentRunePageId == FakeLeagueApi.CreatedPageId, "Created FACM rune page was not active.");
                Require(lcu.Spell1Id == 4 && lcu.Spell2Id == 11, "Flash-on-D preservation failed.");
                Require(lcu.Writes.Count == 4, "Happy path must use POST page + PUT page + PUT current + PATCH spells.");
                RequireWrite(lcu, "POST", LeagueBuildApplyService.PerkCreatePath);
                RequireWrite(lcu, "PUT", LeagueBuildApplyService.PerkPagesPath + "/" + FakeLeagueApi.CreatedPageId);
                RequireWrite(lcu, "PUT", LeagueBuildApplyService.PerkCurrentPagePath);
                var spell = RequireWrite(lcu, "PATCH", LeagueBuildApplyService.MySelectionPath);
                Require(spell.Json.Contains("\"spell1Id\":4") && spell.Json.Contains("\"spell2Id\":11"), "Flash slot write payload is wrong.");
                Require(lcu.Writes.All(call => call.Path.IndexOf("actions", StringComparison.OrdinalIgnoreCase) < 0 &&
                                               call.Path.IndexOf("reroll", StringComparison.OrdinalIgnoreCase) < 0 &&
                                               call.Path.IndexOf("skin", StringComparison.OrdinalIgnoreCase) < 0),
                    "Gate 2 touched a forbidden Champ Select endpoint.");
            }
        }

        private static void ValidateRuneCapacityFailsClosed()
        {
            var lcu = new FakeLeagueApi { CanAddCustomPage = false, Spell1Id = 14, Spell2Id = 4 };
            using (var service = CreateService(lcu, new FakeOpggApi()))
            {
                var result = service.ApplyAsync(Prepare(service), CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "partial" && result.RuneSkippedNoCapacity && !result.RunesApplied && result.SpellsApplied,
                    "Full rune inventory must skip runes but still allow verified spells.");
                Require(!lcu.Writes.Any(call => call.Path.StartsWith(LeagueBuildApplyService.PerkPagesPath, StringComparison.OrdinalIgnoreCase)),
                    "Gate 2 modified rune pages when capacity was full.");
                Require(lcu.Spell1Id == 11 && lcu.Spell2Id == 4, "Flash-on-F preservation failed.");
            }
        }

        private static void ValidateContextDriftBlocksAllWrites()
        {
            var inGame = new FakeLeagueApi { Phase = "InProgress" };
            using (var service = CreateService(inGame, new FakeOpggApi()))
            {
                var result = service.ApplyAsync(Prepare(service), CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champ-select-required", "Phase drift did not fail closed.");
                Require(inGame.Writes.Count == 0, "Gate 2 wrote after leaving Champ Select.");
            }

            var changed = new FakeLeagueApi { ChampionId = 145 };
            using (var service = CreateService(changed, new FakeOpggApi()))
            {
                var result = service.ApplyAsync(Prepare(service), CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "blocked" && result.BlockReason == "champion-changed", "Champion drift did not fail closed.");
                Require(changed.Writes.Count == 0, "Gate 2 wrote a stale champion loadout.");
            }
        }

        private static void ValidateRuneFalseSuccessIsRejected()
        {
            var lcu = new FakeLeagueApi { IgnoreCurrentPageSelection = true };
            using (var service = CreateService(lcu, new FakeOpggApi()))
            {
                var plan = Prepare(service);
                plan.Spell1Id = 0;
                plan.Spell2Id = 0;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "failed" && result.RuneStatus == "verify-failed" && !result.RunesApplied,
                    "A 2xx current-page response was falsely treated as an applied rune page.");
                Require(lcu.CurrentRunePageId != FakeLeagueApi.CreatedPageId, "False-success fixture accidentally activated the FACM page.");
                Require(lcu.Writes.Count(x => x.Method == "PUT" && x.Path == LeagueBuildApplyService.PerkCurrentPagePath) == 2,
                    "Rune activation must retry once before failing honestly.");
            }
        }

        private static void ValidateSpellFalseSuccessIsRejected()
        {
            var lcu = new FakeLeagueApi { Spell1Id = 4, Spell2Id = 14, AcceptSpellPatchWithoutApplying = true };
            using (var service = CreateService(lcu, new FakeOpggApi()))
            {
                var plan = Prepare(service);
                plan.PrimaryStyleId = 0;
                var result = service.ApplyAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
                Require(result.Status == "failed" && result.SpellStatus == "verify-failed" && !result.SpellsApplied,
                    "A 2xx spell PATCH was falsely treated as applied when League kept the old spells.");
                Require(lcu.Spell1Id == 4 && lcu.Spell2Id == 14, "False-success spell fixture unexpectedly changed state.");
                Require(lcu.Writes.Count(x => x.Method == "PATCH" && x.Path == LeagueBuildApplyService.MySelectionPath) == 2,
                    "Spell apply must retry once before failing honestly.");
            }
        }

        private static void ValidateCancellation()
        {
            var lcu = new FakeLeagueApi();
            using (var service = CreateService(lcu, new FakeOpggApi()))
            using (var cancellation = new CancellationTokenSource())
            {
                var plan = Prepare(service);
                cancellation.Cancel();
                try
                {
                    service.ApplyAsync(plan, cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Gate 2 ignored caller cancellation.");
                }
                catch (OperationCanceledException) { }
                Require(lcu.Writes.Count == 0, "Cancelled Gate 2 apply produced a write.");
            }
        }

        private static LeagueBuildApplyService CreateService(FakeLeagueApi lcu, FakeOpggApi opgg)
        {
            return new LeagueBuildApplyService(lcu, lcu, new PerformanceBudgetProvider(), opgg);
        }

        private static LeagueBuildApplyPlan Prepare(LeagueBuildApplyService service)
        {
            return service.PrepareAsync(CreateSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
        }

        private static LeagueBuildAdvisorSnapshot CreateSnapshot()
        {
            var recommendation = new LeagueBuildRecommendation();
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "summoner-spells", Recommendation = "惩戒 · 闪现" });
            recommendation.Rows.Add(new LeagueBuildAdvisorRow { Category = "runes", Recommendation = "致命节奏 · 凯旋 · 欢欣 · 坚毅不倒 · 猛然冲击 · 寻宝猎人" });
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
            public readonly List<string> Paths = new List<string>();
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                return Bytes("{\"data\":{\"summoner_spells\":[{\"ids\":[11,4]}],\"runes\":[{\"primary_page_id\":8000,\"secondary_page_id\":8100,\"primary_rune_ids\":[8005,9111,9104,8014],\"secondary_rune_ids\":[8139,8135],\"stat_mod_ids\":[5005,5008,5001]}]}}");
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
            public int CurrentRunePageId { get; set; } = 12;
            public bool CanAddCustomPage { get; set; } = true;
            public bool IgnoreCurrentPageSelection { get; set; }
            public bool AcceptSpellPatchWithoutApplying { get; set; }
            public readonly List<string> ReadPaths = new List<string>();
            public readonly List<WriteCall> Writes = new List<WriteCall>();

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadPaths.Add(path);
                if (path == LeagueDashboardPhaseService.PhasePath) return Bytes("\"" + Phase + "\"");
                if (path == LeagueLiveDataService.ChampSelectSessionPath)
                    return Bytes("{\"gameId\":123,\"queueId\":" + QueueId + ",\"localPlayerCellId\":1,\"myTeam\":[{\"cellId\":1,\"puuid\":\"local\",\"assignedPosition\":\"MIDDLE\",\"championId\":" + ChampionId + ",\"championPickIntent\":" + ChampionId + "}],\"theirTeam\":[],\"actions\":[]}");
                if (path == LeagueBuildApplyService.PerkInventoryPath) return Bytes("{\"canAddCustomPage\":" + (CanAddCustomPage ? "true" : "false") + "}");
                if (path == LeagueBuildApplyService.MySelectionPath) return Bytes("{\"spell1Id\":" + Spell1Id + ",\"spell2Id\":" + Spell2Id + "}");
                if (path == LeagueBuildApplyService.PerkPagesPath) return Bytes(_runePageJson == null ? "[]" : "[" + _runePageJson + "]");
                if (path == LeagueBuildApplyService.PerkCurrentPagePath)
                    return Bytes(CurrentRunePageId == CreatedPageId && _runePageJson != null ? _runePageJson : "{\"id\":" + CurrentRunePageId + "}");
                return Task.FromResult<byte[]>(null);
            }

            public Task<LeagueClientWriteResponse> TrySendJsonAsync(string method, string path, string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Writes.Add(new WriteCall { Method = method, Path = path, Json = json ?? string.Empty });
                if (method == "POST" && path == LeagueBuildApplyService.PerkCreatePath) return Success("{\"id\":" + CreatedPageId + "}");
                if (method == "PUT" && path == LeagueBuildApplyService.PerkPagesPath + "/" + CreatedPageId)
                {
                    _runePageJson = json;
                    return Success("{}");
                }
                if (method == "PUT" && path == LeagueBuildApplyService.PerkCurrentPagePath)
                {
                    int id;
                    if (!IgnoreCurrentPageSelection && int.TryParse((json ?? string.Empty).Trim('"'), out id)) CurrentRunePageId = id;
                    return Success("{}");
                }
                if (method == "PATCH" && path == LeagueBuildApplyService.MySelectionPath)
                {
                    if (!AcceptSpellPatchWithoutApplying)
                    {
                        var parsed = _json.DeserializeObject(json) as Dictionary<string, object>;
                        Spell1Id = ReadInt(parsed, "spell1Id");
                        Spell2Id = ReadInt(parsed, "spell2Id");
                    }
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
                return source != null && source.TryGetValue(key, out value) && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : 0;
            }
        }

        private static Task<byte[]> Bytes(string text) { return Task.FromResult(BytesValue(text)); }
        private static byte[] BytesValue(string text) { return Encoding.UTF8.GetBytes(text ?? string.Empty); }
    }
}
