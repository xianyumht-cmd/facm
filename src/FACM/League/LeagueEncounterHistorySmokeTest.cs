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
                    "\"queue\":{\"id\":450,\"type\":\"ARAM\"},\"teamOne\":[{\"puuid\":\"self\"},{\"puuid\":\"ally\"}]," +
                    "\"teamTwo\":[{\"puuid\":\"enemy\"}]}}"));
            var capture = LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" });
            Require(capture != null && capture.GameId == 12345 && capture.Participants.Length == 3 &&
                capture.QueueType == "ARAM", "Encountered-game queue type must match the authenticated gameflow session.");
            game.QueueType = null;
            Require(LeagueEncounterCapture.Project(session, game, new LeaguePlayerProfile { PuuId = "self" }).QueueType == "queue-450",
                "Older gameflow payloads without queue.type should preserve the bounded fallback.");
            game.QueueType = "ARAM";
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
                Require(reloaded.SaveGame(12346, "self", "HN1", "HN1", "CLASSIC", new[] { "self", "enemy" }) == 1 &&
                    reloaded.SaveGame(12347, "self", "HN1", "HN1", "ARAM", new[] { "self", "enemy" }) == 1 &&
                    reloaded.SaveGame(12348, "self", "HN1", "HN1", "ARAM", new[] { "self", "enemy" }) == 1,
                    "Encounter query fixture could not write all games.");
                var page1 = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 2);
                var page2 = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 2, 2);
                Require(page1.Total == 3 && page1.Records.Select(p => p.GameId).SequenceEqual(new long[] { 12348, 12347 }) &&
                    page2.Total == 3 && page2.Records.Single().GameId == 12346,
                    "Encountered-game paging or newest-first ordering differs from the source query.");
                var filtered = reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 20, "ARAM", false);
                Require(filtered.Total == 2 && filtered.Records.Select(p => p.GameId).SequenceEqual(new long[] { 12347, 12348 }) &&
                    reloaded.QueryPlayer("self", "enemy", "HN1", "HN1", 1, 20, "AR").Total == 0 &&
                    reloaded.QueryPlayer("other", "enemy", "HN1", "HN1").Total == 0,
                    "Encountered-game filters must be exact and remain scoped to the signed-in account.");
                var overview = LeagueSavedPlayerOverview.Build(new[]
                {
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "enemy", Region = "hn1",
                        PlatformId = "hn1", Tag = "known", UpdatedUtc = DateTime.UtcNow },
                    new LeaguePlayerTagRecord { SelfPuuid = "other", Puuid = "enemy", Region = "HN1",
                        PlatformId = "HN1", Tag = "different owner", UpdatedUtc = DateTime.UtcNow },
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "enemy", Region = "HN2",
                        PlatformId = "HN2", Tag = "different region", UpdatedUtc = DateTime.UtcNow },
                    new LeaguePlayerTagRecord { SelfPuuid = "self", Puuid = "tag-only", Region = "HN1",
                        PlatformId = "HN1", Tag = "no encounters", UpdatedUtc = DateTime.UtcNow }
                }, reloaded.ReadAll());
                Require(overview.Count == 5 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN1")
                    .EncounterCount == 3 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN1")
                    .Tag == "known" && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "self" && p.Region == "HN2")
                    .EncounterCount == 0 && overview.Single(p => p.Puuid == "enemy" && p.SelfPuuid == "other")
                    .EncounterCount == 0 && overview.Single(p => p.Puuid == "ally").EncounterCount == 1 &&
                    overview.Single(p => p.Puuid == "tag-only").LastMetUtc == null,
                    "Saved-player index must include untagged encounters and isolated tag-only records.");
                var copied = reloaded.ReadAll().First();
                copied.Puuid = "modified";
                Require(reloaded.ReadAll().All(p => p.Puuid != "modified"),
                    "Saved-player overview must not expose mutable local-store state.");
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
