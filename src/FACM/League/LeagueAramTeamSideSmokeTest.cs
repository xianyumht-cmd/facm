using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueAramTeamSideSmokeTest
    {
        private const string ConversationId = "lol-champ-select@CN1-episode-1";

        internal static void Validate()
        {
            ValidateSettings();
            ValidateParsingAndWriteFence();
            ValidateDisabledAndWrongMode();
            ValidateOncePerEpisodeAndVisibility();
            ValidateStaleAndCancelled();
        }

        private static void ValidateSettings()
        {
            var settings = new AppSettings();
            Require(!settings.LeagueAutoAramTeamSideEnabled && !settings.LeagueAutoAramTeamSideVisibleToTeam,
                "ARAM side reminder must default off and local-only.");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoAramTeamSideEnabled=True");
            AppSettings.ApplyLineForSmokeTest(settings, "LeagueAutoAramTeamSideVisibleToTeam=True");
            var restored = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(restored.LeagueAutoAramTeamSideEnabled && restored.LeagueAutoAramTeamSideVisibleToTeam,
                "ARAM side reminder did not survive local settings roundtrip.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var target = new AppSettings();
            cloud.ApplyTo(target);
            Require(target.LeagueAutoAramTeamSideEnabled && target.LeagueAutoAramTeamSideVisibleToTeam,
                "ARAM side reminder did not survive cloud settings roundtrip.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
        }

        private static void ValidateParsingAndWriteFence()
        {
            Require(LeagueAramTeamSideController.ParseMode(Bytes(Gameflow("ARAM"))) == "ARAM" &&
                LeagueAramTeamSideController.ParseMode(Bytes(Gameflow("KIWI"))) == "KIWI" &&
                LeagueAramTeamSideController.ParseMode(Bytes(Gameflow("CLASSIC"))) == null,
                "Team-side mode fence accepted an unexpected mode.");
            Require(LeagueAramTeamSideController.ParseTeam(Bytes(ChampSelect(2))) == 2 &&
                LeagueAramTeamSideController.ParseTeam(Bytes(ChampSelect(1))) == 1 &&
                LeagueAramTeamSideController.ParseTeam(Bytes(ChampSelect(4))) == 0 &&
                LeagueAramTeamSideController.ParseTeam(Bytes(ChampSelect(2).Replace("true", "false"))) == 0,
                "Team-side parser accepted ambiguous or ineligible champ select.");
            Require(LeagueAramTeamSideController.ParseConversation(Bytes(Conversations(ConversationId))) == ConversationId &&
                LeagueAramTeamSideController.ParseConversation(Bytes(Conversations("../../bad"))) == null &&
                LeagueAramTeamSideController.ParseConversation(Bytes("[{\"type\":\"customGame\",\"id\":\"" + ConversationId + "\"}]")) == null,
                "Team-side conversation validation failed.");
            var local = LeagueMatchmakingWriteApiClient.BuildSideMessageJson(ConversationId, "左下侧", false);
            var team = LeagueMatchmakingWriteApiClient.BuildSideMessageJson(ConversationId, "左下侧", true);
            Require(local.Contains("\"type\":\"celebration\"") && team.Contains("\"type\":\"chat\""),
                "ARAM visibility options must use different LCU chat message types.");
            Require(!LeagueMatchmakingWriteApiClient.IsSafeChampionSelectConversationId("lol-champ-select/../../oops") &&
                !LeagueMatchmakingWriteApiClient.IsSafeChampionSelectConversationId("lobby@CN1"),
                "ARAM writer accepted unsafe/non-champ-select target.");
        }

        private static void ValidateDisabledAndWrongMode()
        {
            var read = CreateRead("ARAM", 1);
            var write = new Write();
            using (var controller = new LeagueAramTeamSideController(read, write))
            {
                Run(controller);
                Require(read.Reads == 0 && write.Calls.Count == 0, "Disabled ARAM reminder performed LCU work.");
                read.Set(LeagueAramTeamSideController.GameflowPath, Gameflow("CLASSIC"));
                controller.Configure(true, false);
                Run(controller);
                Require(write.Calls.Count == 0, "Non-ARAM mode received team-side notification.");
            }
        }

        private static void ValidateOncePerEpisodeAndVisibility()
        {
            var read = CreateRead("KIWI", 1);
            var write = new Write();
            using (var controller = new LeagueAramTeamSideController(read, write))
            {
                controller.Configure(true, false);
                Run(controller);
                Run(controller);
                Require(write.Calls.Count == 1 && write.Calls[0].Contains("蓝色方") && !write.Visible[0],
                    "Private ARAM side reminder must send once per champion-select conversation.");
                Require(read.Reads == 6, "A completed reminder must stop further LCU reads within the episode.");
                controller.Configure(true, true);
                Run(controller);
                Require(write.Calls.Count == 1, "Visibility toggle must not send a duplicate.");
                controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                read.Set(LeagueAramTeamSideController.ChampSelectPath, ChampSelect(2));
                read.Set(LeagueAramTeamSideController.ConversationsPath,
                    Conversations("lol-champ-select@CN1-episode-2"));
                Run(controller);
                Require(write.Calls.Count == 2 && write.Calls[1].Contains("红色方") && write.Visible[1],
                    "New ARAM episode must report red side with the configured public visibility.");
            }
        }

        private static void ValidateStaleAndCancelled()
        {
            var read = CreateRead("ARAM", 2);
            var write = new Write();
            using (var controller = new LeagueAramTeamSideController(read, write))
            {
                read.BeforeRead = (path, count) =>
                {
                    if (path == LeagueAramTeamSideController.ChampSelectPath && count == 2)
                        read.Set(path, ChampSelect(1));
                };
                controller.Configure(true, true);
                Run(controller);
                Require(write.Calls.Count == 0, "Side change during LCU read must suppress stale message.");
            }
            read = CreateRead("ARAM", 2);
            write = new Write();
            using (var controller = new LeagueAramTeamSideController(read, write))
            {
                read.BeforeRead = (path, count) =>
                {
                    if (path == LeagueAramTeamSideController.ConversationsPath && count == 2)
                        controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                };
                controller.Configure(true, true);
                Run(controller);
                Require(write.Calls.Count == 0, "Leaving champion select must cancel its in-flight message.");
            }
        }

        private static Read CreateRead(string mode, int team)
        {
            var result = new Read();
            result.Set(LeagueAramTeamSideController.GameflowPath, Gameflow(mode));
            result.Set(LeagueAramTeamSideController.ChampSelectPath, ChampSelect(team));
            result.Set(LeagueAramTeamSideController.ConversationsPath, Conversations(ConversationId));
            return result;
        }

        private static void Run(LeagueAramTeamSideController controller)
        {
            controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" });
            controller.AwaitCurrentForSmokeTestAsync().GetAwaiter().GetResult();
        }

        private static string Gameflow(string mode) { return "{\"map\":{\"gameMode\":\"" + mode + "\"}}"; }
        private static string ChampSelect(int team)
        {
            return "{\"benchEnabled\":true,\"localPlayerCellId\":7,\"myTeam\":[{\"cellId\":7,\"team\":" + team + "}]}";
        }
        private static string Conversations(string id)
        {
            return "[{\"type\":\"championSelect\",\"id\":\"" + id + "\"}]";
        }

        private static byte[] Bytes(string text) { return Encoding.UTF8.GetBytes(text); }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Read : ILeagueClientApi
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
            internal int Reads;
            internal Action<string, int> BeforeRead;
            internal void Set(string path, string json) { lock (_values) _values[path] = json; }
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string json;
                int count;
                lock (_values)
                {
                    Reads++;
                    _counts.TryGetValue(path, out count);
                    _counts[path] = ++count;
                    if (BeforeRead != null) BeforeRead(path, count);
                    _values.TryGetValue(path, out json);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(json == null ? null : Bytes(json));
            }
        }

        private sealed class Write : ILeagueChampSelectSideWriteApi
        {
            internal readonly List<string> Calls = new List<string>();
            internal readonly List<bool> Visible = new List<bool>();
            public Task<LeagueClientWriteResponse> TrySendSideAsync(
                string conversationId, string message, bool visibleToTeam, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(conversationId + "|" + message);
                Visible.Add(visibleToTeam);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 201, Body = Array.Empty<byte>() });
            }
        }
    }
}
