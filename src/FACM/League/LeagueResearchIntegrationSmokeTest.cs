using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeagueResearchIntegrationSmokeTest
    {
        public static void Validate()
        {
            var fixture = new FakeClient();
            fixture.Muted.Add(First);
            var service = new LeagueChampSelectMuteService(fixture, fixture);
            var result = service.MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Requested == 1 && result.Succeeded == 1 && result.AlreadyMuted == 1 &&
                    fixture.Writes == 1 && fixture.Muted.Contains(First) && fixture.Muted.Contains(Second),
                "Mute-all changed an already-muted target or failed to preserve a large identity.");
            result = service.MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Requested == 0 && result.AlreadyMuted == 2 && fixture.Writes == 1,
                "Repeated mute-all was not idempotent.");

            var unavailable = new FakeClient { MutedOverride = "{}" };
            result = new LeagueChampSelectMuteService(unavailable, unavailable)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && unavailable.Writes == 0,
                "Invalid mute state allowed a blind toggle.");

            var lobby = new FakeClient { Phase = "Lobby" };
            result = new LeagueChampSelectMuteService(lobby, lobby)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.NotInChampSelect && lobby.Writes == 0,
                "Mute action escaped the champion-select phase.");

            var changed = new FakeClient { ChangeRoster = true };
            result = new LeagueChampSelectMuteService(changed, changed)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && changed.Writes == 0,
                "Mute action continued after roster changed.");

            var uncertain = new FakeClient { UncertainWrite = true };
            result = new LeagueChampSelectMuteService(uncertain, uncertain)
                .MuteAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.StateUnavailable && uncertain.Writes == 1,
                "Uncertain toggle was retried or followed by another write.");

            var cancelled = new FakeClient();
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    new LeagueChampSelectMuteService(cancelled, cancelled)
                        .MuteAllAsync(cancellation.Token).GetAwaiter().GetResult();
                    throw new InvalidOperationException("Canceled mute completed.");
                }
                catch (OperationCanceledException) { }
            }
            Require(cancelled.Writes == 0, "Canceled mute wrote to the client.");

            var penalty = LeagueLeaverBusterService.ParseSearch(Bytes(
                "{\"errors\":[{\"errorType\":\"QUEUE_DODGER\",\"penaltyTimeRemaining\":39233.063}]}"));
            Require(penalty.Available && penalty.HasPenalty && penalty.MaxSecondsRemaining > 39233 &&
                    penalty.Entries[0].Type == "QUEUE_DODGER",
                "Active matchmaking restriction was not recognized.");
            var clean = LeagueLeaverBusterService.ParseSearch(Bytes("{\"errors\":[]}"));
            Require(clean.Available && !clean.HasPenalty, "Empty errors should mean no reported restriction.");
            Require(!LeagueLeaverBusterService.ParseSearch(Bytes("{}")).Available &&
                    !LeagueLeaverBusterService.ParseSearch(Bytes("[]")).Available &&
                    !LeagueLeaverBusterService.ParseSearch(Bytes("{\"errors\":[{}]}")).Available,
                "Missing or malformed matchmaking errors were presented as clean.");

            var rewards = new LeagueRewardsOverviewService(new FakeClient())
                .ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(rewards.Available && rewards.LootAvailable && !rewards.MissionsAvailable &&
                    rewards.LootItemCount == 0, "Partial reward availability collapsed into numeric zero.");
        }

        private const string First = "{\"summonerId\":3000000001,\"puuid\":\"fixture-a\",\"obfuscatedSummonerId\":0,\"obfuscatedPuuid\":\"\"}";
        private const string Second = "{\"summonerId\":3000000002,\"puuid\":\"fixture-b\",\"obfuscatedSummonerId\":0,\"obfuscatedPuuid\":\"\"}";
        private static byte[] Bytes(string value) { return value == null ? null : Encoding.UTF8.GetBytes(value); }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeClient : ILeagueClientApi, ILeagueChampSelectMuteWriteApi
        {
            public string Phase = "ChampSelect";
            public string MutedOverride;
            public bool ChangeRoster;
            public bool UncertainWrite;
            public int Writes;
            private int _sessionReads;
            public readonly HashSet<string> Muted = new HashSet<string>(StringComparer.Ordinal);

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string body = null;
                if (path == LeagueChampSelectMuteService.PhasePath) body = "\"" + Phase + "\"";
                if (path == LeagueChampSelectMuteService.SessionPath)
                {
                    _sessionReads++;
                    body = "{\"localPlayerCellId\":0,\"gameId\":123,\"myTeam\":[{\"cellId\":0}," +
                           "{\"cellId\":1," + First.Substring(1) +
                           (ChangeRoster && _sessionReads > 1 ? "" : ",{\"cellId\":2," + Second.Substring(1)) +
                           "],\"theirTeam\":[]}";
                }
                if (path == LeagueChampSelectMuteService.MutedPlayersPath)
                    body = MutedOverride ?? ("[" + string.Join(",", Muted) + "]");
                if (path == LeagueRewardsOverviewService.PlayerLootPath) body = "[]";
                return Task.FromResult(Bytes(body));
            }

            public Task<LeagueClientWriteResponse> TryToggleAsync(string identityJson, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Writes++;
                if (UncertainWrite) return Task.FromResult<LeagueClientWriteResponse>(null);
                if (!Muted.Add(identityJson)) Muted.Remove(identityJson);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 204 });
            }
        }
    }
}
