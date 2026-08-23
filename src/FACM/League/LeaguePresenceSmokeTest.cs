using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal static class LeaguePresenceSmokeTest
    {
        public static void Validate()
        {
            ValidatePayloadPreservesUnrelatedPresenceFields();
            ValidateUserDirectedApplyUsesOneWrite();
            ValidateDisplayInGame();
            ValidateClientOverrideIsReportedWithoutRewriteLoop();
            ValidateDedicatedWriterFence();
        }

        private static void ValidatePayloadPreservesUnrelatedPresenceFields()
        {
            var fake = new FakePresenceApi();
            var service = CreateService(fake);
            var payload = service.BuildPayloadForSmokeTest(fake.CurrentBytes, LeaguePresenceMode.Away);
            var root = new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string, object>;
            Require(root != null, "Presence payload could not be parsed.");
            Require(ReadString(root, "availability") == "away", "Away mode did not set availability.");
            Require(ReadString(root, "statusMessage") == "keep-me", "Presence write dropped statusMessage.");
            Require(ReadString(root, "customRoot") == "preserve", "Presence write dropped an unrelated root field.");
            var lol = ReadDictionary(root, "lol");
            Require(ReadString(lol, "gameStatus") == "outOfGame", "Away mode did not clear the displayed in-game state.");
            Require(ReadString(lol, "rankedLeagueName") == "Gold", "Presence write dropped unrelated lol metadata.");
        }

        private static void ValidateUserDirectedApplyUsesOneWrite()
        {
            var fake = new FakePresenceApi();
            var service = CreateService(fake);
            var result = service.ApplyAsync(LeaguePresenceMode.Offline, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Status == "success", "Offline presence did not verify successfully in the deterministic fixture.");
            Require(fake.WriteCount == 1, "A single user presence click must produce exactly one PUT.");
            Require(result.Observed != null && result.Observed.Availability == "offline", "Offline readback was not returned.");
        }

        private static void ValidateDisplayInGame()
        {
            var fake = new FakePresenceApi();
            var service = CreateService(fake);
            var result = service.ApplyAsync(LeaguePresenceMode.DisplayInGame, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Status == "success", "Displayed in-game presence did not verify.");
            Require(fake.WriteCount == 1, "Displayed in-game mode must not retry writes in the background.");
            Require(result.Observed != null &&
                    string.Equals(result.Observed.GameStatus, "inGame", StringComparison.OrdinalIgnoreCase),
                "Displayed in-game mode lost gameStatus=inGame.");
        }

        private static void ValidateClientOverrideIsReportedWithoutRewriteLoop()
        {
            var fake = new FakePresenceApi { OverrideOnSecondPostWriteRead = true };
            var service = CreateService(fake);
            var result = service.ApplyAsync(LeaguePresenceMode.Away, CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Status == "overridden", "Client overwrite must be reported honestly.");
            Require(fake.WriteCount == 1, "FACM must not fight the League client with a presence rewrite loop.");
        }

        private static void ValidateDedicatedWriterFence()
        {
            Require(LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me"),
                "Presence writer blocked its exact endpoint.");
            Require(!LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("POST", "/lol-chat/v1/me"),
                "Presence writer accepted the wrong HTTP method.");
            Require(!LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-chat/v1/me?force=true"),
                "Presence writer accepted a query-string escape hatch.");
            Require(!LeaguePresenceWriteApiClient.IsAllowedTargetForSmokeTest("PUT", "/lol-champ-select/v1/session/my-selection"),
                "Presence writer escaped into Champ Select writes.");
        }

        private static LeaguePresenceService CreateService(FakePresenceApi fake)
        {
            return new LeaguePresenceService(fake, fake, TimeSpan.Zero, TimeSpan.Zero);
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakePresenceApi : ILeagueClientApi, ILeaguePresenceWriteApi
        {
            private byte[] _current = Encoding.UTF8.GetBytes(
                "{\"availability\":\"chat\",\"name\":\"Tester\",\"statusMessage\":\"keep-me\",\"customRoot\":\"preserve\",\"lol\":{\"gameStatus\":\"outOfGame\",\"rankedLeagueName\":\"Gold\"}}");
            private bool _written;
            private int _postWriteReads;

            public bool OverrideOnSecondPostWriteRead { get; set; }
            public int WriteCount { get; private set; }
            public byte[] CurrentBytes { get { return _current; } }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(path, LeaguePresenceService.PresencePath, StringComparison.Ordinal))
                    return Task.FromResult<byte[]>(null);
                if (_written)
                {
                    _postWriteReads++;
                    if (OverrideOnSecondPostWriteRead && _postWriteReads >= 2)
                    {
                        _current = Encoding.UTF8.GetBytes(
                            "{\"availability\":\"chat\",\"name\":\"Tester\",\"statusMessage\":\"keep-me\",\"customRoot\":\"preserve\",\"lol\":{\"gameStatus\":\"outOfGame\",\"rankedLeagueName\":\"Gold\"}}");
                    }
                }
                return Task.FromResult(_current);
            }

            public Task<LeagueClientWriteResponse> TrySetPresenceAsync(string json, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteCount++;
                _written = true;
                _current = Encoding.UTF8.GetBytes(json ?? string.Empty);
                return Task.FromResult(new LeagueClientWriteResponse { StatusCode = 200, Body = Encoding.UTF8.GetBytes("{}") });
            }
        }
    }
}
