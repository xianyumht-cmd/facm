using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeagueChampSelectQuitSmokeTest
    {
        public static void Validate()
        {
            Require(LeagueChampSelectQuitWriteApiClient.IsAllowedTargetForSmokeTest("POST",
                LeagueChampSelectQuitWriteApiClient.QuitPath), "Primary quit route is blocked.");
            Require(LeagueChampSelectQuitWriteApiClient.IsAllowedTargetForSmokeTest("POST",
                LeagueChampSelectQuitWriteApiClient.LegacyQuitPath), "Legacy quitV2 route is blocked.");
            Require(LeagueChampSelectQuitWriteApiClient.LegacyQuitPath.StartsWith(
                    "/lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=", StringComparison.Ordinal) &&
                    Uri.UnescapeDataString(LeagueChampSelectQuitWriteApiClient.LegacyQuitPath.Split(
                        new[] { "&args=" }, StringSplitOptions.None)[1]) == "[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]",
                "Legacy quitV2 request arguments changed.");
            Require(LeagueChampSelectQuitWriteApiClient.DescribeResponseBody(new byte[0]) == "empty" &&
                    LeagueChampSelectQuitWriteApiClient.DescribeResponseBody(Encoding.UTF8.GetBytes("false")) == "bool-false" &&
                    LeagueChampSelectQuitWriteApiClient.DescribeResponseBody(Encoding.UTF8.GetBytes("{}")) == "object",
                "Safe response diagnostic shape detection regressed.");
            foreach (var path in new[] { "/lol-lobby/v2/lobby", "/lol-lobby-team-builder/v1/lobby",
                                         "/lol-gameflow/v1/session/dodge", "/lol-gameflow/v1/session/request-lobby",
                                         "/lol-login/v1/session/invoke" })
            {
                Require(!LeagueChampSelectQuitWriteApiClient.IsAllowedTargetForSmokeTest("DELETE", path),
                    "Quit writer can delete a lobby or gameflow session.");
                Require(!LeagueChampSelectQuitWriteApiClient.IsAllowedTargetForSmokeTest("POST", path),
                    "Quit writer can mutate a forbidden route.");
            }

            var normal = new FakeQuitSession(200);
            AssertResult(normal, LeagueChampSelectQuitStatus.Success, true, 1, 0);

            var matchmade = new FakeQuitSession(400) { FallbackResponse = Response(200, "") };
            AssertResult(matchmade, LeagueChampSelectQuitStatus.Success, true, 1, 1);

            var payloadFalseButExited = new FakeQuitSession(400) { FallbackResponse = Response(200, "false") };
            AssertResult(payloadFalseButExited, LeagueChampSelectQuitStatus.Success, true, 1, 1);

            var unchanged = new FakeQuitSession(400) { FallbackResponse = Response(200, "true"),
                                                      FallbackChangesPhase = false };
            AssertResult(unchanged, LeagueChampSelectQuitStatus.VerificationFailed, false, 1, 1);

            var declined = new FakeQuitSession(400) { FallbackResponse = Response(403, "{\"errorCode\":\"DISALLOWED\"}") };
            AssertResult(declined, LeagueChampSelectQuitStatus.WriteRejected, false, 1, 1);

            var repeated = new FakeQuitSession(400) { FallbackResponse = Response(500) };
            var repeatedService = new LeagueChampSelectQuitService(repeated, repeated);
            repeatedService.QuitAsync(CancellationToken.None).GetAwaiter().GetResult();
            var tooSoon = repeatedService.QuitAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(tooSoon.Status == LeagueChampSelectQuitStatus.WriteRejected &&
                    repeated.PrimaryCount == 1 && repeated.FallbackCount == 1,
                "Repeated click submitted another rejected quit before cooldown.");

            var unavailable = new FakeQuitSession(400) { OriginalLobby = null };
            AssertResult(unavailable, LeagueChampSelectQuitStatus.WriteRejected, false, 1, 0);

            var missingIdentity = new FakeQuitSession(400)
            {
                OriginalLobby = "{\"members\":[{\"puuid\":\"first\"}]}"
            };
            AssertResult(missingIdentity, LeagueChampSelectQuitStatus.WriteRejected, false, 1, 0);

            var changedPartyBeforeFallback = new FakeQuitSession(400) { ReplacePartyBeforeFallback = true };
            AssertResult(changedPartyBeforeFallback, LeagueChampSelectQuitStatus.VerificationFailed, false, 1, 0);

            var forbidden = new FakeQuitSession(403);
            AssertResult(forbidden, LeagueChampSelectQuitStatus.WriteRejected, false, 1, 0);

            var lostOriginal = new FakeQuitSession(400) { RestoredLobby = DifferentLobby };
            AssertResult(lostOriginal, LeagueChampSelectQuitStatus.VerificationFailed, false, 1, 1);

            var changedPhase = new FakeQuitSession(400) { ChangePhaseOnRejectedPrimary = true };
            AssertResult(changedPhase, LeagueChampSelectQuitStatus.VerificationFailed, false, 1, 0);

            var notSelecting = new FakeQuitSession(200) { Phase = "Lobby" };
            AssertResult(notSelecting, LeagueChampSelectQuitStatus.NotInChampSelect, false, 0, 0);

            var transportMissing = new FakeQuitSession(0) { PrimaryResponse = null };
            AssertResult(transportMissing, LeagueChampSelectQuitStatus.SessionUnavailable, false, 1, 0);

            var cancelled = new FakeQuitSession(200);
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                new LeagueChampSelectQuitService(cancelled, cancelled)
                    .QuitAsync(cancellation.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("Canceled quit unexpectedly entered its write path.");
            }
            catch (OperationCanceledException) { }
            Require(cancelled.PrimaryCount == 0 && cancelled.FallbackCount == 0,
                "Canceled quit submitted a write.");
        }

        private const string SameLobby = "{\"chatRoomId\":\"party-1\",\"members\":[{\"puuid\":\"first\"},{\"puuid\":\"second\"}]}";
        private const string DifferentLobby = "{\"chatRoomId\":\"party-2\",\"members\":[{\"puuid\":\"first\"},{\"puuid\":\"second\"}]}";

        private static LeagueClientWriteResponse Response(int status, string body = "")
        {
            return new LeagueClientWriteResponse { StatusCode = status, Body = Encoding.UTF8.GetBytes(body) };
        }

        private static void AssertResult(
            FakeQuitSession fixture, LeagueChampSelectQuitStatus expected, bool expectedPreserved,
            int primaryCount, int fallbackCount)
        {
            var result = new LeagueChampSelectQuitService(fixture, fixture)
                .QuitAsync(CancellationToken.None).GetAwaiter().GetResult();
            Require(result.Status == expected,
                "Unexpected Champion Select quit result: " + result.Status + " expected " + expected);
            Require(result.LobbyPreserved == expectedPreserved,
                "Champion Select quit incorrectly marked the original party preserved.");
            Require(fixture.PrimaryCount == primaryCount && fixture.FallbackCount == fallbackCount,
                "Champion Select quit wrote unexpected routes or repeated a rejected request.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeQuitSession : ILeagueClientApi, ILeagueChampSelectQuitWriteApi
        {
            public FakeQuitSession(int primaryStatus)
            {
                PrimaryResponse = Response(primaryStatus, primaryStatus == 400
                    ? "{\"errorCode\":\"INVALID_STATE\"}" : "");
            }

            public string Phase = "ChampSelect";
            public string OriginalLobby = SameLobby;
            public string RestoredLobby = SameLobby;
            public bool ChangePhaseOnRejectedPrimary;
            public LeagueClientWriteResponse PrimaryResponse;
            public LeagueClientWriteResponse FallbackResponse = Response(200, "");
            public bool FallbackChangesPhase = true;
            public bool ReplacePartyBeforeFallback;
            private int _lobbyReads;
            public int PrimaryCount;
            public int FallbackCount;
            private bool _sentWrite;

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string body = null;
                if (path == LeagueChampSelectQuitService.GameflowPhasePath)
                    body = "\"" + Phase + "\"";
                if (path == LeagueChampSelectQuitService.ChampSelectSessionPath &&
                    string.Equals(Phase, "ChampSelect", StringComparison.Ordinal))
                    body = "{}";
                if (path == LeagueChampSelectQuitService.LobbyPath)
                {
                    _lobbyReads++;
                    body = _sentWrite ? RestoredLobby :
                           (ReplacePartyBeforeFallback && _lobbyReads > 1 ? DifferentLobby : OriginalLobby);
                }
                return Task.FromResult(body == null ? null : Encoding.UTF8.GetBytes(body));
            }

            public Task<LeagueClientWriteResponse> TryQuitAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrimaryCount++;
                if (PrimaryResponse != null && PrimaryResponse.IsSuccessStatusCode)
                {
                    _sentWrite = true;
                    Phase = "Lobby";
                }
                else if (ChangePhaseOnRejectedPrimary)
                    Phase = "Lobby";
                return Task.FromResult(PrimaryResponse);
            }

            public Task<LeagueClientWriteResponse> TryLegacyQuitAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FallbackCount++;
                if (FallbackChangesPhase && FallbackResponse != null && FallbackResponse.IsSuccessStatusCode)
                {
                    _sentWrite = true;
                    Phase = "Lobby";
                }
                return Task.FromResult(FallbackResponse);
            }
        }
    }
}
