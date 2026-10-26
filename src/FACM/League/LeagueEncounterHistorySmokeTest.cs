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
    internal static class LeagueEncounterHistorySmokeTest
    {
        internal static void Validate()
        {
            var session = new LeagueClientSession("LeagueClientUx", 55, 2999, "test", "https", "fixture", "HN1", "HN1");
            var game = new LeagueLiveSnapshot();
            new LeagueLiveDataService(new StubClient(), new PerformanceBudgetProvider()).ApplyCurrentGame(game,
                Encoding.UTF8.GetBytes("{\"phase\":\"InProgress\",\"gameData\":{\"gameId\":12345," +
                    "\"queue\":{\"id\":450},\"teamOne\":[{\"puuid\":\"self\"},{\"puuid\":\"ally\"}]," +
                    "\"teamTwo\":[{\"puuid\":\"enemy\"}]}}"));
            var capture = LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" });
            Require(capture != null && capture.GameId == 12345 && capture.Participants.Length == 3 &&
                capture.QueueType == "queue-450", "End-of-game encounter projection lost the pinned game identity.");
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "stranger" }) == null,
                "Encounter projection must not record a game without the signed-in player.");
            game.Phase = "EndOfGame";
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }) == null,
                "A stale end-game roster must not be mistaken for an in-progress match capture.");
            game.Phase = "InProgress";
            game.Players.Add(new LeagueLivePlayerRow { PuuId = "enemy" });
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }) == null,
                "Duplicated roster identities must not be recorded.");
            game.Players.RemoveAt(game.Players.Count - 1);

            var directory = Path.Combine(Path.GetTempPath(), "ggman-encounters-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LeagueEncounterHistoryStore(directory);
                Require(store.ReadPlayer("self", "enemy", "HN1", "HN1").Count == 0,
                    "Fresh encounter storage should be empty.");
                Require(store.SaveGame(capture.GameId, capture.SelfPuuid, capture.Region,
                    capture.PlatformId, capture.QueueType, capture.Participants) == 2,
                    "The first game must record each other player exactly once.");
                Require(store.SaveGame(capture.GameId, capture.SelfPuuid, capture.Region,
                    capture.PlatformId, capture.QueueType, capture.Participants) == 0,
                    "Replayed EndOfGame events must not duplicate a game.");
                var reloaded = new LeagueEncounterHistoryStore(directory);
                Require(reloaded.ReadPlayer("self", "enemy", "HN1", "HN1").Single().GameId == 12345 &&
                    reloaded.ReadPlayer("self", "enemy", "HN2", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("different-self", "enemy", "HN1", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("self", "enemy", "HN1", "HN2").Count == 0,
                    "Historical encounters must persist and remain scoped to the owning player and platform.");
                Require(reloaded.DeleteGame(12345, "self", "enemy", "HN1", "HN1") &&
                    reloaded.ReadPlayer("self", "enemy", "HN1", "HN1").Count == 0 &&
                    reloaded.ReadPlayer("self", "ally", "HN1", "HN1").Count == 1,
                    "Deleting one player's encounter must not remove another player's record.");
                bool rejected = false;
                try { reloaded.SaveGame(99, "self", "HN1", "HN1", "queue-450", new[] { "ally", "enemy" }); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "An unrelated roster cannot be credited to the signed-in player.");
                var path = Path.Combine(directory, "league-encounters.json");
                File.WriteAllText(path, "{invalid encounter file", Encoding.UTF8);
                rejected = false;
                try { reloaded.SaveGame(888, "self", "HN1", "HN1", "queue-450", new[] { "self", "enemy" }); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected && File.ReadAllText(path, Encoding.UTF8).Contains("invalid encounter file"),
                    "Invalid encounter storage must fail closed without overwriting the damaged file.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class StubClient : ILeagueClientApi
        {
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                return Task.FromResult<byte[]>(null);
            }
        }
    }
}
