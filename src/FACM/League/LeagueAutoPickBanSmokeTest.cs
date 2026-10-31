using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Linq;
using FACM.Performance;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAutoPickBanSmokeTest
    {
        public static void Validate()
        {
            ValidateQueueGroupsAndTiming();
            ValidateRemoteGroupCatalog();
            ValidateLiveQueueContextAsync().GetAwaiter().GetResult();
            ValidateSpecialChampionsAsync().GetAwaiter().GetResult();
            ValidateSubsetPicksAsync().GetAwaiter().GetResult();
            ValidateTransientEligibilityRecoveryAsync().GetAwaiter().GetResult();
            Require(!new AppSettings().LeagueAutoPickEnabled && !new AppSettings().LeagueAutoBanEnabled,
                "Automatic pick/ban must default disabled.");
            var settings = AppSettings.ParseLines(new[]
            {
                "LeagueAutoPickEnabled=True", "LeagueAutoBanEnabled=True",
                "LeagueAutoPickChampionId=22", "LeagueAutoBanChampionId=67"
            });
            Require(settings.LeagueAutoPickEnabled && settings.LeagueAutoBanEnabled &&
                    settings.LeagueAutoPickChampionId == 22 && settings.LeagueAutoBanChampionId == 67,
                "Auto-select preferences did not parse.");
            Require(string.Join("\n", settings.BuildLines()).Contains("LeagueAutoPickChampionId=22"),
                "Auto-select preferences did not serialize.");
            Require(LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, -3) &&
                    LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", 0, -1) &&
                    LeagueChampSelectActionWriteApiClient.IsAllowedIntentForSmokeTest(0, -3) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", 0, -3) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, -1) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedIntentForSmokeTest(0, -1),
                "Special champion write targets escaped their action restrictions.");
            Require(LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, 22) &&
                    LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", 3, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("DELETE", "pick", 0, 22) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "trade", 3, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "ban", -1, 67) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedForSmokeTest("PATCH", "pick", 0, 0),
                "ChampSelect action writer escaped its method/type/action/champion fence.");
            ValidateActionParsing();
            ValidateProfiles();
            ValidatePickAndBanAsync().GetAwaiter().GetResult();
            ValidatePickIntentAsync().GetAwaiter().GetResult();
            ValidateAutoBenchAsync().GetAwaiter().GetResult();
            ValidateChampionTradesAsync().GetAwaiter().GetResult();
            ValidateFailureAndCancellationAsync().GetAwaiter().GetResult();
        }

        private static void ValidateActionParsing()
        {
            var action = LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0)));
            Require(action != null && action.Id == 0 && action.ChampionId == 0 && action.Type == "pick",
                "Valid first-player/first-action zero IDs should be accepted.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, false))) == null,
                "Inactive local action should never be issued.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, true, "FINALIZATION"))) == null,
                "Non-ban-pick timer phase should fail closed.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(Session("pick", 0, 0, true, "BAN_PICK", true))) == null,
                "Completed action should never be issued.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes("not json")) == null,
                "Malformed session must not create a write target.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(
                    "{\"timer\":{\"phase\":\"BAN_PICK\"},\"localPlayerCellId\":0," +
                    "\"actions\":[[{\"id\":0,\"type\":\"pick\",\"championId\":0," +
                    "\"isInProgress\":true,\"completed\":false}]]}")) == null,
                "A missing actorCellId must not accidentally match player zero.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(
                    "{\"timer\":{\"phase\":\"BAN_PICK\"},\"localPlayerCellId\":0," +
                    "\"actions\":[[{\"id\":0,\"actorCellId\":0,\"type\":\"pick\"," +
                    "\"championId\":0,\"isInProgress\":true}]]}")) == null,
                "A session without the completed flag must fail closed.");
            Require(LeagueAutoPickBanController.IsAvailable(Bytes("[1,22,67]"), 22) &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("[1,67]"), 22) &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("{\"ids\":[22]}"), 22),
                "Champion availability must use the validated LCU list.");
        }


        private static void ValidateRemoteGroupCatalog()
        {
            var valid = @"{""updatedAt"":""2026-08-02T00:00:00.000Z"",""groups"":[{""groupId"":""ranked"",""name"":{""zh-CN"":""排位模式"",""en"":""Ranked""},""iconPath"":""/lol-game-data/assets/test.png"",""isCustom"":false,""supportedSgpServers"":[""*""],""targetGameModes"":[{""gameMode"":""CLASSIC"",""queueTypes"":[""RANKED_EXPERIMENTAL"",""RANKED_SOLO_5x5""]}],""positions"":[""top"",""jungle"",""middle"",""bottom"",""utility""],""additionalPicks"":[],""additionalBans"":[],""excludedPicks"":[-1],""excludedBans"":[]},{""groupId"":""normal"",""name"":{""zh-CN"":""普通模式"",""en"":""Normal""},""iconPath"":""/lol-game-data/assets/test.png"",""isCustom"":false,""supportedSgpServers"":[""*""],""targetGameModes"":[{""gameMode"":""CLASSIC"",""queueTypes"":[""NORMAL""]}],""positions"":[""default""],""additionalPicks"":[],""additionalBans"":[],""excludedPicks"":[-1],""excludedBans"":[]}]}";
            try
            {
                Require(LeagueAutoSelectGroupCatalog.TryApply(Bytes(valid)) &&
                    LeagueAutoSelectContext.GroupFor("CLASSIC", "RANKED_EXPERIMENTAL", false) == "ranked" &&
                    LeagueAutoSelectContext.GroupFor("CLASSIC", "NORMAL", false) == "normal" &&
                    LeagueAutoSelectContext.GroupFor("CLASSIC", "RANKED_SOLO_5x5", false) == "ranked" &&
                    LeagueAutoSelectContext.GroupFor("CLASSIC", "RANKED_FLEX_SR", false) == null,
                    "Trusted newer group catalog must replace pinned queue routing without implicit fallback.");
                var bound = LeagueAutoSelectContext.Bind(Bytes(SessionWithRole("pick", 12, "TOP")),
                    Bytes(Flow("RANKED_SOLO_5x5")));
                Require(bound != null && Encoding.UTF8.GetString(bound).Contains(":ranked"),
                    "Auto-select context must include the resolved group so a remote route change invalidates in-flight writes.");
                Require(!LeagueAutoSelectGroupCatalog.TryApply(Bytes(valid)) &&
                    !LeagueAutoSelectGroupCatalog.TryApply(Bytes(valid.Replace("2026-08-02", "2026-07-01"))),
                    "Equal or stale auto-select group catalog must not replace the active snapshot.");
                foreach (var bad in new[] {
                    valid.Replace(@"""ranked""", @"""foreign"""),
                    valid.Replace(@"""gameMode"":""CLASSIC""", @"""gameMode"":""ARAM"""),
                    valid.Replace(@"""supportedSgpServers"":[""*"""]", @"""supportedSgpServers"":[""TENCENT_HN1""]"),
                    valid.Replace(@"""additionalPicks"":[]", @"""additionalPicks"":[123]"),
                    valid.Replace(@"""iconPath"":""/lol-game-data/assets/test.png""", @"""iconPath"":""https://evil.example/test.png""")
                })
                    Require(!LeagueAutoSelectGroupCatalog.TryApply(Bytes(bad.Replace("2026-08-02", "2026-08-03"))),
                        "Unknown group, region-restricted group, unmodeled action or remote icon must fail closed.");
            }
            finally { LeagueAutoSelectGroupCatalog.ResetForSmokeTest(); }
            Require(LeagueAutoSelectContext.GroupFor("CLASSIC", "RANKED_SOLO_5x5", false) == "ranked" &&
                LeagueAutoSelectContext.GroupFor("CLASSIC", "RANKED_EXPERIMENTAL", false) == null,
                "Built-in group routing must recover after synthetic remote catalog tests.");
        }

        private static void ValidateQueueGroupsAndTiming()
        {
            var contexts = new[] {
                new[] { "CLASSIC", "RANKED_SOLO_5x5", "ranked" }, new[] { "CLASSIC", "RANKED_FLEX_SR", "ranked" },
                new[] { "CLASSIC", "NORMAL", "normal" }, new[] { "ARAM", "ARAM_UNRANKED_5x5", "aram" },
                new[] { "KIWI", "ANY", "aram" }, new[] { "CHERRY", "ANY", "cherry" }, new[] { "URF", "ANY", "urf" },
                new[] { "ONEFORALL", "ANY", "oneforall" }, new[] { "ULTBOOK", "ANY", "ultbook" },
                new[] { "SWIFTPLAY", "RIOTSCRIPT_BOT", "bot" } };
            foreach (var entry in contexts)
                Require(LeagueAutoSelectContext.GroupFor(entry[0], entry[1], false) == entry[2], "Pinned queue group did not match.");
            Require(LeagueAutoSelectContext.GroupFor("CLASSIC", "NORMAL", true) == "custom" &&
                LeagueAutoSelectContext.GroupFor("PRACTICETOOL", "NORMAL", true) == "custom" &&
                LeagueAutoSelectContext.GroupFor("ARAM", "ANY", true) == null &&
                LeagueAutoSelectContext.GroupFor("CLASSIC", "UNKNOWN", false) == null &&
                LeagueAutoSelectContext.GroupFor("CLASSIC", "", false) == null, "Custom or unsupported queue isolation failed.");
            var timer = new Dictionary<string, object> { { "internalNowInEpochMs", 50000L },
                { "totalTimeInPhase", 30000 }, { "adjustedTimeLeftInPhase", 28500 } };
            Require(LeagueAutoSelectContext.PhaseDelay(2000, false, timer, 50000) == 500, "Phase elapsed time must reduce the first delay.");
            timer["adjustedTimeLeftInPhase"] = 27900;
            Require(LeagueAutoSelectContext.PhaseDelay(2000, true, timer, 50000) == 1900, "Second stage did not align with twice the configured offset.");
            timer["adjustedTimeLeftInPhase"] = -100;
            Require(LeagueAutoSelectContext.PhaseDelay(2000, false, timer, 50000) == 0, "Expired phase extended the delay.");
            timer["isInfinite"] = true;
            Require(LeagueAutoSelectContext.PhaseDelay(2000, false, timer, 50000) == 2000 &&
                LeagueAutoSelectContext.PhaseDelay(2000, false, null, 50000) == 2000, "Non-authoritative timer changed the configured delay.");
            var profiles = new List<LeagueAutoSelectProfile>();
            foreach (var mode in LeagueAutoSelectProfiles.Modes)
                foreach (var role in mode == "ranked" || mode == "rift" || mode == "custom" ? LeagueAutoSelectProfiles.Roles : new[] { "default" })
                    profiles.Add(new LeagueAutoSelectProfile { Mode = mode, Role = role,
                        Picks = Enumerable.Range(1, 20).ToList(), Bans = Enumerable.Range(21, 20).ToList() });
            var encoded = LeagueAutoSelectProfiles.Serialize(profiles);
            Require(encoded.Length > 8192 && LeagueAutoSelectProfiles.Parse(encoded).Count == profiles.Count,
                "The expanded native profile catalog must survive the old size limit.");
            var settings = new AppSettings { LeagueAutoSelectProfiles = encoded, LeagueAutoPickEnabled = true, LeagueAutoPickChampionId = 22 };
            Require(AppSettings.ParseLines(settings.BuildLinesForSmokeTest()).LeagueAutoSelectProfiles == encoded, "Expanded profiles failed INI roundtrip.");
            var restored = new AppSettings();
            var snapshot = CloudSettingsSnapshot.Capture(settings);
            snapshot.ApplyTo(restored);
            var restoredProfiles = LeagueAutoSelectProfiles.Parse(restored.LeagueAutoSelectProfiles);
            Require(restoredProfiles.Count == profiles.Count && profiles.All(expected =>
                    restoredProfiles.Any(actual => actual.Mode == expected.Mode && actual.Role == expected.Role &&
                        actual.Picks.SequenceEqual(expected.Picks) && actual.Bans.SequenceEqual(expected.Bans) &&
                        actual.PickEnabled == expected.PickEnabled && actual.BanEnabled == expected.BanEnabled &&
                        actual.PickStrategy == expected.PickStrategy && actual.BanStrategy == expected.BanStrategy &&
                        actual.PickDelayMs == expected.PickDelayMs && actual.BanDelayMs == expected.BanDelayMs &&
                        actual.BenchSwapDelayMs == expected.BenchSwapDelayMs)) &&
                    restored.LeagueAutoPickEnabled && restored.LeagueAutoPickChampionId == 22,
                "Auto-select profiles or opt-in preferences were lost in cloud restore.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = snapshot });
        }

        private static string Flow(string type, bool custom = false)
        {
            return "{\"phase\":\"ChampSelect\",\"gameData\":{\"isCustomGame\":" + (custom ? "true" : "false") +
                ",\"queue\":{\"id\":420,\"gameMode\":\"CLASSIC\",\"type\":\"" + type + "\"}}}";
        }

        private static async Task ValidateLiveQueueContextAsync()
        {
            var bareSession = SessionWithRole("pick", 12, "TOP").Replace("\"queueId\":420,\"gameMode\":\"CLASSIC\",", "");
            var profiles = LeagueAutoSelectProfiles.Serialize(new[] {
                new LeagueAutoSelectProfile { Mode = "ranked", Role = "top", Picks = new List<int> { 67 } },
                new LeagueAutoSelectProfile { Mode = "normal", Picks = new List<int> { 22 } },
                new LeagueAutoSelectProfile { Mode = "custom", Role = "top", Picks = new List<int> { 22 }, PickEnabled = false } });
            var reader = new FakeRead { SessionJson = bareSession, GameflowJson = Flow("RANKED_SOLO_5x5") };
            var writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 1 && writer.Calls[0] == "pick:12:67:True" &&
                    reader.Paths.Count(p => p == LeagueLiveDataService.GameflowSessionPath) == 2,
                    "Real Gameflow context must drive ranked profiles and be revalidated before writing.");
            }
            foreach (var flow in new[] { Flow("NORMAL", true), "{}", Flow("RANKED_SOLO_5x5").Replace("ChampSelect", "Lobby") })
            {
                reader = new FakeRead { SessionJson = bareSession, GameflowJson = flow }; writer = new FakeWrite();
                using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
                {
                    controller.Configure(true, 22, false, 0, profiles);
                    await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                    Require(writer.Calls.Count == 0 && !reader.Paths.Contains(LeagueAutoPickBanController.PickablePath),
                        "Disabled custom group or unavailable phase context must not reach candidate writes.");
                }
            }
            reader = new FakeRead { SessionJson = bareSession, GameflowJson = Flow("RANKED_SOLO_5x5"), NextGameflowJson = Flow("NORMAL") };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 0, "Changed queue type bypassed the final context fence.");
            }
        }

        private static async Task ValidateSpecialChampionsAsync()
        {
            var cherry = LeagueAutoSelectProfiles.Serialize(new[] {
                new LeagueAutoSelectProfile { Mode = "cherry", Picks = new List<int> { -3 } } });
            Require(LeagueAutoSelectProfiles.Parse(cherry)[0].Picks.Single() == -3 &&
                    LeagueAutoSelectProfiles.Parse("[{\"Mode\":\"aram\",\"Picks\":[-3]}]").Count == 0 &&
                    LeagueAutoSelectProfiles.Parse("[{\"Mode\":\"ranked\",\"Bans\":[-3]}]").Count == 0,
                "Arena bravery must not be accepted outside its pick-only group.");
            var selection = SessionWithRole("pick", 12, "TOP");
            var arenaFlow = "{\"phase\":\"ChampSelect\",\"gameData\":{\"queue\":{\"id\":1700,\"gameMode\":\"CHERRY\",\"type\":\"NORMAL\"}}}";
            var reader = new FakeRead { SessionJson = selection, GameflowJson = arenaFlow, Pickable = "[22]" };
            var writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, cherry);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.SequenceEqual(new[] { "pick:12:-3:True" }),
                    "Arena bravery must be available without a normal champion grid ID.");
            }
            reader = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"), Pickable = "[22]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, cherry);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 0, "Arena bravery escaped its gameflow context.");
            }
            var noBan = LeagueAutoSelectProfiles.Serialize(new[] {
                new LeagueAutoSelectProfile { Mode = "ranked", Role = "top", Bans = new List<int> { -1 } } });
            Require(LeagueAutoSelectProfiles.Parse(noBan)[0].Bans.Single() == -1 &&
                    LeagueAutoPickBanController.IsAvailable(Bytes("[-1,22]"), -1, "ban", "ranked") &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("[22]"), -1, "ban", "ranked") &&
                    !LeagueAutoPickBanController.IsAvailable(Bytes("[-1]"), -1, "pick", "ranked"),
                "Empty ban must require explicit LCU bannable eligibility.");
            reader = new FakeRead { SessionJson = SessionWithRole("ban", 12, "TOP"),
                GameflowJson = Flow("RANKED_SOLO_5x5"), Bannable = "[-1,22]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(false, 0, true, 0, noBan);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.SequenceEqual(new[] { "ban:12:-1:True" }),
                    "Explicit empty ban was not submitted in an eligible turn.");
            }
            reader = new FakeRead { SessionJson = SessionWithRole("ban", 12, "TOP"),
                GameflowJson = Flow("RANKED_SOLO_5x5"), Bannable = "[22]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(false, 0, true, 0, noBan);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 0, "Empty ban bypassed server bannable eligibility.");
            }
        }

        private static async Task ValidateSubsetPicksAsync()
        {
            var profiles = LeagueAutoSelectProfiles.Serialize(new[] {
                new LeagueAutoSelectProfile { Mode = "ranked", Role = "top", Picks = new List<int> { 67, 22 } } });
            var selection = "{\"allowSubsetChampionPicks\":true," + SessionWithRole("pick", 12, "TOP").Substring(1);
            var reader = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"),
                Pickable = "[22,67]", Subset = "[22]" };
            var writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.SequenceEqual(new[] { "pick:12:22:True" }) &&
                    reader.Paths.Count(path => path == LeagueAutoPickBanController.SubsetPath) == 2,
                    "Subset pick must choose only an eligible offered champion and revalidate before write.");
            }
            var showOnly = LeagueAutoSelectProfiles.Serialize(new[] {
                new LeagueAutoSelectProfile { Mode = "ranked", Role = "top", Picks = new List<int> { 22 },
                    PickStrategy = "just-show" } });
            reader = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"),
                Pickable = "[22,67]", Subset = "[22]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, showOnly);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.SequenceEqual(new[] { "pick:12:22:True" }),
                    "Subset draw must finalize even when the ordinary strategy is show-only.");
            }
            reader = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"),
                Pickable = "[22,67]", Subset = "[]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 0, "Missing subset eligibility must fail closed.");
            }
            reader = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"),
                Pickable = "[22,67]", Subset = "[22]", NextSubset = "[67]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.Count == 0, "Changed subset must cancel a stale pick before write.");
            }
            reader = new FakeRead { SessionJson = SessionWithRole("pick", 12, "TOP"),
                GameflowJson = Flow("RANKED_SOLO_5x5"), Pickable = "[22,67]", Subset = "[]" };
            writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.Calls.SequenceEqual(new[] { "pick:12:67:True" }) &&
                    !reader.Paths.Contains(LeagueAutoPickBanController.SubsetPath),
                    "Ordinary drafts must not add subset requests or restrict normal picks.");
            }
        }

        private static void ValidateProfiles()
        {
            var profiles = new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67, 22 },
                    Bans = new List<int> { 99 }, PickStrategy = "just-show", PickDelayMs = 1500 },
                new LeagueAutoSelectProfile { Mode = "aram", Role = "default", Picks = new List<int> { 157 } }
            };
            var encoded = LeagueAutoSelectProfiles.Serialize(profiles);
            var decoded = LeagueAutoSelectProfiles.Parse(encoded);
            Require(decoded.Count == 2 &&
                decoded[0].Mode == "rift" && decoded[0].Role == "top" &&
                decoded[0].Picks.SequenceEqual(new[] { 67, 22 }) &&
                decoded[0].Bans.SequenceEqual(new[] { 99 }) &&
                decoded[0].PickStrategy == "just-show" && decoded[0].PickDelayMs == 1500 &&
                decoded[1].Mode == "aram" && decoded[1].Role == "default" &&
                decoded[1].Picks.SequenceEqual(new[] { 157 }) &&
                decoded[1].Bans.Count == 0 &&
                LeagueAutoSelectProfiles.Parse(LeagueAutoSelectProfiles.Serialize(decoded)).Count == 2,
                "Priority profiles must round-trip without order loss.");
            Require(LeagueAutoSelectProfiles.ModeFor(420, "CLASSIC") == "rift" &&
                    LeagueAutoSelectProfiles.ModeFor(450, "ARAM") == "aram" &&
                    LeagueAutoSelectProfiles.ModeFor(3270, "ARAM") == "mayhem",
                "Auto-select modes must use the existing ARAM/Mayhem policy.");
            Require(LeagueAutoPickBanController.ParseCurrentAction(Bytes(SessionWithRole("pick", 6, "UTILITY")))?.Role
                    == "support" &&
                    LeagueAutoPickBanController.ParseCurrentAction(Bytes(SessionWithRole("pick", 6, "FILL")))?.Role
                    == null,
                "Only a known locally assigned lane is valid for role-specific profiles.");
            var fallback = LeagueAutoSelectProfiles.Candidates(decoded, "rift", "top", true, 2);
            Require(fallback.Count == 3 && fallback[0] == 67 && fallback[1] == 22 && fallback[2] == 2,
                "Lane-specific priority and basic-choice fallback are not ordered.");
            var aram = LeagueAutoSelectProfiles.Candidates(decoded, "aram", "default", true, 2);
            Require(aram.Count == 2 && aram[0] == 157 && aram[1] == 2,
                "ARAM profile must not leak Rift lane preferences.");
            Require(LeagueAutoSelectProfiles.ResolveStrategy(decoded, "rift", "top").PickStrategy == "just-show" &&
                    LeagueAutoSelectProfiles.Parse("[{\"Mode\":\"unsupported\"}]").Count == 0,
                "Profile strategy selection or malformed-input rejection failed.");
            Require(!decoded[1].BenchAutoSwapEnabled && decoded[1].BenchSwapDelayMs == 2900,
                "Automatic bench must stay opt-in and preserve its delay default on profile round-trip.");
            Require(LeagueAutoSelectProfiles.Parse("[{\"Mode\":\"aram\",\"Role\":\"default\"," +
                "\"BenchAutoSwapEnabled\":true,\"BenchSwapDelayMs\":20000}]").Count == 0,
                "Unbounded bench delays must be rejected before saving.");
        }

        private static async Task ValidateAutoBenchAsync()
        {
            Require(LeagueAutoPickBanController.SelectBenchTarget(new[] { 55, 34, 22 }, 34,
                        new[] { 55 }, true) == 55 &&
                    LeagueAutoPickBanController.SelectBenchTarget(new[] { 55, 34, 22 }, 34,
                        new[] { 22 }, true) == 0 &&
                    LeagueAutoPickBanController.SelectBenchTarget(new[] { 55, 34, 22 }, 34,
                        new[] { 55 }, false) == 0 &&
                    LeagueAutoPickBanController.SelectBenchTarget(new[] { 55, 34, 22 }, 99,
                        new[] { 55 }, false) == 55,
                "Automatic bench priority must protect the current ranked preference.");
            var guard = new LeagueAutoBenchEpisodeGuard();
            var tick = System.Diagnostics.Stopwatch.Frequency;
            guard.ObserveBench(new[] { 55, 22 }, tick);
            Require(guard.RemainingCooldownMs(55, 2900, tick) == 2900 &&
                    guard.RemainingCooldownMs(55, 2900, tick + tick) == 1900,
                "Bench cooldown must count from the first observed appearance, not each poll.");
            guard.ObserveBench(new[] { 55, 34 }, tick + tick);
            Require(guard.RemainingCooldownMs(55, 2900, tick + tick * 2) == 900 &&
                    guard.RemainingCooldownMs(34, 2900, tick + tick * 2) == 1900 &&
                    guard.RemainingCooldownMs(22, 2900, tick + tick * 2) == 2900,
                "An already-visible bench champion must retain its age; a new one starts fresh.");
            guard.ObserveBench(new[] { 34 }, tick + tick * 2);
            guard.ObserveBench(new[] { 55, 34 }, tick + tick * 3);
            Require(guard.RemainingCooldownMs(55, 2900, tick + tick * 3) == 2900 &&
                    guard.RemainingCooldownMs(34, 2900, tick + tick * 3) == 900 &&
                    guard.RemainingCooldownMs(55, 0, tick + tick * 3) == 0,
                "Removing and returning a champion must restart only its own cooldown.");
            Require(guard.AcceptObservedChampion(34), "First held champion must establish an episode baseline.");
            guard.AcceptAutomaticSwap(55);
            Require(guard.AcceptObservedChampion(55) && !guard.AcceptObservedChampion(22) &&
                    !guard.AcceptObservedChampion(55), "Manual swap must block all later automatic writes.");

            var profiles = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "aram", Picks = new List<int> { 55, 34 },
                    BenchAutoSwapEnabled = true, BenchPreferHigherPriority = true, BenchSwapDelayMs = 0 }
            });
            var read = new FakeRead { SessionJson = BenchSession(34, "[55]", 450, false) };
            var benchWriter = new FakeBenchWrite { AfterWrite = id => read.SessionJson = BenchSession(id, "[34]", 450, false) };
            using (var controller = new LeagueAutoPickBanController(
                read, new FakeWrite(), benchWriter, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(benchWriter.Calls == 1 && benchWriter.LastId == 55 &&
                        benchWriter.Route == LeagueBenchSwapRoute.Legacy && read.ActionPaths.Count == 3,
                    "Opt-in ARAM bench swap must preflight, submit once and read back on the same task.");
            }

            var disabled = new FakeBenchWrite();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = BenchSession(34, "[55]", 450, false) },
                new FakeWrite(), disabled, new InstantClock()))
            {
                controller.Configure(true, 34, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(disabled.Calls == 0, "No automatic bench writes without an explicit enabled profile.");
            }

            var changed = new FakeRead { SessionJson = BenchSession(34, "[55]", 450, false),
                NextSessionJson = BenchSession(99, "[55]", 450, false) };
            var cancelledWriter = new FakeBenchWrite();
            using (var controller = new LeagueAutoPickBanController(
                changed, new FakeWrite(), cancelledWriter, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(cancelledWriter.Calls == 0,
                    "A manual hero change between initial bench scan and POST must cancel the swap.");
            }

            var pendingProfile = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "aram", Picks = new List<int> { 55, 34 },
                    BenchAutoSwapEnabled = true, BenchPreferHigherPriority = true, BenchSwapDelayMs = 5000 }
            });
            var pendingClock = new BenchPendingClock();
            var pendingWriter = new FakeBenchWrite();
            using (var cancellation = new CancellationTokenSource())
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = BenchSession(34, "[55]", 450, false) },
                new FakeWrite(), pendingWriter, pendingClock))
            {
                controller.Configure(true, 0, false, 0, pendingProfile);
                var operation = controller.RunOnceForSmokeTestAsync(cancellation.Token);
                Require(pendingClock.Scheduled && pendingWriter.Calls == 0,
                    "Bench cooldown must wait before any irreversible write.");
                cancellation.Cancel();
                try { await operation.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                Require(pendingClock.Cancelled && pendingWriter.Calls == 0,
                    "Canceling a queued bench swap must prevent the POST.");
            }

            var teamProfiles = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "mayhem", Picks = new List<int> { 55, 34 },
                    BenchAutoSwapEnabled = true, BenchPreferHigherPriority = true, BenchSwapDelayMs = 0 }
            });
            var team = new FakeRead
            {
                SessionJson = BenchSession(34, "[]", 3270, true),
                TeamBuilderJson = BenchSession(34, "[55]", 0, true)
            };
            var teamWriter = new FakeBenchWrite { AfterWrite = id =>
                {
                    team.SessionJson = BenchSession(id, "[]", 3270, true);
                    team.TeamBuilderJson = BenchSession(id, "[34]", 0, true);
                } };
            using (var controller = new LeagueAutoPickBanController(
                team, new FakeWrite(), teamWriter, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, teamProfiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(teamWriter.Calls == 1 && teamWriter.Route == LeagueBenchSwapRoute.TeamBuilder &&
                        team.Paths.FindAll(x => x == LeagueLiveDataService.TeamBuilderChampSelectSessionPath).Count == 3,
                    "Tencent Mayhem must recover the bench via its Team Builder read/write route.");
            }
        }

        private static async Task ValidateChampionTradesAsync()
        {
            Require(LeagueBenchSwapWriteApiClient.BuildTradePathForSmokeTest(73, true) ==
                    "/lol-champ-select/v1/session/champion-swaps/73/accept" &&
                    LeagueBenchSwapWriteApiClient.BuildTradePathForSmokeTest(73, false) ==
                    "/lol-champ-select/v1/session/champion-swaps/73/decline" &&
                    LeagueBenchSwapWriteApiClient.BuildTradePathForSmokeTest(0, true).Length == 0,
                "Champion trade writer must keep its fixed authenticated POST routes.");
            Require(LeagueAutoPickBanController.ParseIncomingChampionTrade(Bytes(Trade(73, 55)))?.Id == 73 &&
                    LeagueAutoPickBanController.ParseIncomingChampionTrade(Bytes(Trade(73, 55).Replace(
                        "\"RECEIVED\"", "\"ACCEPTED\""))) == null &&
                    LeagueAutoPickBanController.ParseIncomingChampionTrade(Bytes(Trade(73, 55).Replace(
                        "\"initiatedByLocalPlayer\":false", "\"initiatedByLocalPlayer\":true"))) == null &&
                    LeagueAutoPickBanController.ParseIncomingChampionTrade(Bytes("{}")) == null,
                "Only an incoming, active CHAMPION swap is eligible for automation.");

            var profiles = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "aram", Picks = new List<int> { 55, 34, 22 },
                    BenchHandleTradeEnabled = true, BenchPreferHigherPriority = true, PickDelayMs = 0 }
            });
            var incoming = new FakeRead { SessionJson = BenchSession(34, "[]", 450, false),
                TradeJson = Trade(73, 55) };
            var accepted = new FakeTradeWrite();
            using (var controller = new LeagueAutoPickBanController(
                incoming, new FakeWrite(), null, accepted, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(accepted.Calls == 1 && accepted.Id == 73 && accepted.Accept &&
                        incoming.ActionPaths.Count == 4,
                    "An offered higher-priority hero should be accepted after two guarded reads.");
            }

            var undesirable = new FakeRead { SessionJson = BenchSession(34, "[]", 450, false),
                TradeJson = Trade(74, 22) };
            var declined = new FakeTradeWrite();
            using (var controller = new LeagueAutoPickBanController(
                undesirable, new FakeWrite(), null, declined, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(declined.Calls == 1 && declined.Id == 74 && !declined.Accept,
                    "An offered lower-priority hero should be declined with explicit opt-in.");
            }

            var changed = new FakeRead { SessionJson = BenchSession(34, "[]", 450, false),
                TradeJson = Trade(75, 55), NextTradeJson = Trade(76, 55) };
            var blocked = new FakeTradeWrite();
            using (var controller = new LeagueAutoPickBanController(
                changed, new FakeWrite(), null, blocked, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(blocked.Calls == 0,
                    "A changed incoming trade ID between plan and write must cancel automation.");
            }

            var defaultOff = new FakeRead { SessionJson = BenchSession(34, "[]", 450, false),
                TradeJson = Trade(77, 55) };
            using (var controller = new LeagueAutoPickBanController(
                defaultOff, new FakeWrite(), null, new FakeTradeWrite(), new InstantClock()))
            {
                controller.Configure(true, 34, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(!defaultOff.Paths.Contains(LeagueAutoPickBanController.OngoingChampionTradePath),
                    "Disabled trade automation must not poll the extra LCU endpoint.");
            }
        }

        private static async Task ValidatePickAndBanAsync()
        {
            var pickRead = new FakeRead { SessionJson = Session("pick", 0, 0) };
            var pickWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(pickRead, pickWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(pickWrite.Calls.Count == 1 && pickWrite.Calls[0] == "pick:0:22:True",
                    "Pick write must only target the active local action.");
                Require(pickRead.ActionPaths.Count == 3 && pickRead.ActionPaths[1] == LeagueAutoPickBanController.PickablePath,
                    "Pick must read active session, pickable list and live preflight.");
            }

            var banRead = new FakeRead { SessionJson = Session("ban", 4, 0) };
            var banWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(banRead, banWrite, new FakeClock()))
            {
                controller.Configure(false, 0, true, 67);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(banWrite.Calls.Count == 1 && banWrite.Calls[0] == "ban:4:67:True",
                    "Ban write must only target the active local action.");
                Require(banRead.ActionPaths.Count == 3 && banRead.ActionPaths[1] == LeagueAutoPickBanController.BannablePath,
                    "Ban must read the bannable list, not the pickable list.");
            }

            var manualRead = new FakeRead { SessionJson = Session("pick", 3, 67) };
            var manualWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(manualRead, manualWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(manualWrite.Calls.Count == 0 && manualRead.ActionPaths.Count == 1,
                    "An existing manually selected different champion must not be overridden.");
            }

            var profiles = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 99, 67 },
                    PickStrategy = "just-show", PickDelayMs = 1000 }
            });
            var priorityRead = new FakeRead { SessionJson = SessionWithRole("pick", 5, "TOP"), Pickable = "[67,22]" };
            var priorityWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(priorityRead, priorityWrite, new InstantClock()))
            {
                controller.Configure(true, 22, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(priorityWrite.Calls.Count == 1 && priorityWrite.Calls[0] == "pick:5:67:False",
                    "Mode/role priority must pick the first available fallback and honor just-show.");
            }

            var avoidIntent = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67, 22 } }
            });
            var reservedWriter = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = SessionWithReservation("pick", 12, 67), Pickable = "[22,67]" },
                reservedWriter, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, avoidIntent);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(reservedWriter.Calls.Count == 1 && reservedWriter.Calls[0] == "pick:12:22:True",
                    "Active auto-pick must avoid a teammate's reserved first-choice champion by default.");
            }

            var newReservation = new FakeRead { SessionJson = SessionWithRole("pick", 13, "TOP"),
                NextSessionJson = SessionWithReservation("pick", 13, 67), Pickable = "[67,22]" };
            var staleIntentWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(newReservation,
                staleIntentWrite, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, avoidIntent);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(staleIntentWrite.Calls.Count == 0,
                    "New teammate intent before lock preflight must cancel the active pick write.");
            }

            var showAndLock = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67 },
                    PickStrategy = "show-and-lock-in" }
            });
            var stagedWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = SessionWithRole("pick", 8, "TOP") }, stagedWrite, new InstantClock()))
            {
                controller.Configure(true, 67, false, 0, showAndLock);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(stagedWrite.Calls.Count == 2 && stagedWrite.Calls[0] == "pick:8:67:False" &&
                        stagedWrite.Calls[1] == "pick:8:67:True",
                    "Show-and-lock must validate and issue two distinct action stages.");
            }

            var manualAfterShow = new FakeRead
            {
                SessionJson = SessionWithRole("pick", 9, "TOP"),
                ThirdSessionJson = SessionWithRole("pick", 9, "TOP").Replace("\"championId\":0", "\"championId\":99")
            };
            var manualAfterShowWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(
                manualAfterShow, manualAfterShowWrite, new InstantClock()))
            {
                controller.Configure(true, 67, false, 0, showAndLock);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(manualAfterShowWrite.Calls.Count == 1 && manualAfterShowWrite.Calls[0] == "pick:9:67:False",
                    "Manual override after showing must cancel the automatic lock stage.");
            }
        }

        private static async Task ValidatePickIntentAsync()
        {
            Require(LeagueChampSelectActionWriteApiClient.IsAllowedIntentForSmokeTest(0, 22) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedIntentForSmokeTest(-1, 22) &&
                    !LeagueChampSelectActionWriteApiClient.IsAllowedIntentForSmokeTest(1, 0),
                "Pick intent must use a validated action target and champion id.");
            Require(LeagueAutoPickBanController.ParsePickIntentAction(Bytes(Planning(0, 0)))?.Id == 10 &&
                    LeagueAutoPickBanController.ParsePickIntentAction(Bytes(Planning(0, 0)
                        .Replace("\"PLANNING\"", "\"FINALIZATION\""))) == null &&
                    LeagueAutoPickBanController.ParsePickIntentAction(Bytes("{}")) == null,
                "Only an unfinished local pick action in planning/selection may receive intent.");

            var profiles = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67, 22 },
                    ShowPickIntent = true, PickDelayMs = 0 }
            });
            var reader = new FakeRead { SessionJson = Planning(0, 67), Pickable = "[22,67]" };
            var writer = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(reader, writer, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(writer.IntentCalls.Count == 1 && writer.IntentCalls[0] == "10:22" &&
                        writer.Calls.Count == 0 && reader.ActionPaths.Count == 4,
                    "Pick intent must respect teammates and issue only an intent payload, not a lock.");
            }

            var ignore = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67, 22 },
                    ShowPickIntent = true, IgnoreTeammateIntent = true }
            });
            var ignoredWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = Planning(0, 67), Pickable = "[22,67]" },
                ignoredWrite, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, ignore);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(ignoredWrite.IntentCalls.Count == 1 && ignoredWrite.IntentCalls[0] == "10:67",
                    "Explicit ignore-intent preference must allow the first available hero.");
            }

            var changed = new FakeRead { SessionJson = Planning(0, 0),
                NextSessionJson = Planning(99, 0) };
            var skipped = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(changed, skipped, new InstantClock()))
            {
                controller.Configure(true, 0, false, 0, profiles);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(skipped.IntentCalls.Count == 0,
                    "A manual intent appearing before preflight must cancel the planned write.");
            }

            var delayed = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 22 },
                    ShowPickIntent = true, PickDelayMs = 5000 }
            });
            var pendingClock = new BenchPendingClock();
            var pendingWrite = new FakeWrite();
            using (var lifetime = new CancellationTokenSource())
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = Planning(0, 0), Pickable = "[22]" },
                pendingWrite, pendingClock))
            {
                controller.Configure(true, 0, false, 0, delayed);
                var operation = controller.RunOnceForSmokeTestAsync(lifetime.Token);
                Require(pendingClock.Scheduled, "Intent delay must be cancellable before writing.");
                lifetime.Cancel();
                try { await operation.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                Require(pendingClock.Cancelled && pendingWrite.IntentCalls.Count == 0,
                    "Cancelled intent must never PATCH the future pick action.");
            }
        }

        private static async Task ValidateTransientEligibilityRecoveryAsync()
        {
            var read = new FakeRead { SessionJson = Session("pick", 81, 0),
                Pickable = "[]", NextPickable = "[22]" };
            var writer = new FakeWrite();
            var clock = new TwoStepClock();
            using (var controller = new LeagueAutoPickBanController(read, writer, clock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                Require(clock.Delays == 2 && writer.Calls.SequenceEqual(new[] { "pick:81:22:True" }),
                    "A temporarily empty pickable list must recover on the next phase-scoped tick.");
                var pending = controller.RunningForSmokeTest;
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                await pending.ConfigureAwait(false);
                Require(clock.Cancelled && writer.Calls.Count == 1,
                    "Eligibility recovery must stop on phase exit without repeating the write.");
            }

            var selection = "{\"allowSubsetChampionPicks\":true," + SessionWithRole("pick", 82, "TOP").Substring(1);
            read = new FakeRead { SessionJson = selection, GameflowJson = Flow("RANKED_SOLO_5x5"),
                Pickable = "[22]", Subset = "[]", NextSubset = "[22]" };
            writer = new FakeWrite();
            clock = new TwoStepClock();
            using (var controller = new LeagueAutoPickBanController(read, writer, clock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                Require(clock.Delays == 2 && writer.Calls.SequenceEqual(new[] { "pick:82:22:True" }) &&
                    read.Paths.Count(path => path == LeagueAutoPickBanController.SubsetPath) == 3,
                    "Subset eligibility must recover without bypassing the fresh subset preflight.");
                var pending = controller.RunningForSmokeTest;
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                await pending.ConfigureAwait(false);
                Require(clock.Cancelled && writer.Calls.Count == 1,
                    "Subset recovery must remain bounded to the active draft.");
            }
        }

        private static async Task ValidateFailureAndCancellationAsync()
        {
            var idleClock = new FakeClock();
            var idleRead = new FakeRead { SessionJson = Session("pick", 1, 0) };
            var idleWrite = new FakeWrite();
            using (var idle = new LeagueAutoPickBanController(idleRead, idleWrite, idleClock))
            {
                idle.Observe(Selecting());
                Require(idleRead.ActionPaths.Count == 0 && idleClock.Delays == 0 && idleWrite.Calls.Count == 0,
                    "Disabled automation must not start a network poller.");
            }

            var pauseClock = new FakeClock();
            var pauseRead = new FakeRead { SessionJson = Session("pick", 1, 0) };
            var pauseWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(pauseRead, pauseWrite, pauseClock))
            {
                controller.SetTemporarilyDisabled(true);
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                Require(pauseRead.ActionPaths.Count == 0 && pauseWrite.Calls.Count == 0,
                    "Temporary pause must suppress LCU work without clearing saved preferences.");
                controller.SetTemporarilyDisabled(false);
                var resumed = controller.RunningForSmokeTest;
                Require(pauseWrite.Calls.Count == 1 && pauseClock.Delays == 1,
                    "Resuming during ChampSelect must rearm the existing task once.");
                controller.SetTemporarilyDisabled(true);
                await resumed.ConfigureAwait(false);
                Require(pauseClock.Cancelled && pauseWrite.Calls.Count == 1,
                    "Pausing again must cancel further selector ticks.");
            }

            var changed = new FakeRead
            {
                SessionJson = Session("pick", 2, 0),
                NextSessionJson = Session("pick", 2, 99)
            };
            var changedWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(changed, changedWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(changedWrite.Calls.Count == 0, "Preflight must respect a later manual champion change.");
            }

            var unavailable = new FakeRead { SessionJson = Session("pick", 2, 0), Pickable = "[67]" };
            var unavailableWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(unavailable, unavailableWrite, new FakeClock()))
            {
                controller.Configure(true, 22, false, 0);
                await controller.RunOnceForSmokeTestAsync(CancellationToken.None).ConfigureAwait(false);
                Require(unavailableWrite.Calls.Count == 0, "Unavailable champion must not be selected.");
            }

            var clock = new FakeClock();
            var read = new FakeRead { SessionJson = Session("pick", 3, 0) };
            var write = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(read, write, clock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                var task = controller.RunningForSmokeTest;
                controller.Observe(Selecting());
                Require(write.Calls.Count == 1 && clock.Delays == 1,
                    "Repeated Gameflow events must not duplicate the action or polling loop.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                await task.ConfigureAwait(false);
                Require(clock.Cancelled && write.Calls.Count == 1,
                    "Leaving ChampSelect failed to cancel the bounded polling loop.");
            }

            var disabledClock = new FakeClock();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = Session("pick", 9, 0) }, new FakeWrite(), disabledClock))
            {
                controller.Configure(true, 22, false, 0);
                controller.Observe(Selecting());
                var task = controller.RunningForSmokeTest;
                controller.Configure(false, 22, false, 0);
                await task.ConfigureAwait(false);
                Require(disabledClock.Cancelled, "Disabling the setting must cancel the active task.");
            }

            var delayedProfile = LeagueAutoSelectProfiles.Serialize(new[]
            {
                new LeagueAutoSelectProfile { Mode = "rift", Role = "top", Picks = new List<int> { 67 },
                    PickStrategy = "show-and-lock-in", PickDelayMs = 2000 }
            });
            var delayedClock = new CalibratedPendingClock();
            var delayedWrite = new FakeWrite();
            using (var controller = new LeagueAutoPickBanController(
                new FakeRead { SessionJson = SessionWithRole("pick", 11, "TOP").Replace("\"adjustedTimeLeftInPhase\":20000", "\"adjustedTimeLeftInPhase\":28000,\"totalTimeInPhase\":30000,\"internalNowInEpochMs\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) }, delayedWrite, delayedClock))
            {
                controller.Configure(true, 67, false, 0, delayedProfile);
                controller.Observe(Selecting());
                var pending = controller.RunningForSmokeTest;
                Require(delayedWrite.Calls.Count == 1 && delayedWrite.Calls[0] == "pick:11:67:False",
                    "Show-and-lock should first issue an uncompleted choice.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                await pending.ConfigureAwait(false);
                Require(delayedClock.Cancelled && delayedWrite.Calls.Count == 1,
                    "Changing phase after showing must not issue a delayed lock.");
            }
        }

        private static string Session(string type, int id, int championId,
            bool inProgress = true, string phase = "BAN_PICK", bool completed = false)
        {
            return "{\"timer\":{\"phase\":\"" + phase + "\"},\"localPlayerCellId\":0,\"actions\":[" +
                   "[{\"id\":" + id + ",\"actorCellId\":0,\"type\":\"" + type +
                   "\",\"championId\":" + championId + ",\"isInProgress\":" +
                   (inProgress ? "true" : "false") + ",\"completed\":" +
                   (completed ? "true" : "false") + "}]]}";
        }

        private static string SessionWithRole(string type, int id, string role)
        {
            return "{\"timer\":{\"phase\":\"BAN_PICK\",\"adjustedTimeLeftInPhase\":20000}," +
                   "\"queueId\":420,\"gameMode\":\"CLASSIC\",\"localPlayerCellId\":0," +
                   "\"myTeam\":[{\"cellId\":0,\"assignedPosition\":\"" + role + "\"}]," +
                   "\"actions\":[[{\"id\":" + id + ",\"actorCellId\":0,\"type\":\"" + type +
                   "\",\"championId\":0,\"isInProgress\":true,\"completed\":false}]]}";
        }

        private static string Planning(int intendedId, int teammateIntent)
        {
            return "{\"timer\":{\"phase\":\"PLANNING\",\"adjustedTimeLeftInPhase\":20000}," +
                "\"queueId\":420,\"gameMode\":\"CLASSIC\",\"localPlayerCellId\":0," +
                "\"myTeam\":[{\"cellId\":0,\"assignedPosition\":\"TOP\"}," +
                "{\"cellId\":1,\"championPickIntent\":" + teammateIntent + "}]," +
                "\"actions\":[[{\"id\":10,\"actorCellId\":0,\"type\":\"pick\"," +
                "\"championId\":" + intendedId + ",\"isInProgress\":false,\"completed\":false}]]}";
        }

        private static string SessionWithReservation(string type, int id, int reservedId)
        {
            return SessionWithRole(type, id, "TOP").Replace(
                "\"assignedPosition\":\"TOP\"}",
                "\"assignedPosition\":\"TOP\"},{\"cellId\":1,\"championPickIntent\":" + reservedId + "}");
        }

        private static string BenchSession(int held, string benchIds, int queue, bool teamBuilder)
        {
            return "{\"timer\":{\"phase\":\"FINALIZATION\",\"adjustedTimeLeftInPhase\":18000}," +
                "\"queueId\":" + queue + ",\"gameMode\":\"" + (queue == 3270 ? "KIWI" : queue == 0 ? "" : "ARAM") +
                "\",\"benchEnabled\":true,\"isLegacyChampSelect\":" + (teamBuilder ? "false" : "true") +
                ",\"localPlayerCellId\":0,\"myTeam\":[{\"cellId\":0,\"championId\":" + held +
                "}],\"benchChampionIds\":" + benchIds + ",\"actions\":[]} ";
        }

        private static string Trade(int id, int requestedId)
        {
            return "{\"id\":" + id + ",\"type\":\"CHAMPION\",\"state\":\"RECEIVED\"," +
                "\"initiatedByLocalPlayer\":false,\"requesterChampionId\":" + requestedId + "}";
        }

        private static LeagueDashboardPhaseState Selecting()
        {
            return new LeagueDashboardPhaseState
            {
                Connected = true, Phase = "ChampSelect", Activity = LeagueActivityLevel.ChampSelect
            };
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private sealed class FakeRead : ILeagueClientApi
        {
            public string SessionJson;
            public string GameflowJson;
            public string NextGameflowJson;
            private string _lastSession;
            private int _gameflowCount;
            public List<string> ActionPaths { get { return Paths.Where(p => p != LeagueLiveDataService.GameflowSessionPath).ToList(); } }
            public string NextSessionJson;
            public string ThirdSessionJson;
            public string TeamBuilderJson;
            public string TradeJson;
            public string NextTradeJson;
            public string Pickable = "[22,67]";
            public string NextPickable;
            private int _pickableCount;
            public string Bannable = "[22,67]";
            public string Subset = "[22,67]";
            public string NextSubset;
            private int _subsetCount;
            public readonly List<string> Paths = new List<string>();
            private int _sessionCount;
            private int _tradeCount;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Paths.Add(path);
                if (path == LeagueAutoPickBanController.SessionPath)
                {
                    _sessionCount++;
                    var next = _sessionCount > 2 && ThirdSessionJson != null ? ThirdSessionJson :
                        _sessionCount > 1 && NextSessionJson != null ? NextSessionJson : SessionJson;
                    _lastSession = next;
                    return Task.FromResult(Bytes(next));
                }
                if (path == LeagueLiveDataService.GameflowSessionPath)
                {
                    _gameflowCount++;
                    if (GameflowJson != null) return Task.FromResult(Bytes(_gameflowCount > 1 && NextGameflowJson != null ? NextGameflowJson : GameflowJson));
                    var root = new JavaScriptSerializer().DeserializeObject(_lastSession ?? "{}") as Dictionary<string, object>;
                    var mode = LeagueAutoSelectContext.Text(root, "gameMode");
                    var id = LeagueAutoSelectContext.Text(root, "queueId");
                    if (string.IsNullOrEmpty(mode)) mode = "CLASSIC";
                    if (string.IsNullOrEmpty(id)) id = "420";
                    var type = id == "420" ? "RANKED_SOLO_5x5" : id == "440" ? "RANKED_FLEX_SR" : "NORMAL";
                    return Task.FromResult(Bytes("{\"phase\":\"ChampSelect\",\"gameData\":{\"isCustomGame\":false,\"queue\":{\"id\":" + id + ",\"gameMode\":\"" + mode + "\",\"type\":\"" + type + "\"}}}"));
                }
                if (path == LeagueAutoPickBanController.PickablePath)
                {
                    _pickableCount++;
                    return Task.FromResult(Bytes(_pickableCount > 1 && NextPickable != null ? NextPickable : Pickable));
                }
                if (path == LeagueAutoPickBanController.BannablePath) return Task.FromResult(Bytes(Bannable));
                if (path == LeagueAutoPickBanController.SubsetPath)
                {
                    _subsetCount++;
                    return Task.FromResult(Bytes(_subsetCount > 1 && NextSubset != null ? NextSubset : Subset));
                }
                if (path == LeagueLiveDataService.TeamBuilderChampSelectSessionPath)
                    return Task.FromResult(Bytes(TeamBuilderJson ?? "{}"));
                if (path == LeagueAutoPickBanController.OngoingChampionTradePath)
                {
                    _tradeCount++;
                    return Task.FromResult(Bytes(_tradeCount > 1 && NextTradeJson != null
                        ? NextTradeJson : TradeJson ?? "{}"));
                }
                throw new InvalidOperationException("Unexpected auto-select read target.");
            }
        }

        private sealed class FakeBenchWrite : ILeagueBenchSwapWriteApi
        {
            public int Calls;
            public int LastId;
            public LeagueBenchSwapRoute Route;
            public Action<int> AfterWrite;

            public Task<LeagueClientWriteResponse> TrySwapAsync(int championId,
                LeagueBenchSwapRoute route, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                LastId = championId;
                Route = route;
                AfterWrite?.Invoke(championId);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }

        private sealed class FakeTradeWrite : ILeagueChampionTradeWriteApi
        {
            public int Calls;
            public int Id;
            public bool Accept;

            public Task<LeagueClientWriteResponse> TryAnswerAsync(
                int tradeId, bool accept, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                Id = tradeId;
                Accept = accept;
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }

        private sealed class FakeWrite : ILeagueChampSelectActionWriteApi
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> IntentCalls = new List<string>();

            public Task<LeagueClientWriteResponse> TryApplyAsync(
                string type, int id, int championId, bool completed, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(type + ":" + id + ":" + championId + ":" + completed);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }

            public Task<LeagueClientWriteResponse> TryIntentAsync(
                int actionId, int championId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntentCalls.Add(actionId + ":" + championId);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }

        private sealed class CalibratedPendingClock : ILeagueMatchmakingClock
        {
            internal bool Cancelled;
            public Task Delay(TimeSpan delay, CancellationToken token)
            {
                Require(delay.TotalMilliseconds > 0 && delay.TotalMilliseconds <= 2000, "Staged delay escaped the configured phase budget.");
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => { Cancelled = true; pending.TrySetCanceled(); });
                return pending.Task;
            }
        }

        private sealed class FakeClock : ILeagueMatchmakingClock
        {
            public int Delays;
            public bool Cancelled;

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                Require(delay == LeagueAutoPickBanController.PollInterval, "Unexpected auto-select polling cadence.");
                Delays++;
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    Cancelled = true;
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }

        private sealed class TwoStepClock : ILeagueMatchmakingClock
        {
            public int Delays;
            public bool Cancelled;
            public Task Delay(TimeSpan delay, CancellationToken token)
            {
                Require(delay == LeagueAutoPickBanController.PollInterval,
                    "Eligibility recovery must not create an extra polling cadence.");
                Delays++;
                if (Delays == 1) return Task.FromResult(0);
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => { Cancelled = true; pending.TrySetCanceled(); });
                return pending.Task;
            }
        }

        private sealed class InstantClock : ILeagueMatchmakingClock
        {
            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }
        }

        private sealed class BenchPendingClock : ILeagueMatchmakingClock
        {
            public bool Scheduled;
            public bool Cancelled;

            public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
            {
                Require(delay == TimeSpan.FromSeconds(5), "Unexpected bench cooldown or network retry.");
                Scheduled = true;
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationToken.Register(() =>
                {
                    Cancelled = true;
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }
    }
}
