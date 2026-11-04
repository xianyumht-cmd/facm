using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FACM.Performance;

namespace FACM.League
{
    internal static class LeagueTaggedPlayerReminderSmokeTest
    {
        private const string Room = "lol-champ-select@CN1-fixture";
        private const string OtherRoom = "lol-champ-select@CN1-next";

        internal static void Validate()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ggman-tag-notice-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LeaguePlayerTagStore(directory);
                var session = new LeagueClientSession("LeagueClientUx", 5, 12345, "fixture", "https", "test", "HN1", "HN1");
                var read = Fixture();
                var write = new Write();
                using (var controller = Make(read, write, () => session, store))
                {
                    Run(controller);
                    Require(read.Reads == 0 && write.Messages.Count == 0,
                        "No local tags must cause no LCU read or outgoing message.");
                    store.SetTag("self", "ally", "HN1", "HN1", "值得信赖");
                    controller.TagsChanged();
                    Wait(controller);
                    Require(write.Messages.Count == 1 && write.Messages[0] == "[已标记的玩家: Buddy#CN1]: \n值得信赖" &&
                        write.Rooms[0] == Room, "Tagged player did not receive the source-shaped reminder.");
                    Run(controller);
                    Require(write.Messages.Count == 1, "Reminder repeated in the same conversation.");

                    read.Set(LeagueAramTeamSideController.ConversationsPath, Conversations(OtherRoom));
                    controller.TagsChanged();
                    Wait(controller);
                    Require(write.Messages.Count == 2 && write.Rooms[1] == OtherRoom,
                        "A replaced champion-select room must have independent reminder deduplication.");
                    controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                    read.Set(LeagueAramTeamSideController.ConversationsPath, Conversations(Room));
                    Run(controller);
                    Require(write.Messages.Count == 3, "A new champion-select episode must reset reminders.");
                }

                read = Fixture(); write = new Write();
                using (var controller = Make(read, write, () => session, store))
                {
                    read.AfterRead = path =>
                    {
                        if (path == LeagueAramTeamSideController.ConversationsPath)
                            controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "Lobby" });
                    };
                    Run(controller);
                    Require(write.Messages.Count == 0, "Leaving champion select must cancel a pending reminder.");
                }

                read = Fixture(); write = new Write();
                using (var controller = Make(read, write, () => session, store))
                {
                    read.AfterRead = path =>
                    {
                        if (path == LeagueLiveDataService.ChampSelectSessionPath)
                            read.Set(path, ChampSelect("different"));
                    };
                    Run(controller);
                    Require(write.Messages.Count == 0, "Changed roster identity must stop automatic posting.");
                }
                Require(!LeagueTaggedPlayerReminderController.SafeReminder("[已标记的玩家: A#B]: \nx\ny") &&
                    LeagueTaggedPlayerReminderController.FormatReminder(new LeagueLivePlayerRow
                    { GameName = "Bad\nPlayer", TagLine = "CN1" }, "safe") == null &&
                    LeagueMatchmakingWriteApiClient.BuildTaggedNoticeJson(Room,
                        "[已标记的玩家: Buddy#CN1]: \n值得信赖").Contains("\"type\":\"celebration\""),
                    "Reminder formatting and celebration-only writer contract failed.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static LeagueTaggedPlayerReminderController Make(Read read, Write write,
            Func<LeagueClientSession> session, LeaguePlayerTagStore store)
        {
            return new LeagueTaggedPlayerReminderController(read, write, session,
                store, new PerformanceBudgetProvider());
        }

        private static Read Fixture()
        {
            var read = new Read();
            read.Set(LeagueDashboardDetailsService.SummonerPath, "{\"puuid\":\"self\"}");
            read.Set(LeagueAramTeamSideController.ConversationsPath, Conversations(Room));
            read.Set(LeagueLiveDataService.ChampSelectSessionPath, ChampSelect("ally"));
            return read;
        }

        private static string Conversations(string room)
        {
            return "[{\"type\":\"championSelect\",\"id\":\"" + room + "\"}]";
        }

        private static string ChampSelect(string ally)
        {
            return "{\"localPlayerCellId\":1,\"myTeam\":[" +
                "{\"cellId\":1,\"puuid\":\"self\",\"gameName\":\"Me\",\"tagLine\":\"CN1\"}," +
                "{\"cellId\":2,\"puuid\":\"" + ally + "\",\"gameName\":\"Buddy\",\"tagLine\":\"CN1\"}]}";
        }

        private static void Run(LeagueTaggedPlayerReminderController controller)
        {
            controller.Observe(new LeagueDashboardPhaseState { Connected = true, Phase = "ChampSelect" });
            Wait(controller);
        }

        private static void Wait(LeagueTaggedPlayerReminderController controller)
        {
            Task.Run(async () => await controller.AwaitCurrentForSmokeTestAsync().ConfigureAwait(false))
                .GetAwaiter().GetResult();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class Read : ILeagueClientApi
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            internal int Reads;
            internal Action<string> AfterRead;

            internal void Set(string path, string json)
            {
                lock (_values) _values[path] = json;
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                string value;
                lock (_values) { Reads++; _values.TryGetValue(path, out value); }
                AfterRead?.Invoke(path);
                token.ThrowIfCancellationRequested();
                return Task.FromResult(value == null ? null : Encoding.UTF8.GetBytes(value));
            }
        }

        private sealed class Write : ILeagueTaggedPlayerNoticeWriteApi
        {
            internal readonly List<string> Rooms = new List<string>();
            internal readonly List<string> Messages = new List<string>();

            public Task<LeagueClientWriteResponse> TrySendTaggedNoticeAsync(
                LeagueClientSession session, string room, string message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Rooms.Add(room); Messages.Add(message);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 201, Body = Array.Empty<byte>() });
            }
        }
    }
}
