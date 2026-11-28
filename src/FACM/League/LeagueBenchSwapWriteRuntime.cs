using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    /// <summary>
    /// Dedicated minimal writer for the ARAM / ARAM Mayhem bench.
    /// Callers provide only a champion id plus the already-observed Champ Select route; the
    /// transport still constructs the endpoint itself and cannot be reused for arbitrary writes.
    /// </summary>
    internal interface ILeagueBenchSwapWriteApi
    {
        Task<LeagueClientWriteResponse> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken);
    }

    internal interface ILeagueChampionTradeWriteApi
    {
        Task<LeagueClientWriteResponse> TryAnswerAsync(
            int tradeId, bool accept, CancellationToken cancellationToken);
    }

    internal interface ILeagueScopedBenchSwapWriteApi
    {
        Task<LeagueClientWriteResponse> TrySwapAsync(int championId, LeagueBenchSwapRoute route,
            LeagueClientSession expected, CancellationToken token);
    }

    internal enum LeagueDraftSwapKind { Champion, Position, PickOrder }
    internal enum LeagueDraftSwapAction { Accept, Decline, Cancel }

    internal interface ILeagueDraftSwapWriteApi
    {
        Task<LeagueClientWriteResponse> TryHandleSwapAsync(LeagueDraftSwapKind kind, int id,
            LeagueDraftSwapAction action, LeagueClientSession expected, CancellationToken token);
    }

    internal interface ILeagueAramRerollWriteApi
    {
        Task<LeagueClientWriteResponse> TryRerollAsync(LeagueClientSession expected, CancellationToken token);
    }

    internal sealed class LeagueDraftSwapObservation
    {
        internal LeagueDraftSwapKind Kind { get; private set; }
        internal int Id { get; private set; }
        internal string State { get; private set; }
        internal bool LocalInitiated { get; private set; }
        internal int RequestedChampionId { get; private set; }
        internal bool CanAnswer { get { return State == "RECEIVED" && !LocalInitiated; } }
        internal bool CanCancel { get { return State == "SENT" && LocalInitiated; } }

        internal bool Allows(LeagueDraftSwapAction action)
        {
            return action == LeagueDraftSwapAction.Cancel ? CanCancel :
                (action == LeagueDraftSwapAction.Accept || action == LeagueDraftSwapAction.Decline) && CanAnswer;
        }

        internal bool SameRequest(LeagueDraftSwapObservation other)
        {
            return other != null && Kind == other.Kind && Id == other.Id && State == other.State &&
                LocalInitiated == other.LocalInitiated && RequestedChampionId == other.RequestedChampionId;
        }

        internal static LeagueDraftSwapObservation Parse(byte[] bytes, LeagueDraftSwapKind kind)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 32768) return null;
            try
            {
                var root = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .DeserializeObject(System.Text.Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                object id, type, state, local, champion;
                var expectedType = kind == LeagueDraftSwapKind.Champion ? "CHAMPION" :
                    kind == LeagueDraftSwapKind.Position ? "POSITION" :
                    kind == LeagueDraftSwapKind.PickOrder ? "PICK_ORDER" : "";
                if (root == null || expectedType.Length == 0 ||
                    !root.TryGetValue("id", out id) || !(id is int) || (int)id <= 0 ||
                    !root.TryGetValue("type", out type) || !string.Equals(type as string, expectedType, StringComparison.Ordinal) ||
                    !root.TryGetValue("state", out state) || !(state is string) ||
                    !root.TryGetValue("initiatedByLocalPlayer", out local) || !(local is bool)) return null;
                root.TryGetValue("requesterChampionId", out champion);
                return new LeagueDraftSwapObservation { Kind = kind, Id = (int)id, State = (string)state,
                    LocalInitiated = (bool)local, RequestedChampionId = champion is int ? (int)champion : 0 };
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static bool Confirms(byte[] bytes, LeagueDraftSwapKind kind, int id, LeagueDraftSwapAction action)
        {
            var result = Parse(bytes, kind);
            if (result == null || result.Id != id) return false;
            switch (action)
            {
                case LeagueDraftSwapAction.Accept: return result.State == "ACCEPTED";
                case LeagueDraftSwapAction.Decline: return result.State == "DECLINED";
                case LeagueDraftSwapAction.Cancel: return result.State == "CANCELLED" || result.State == "CANCELED";
                default: return false;
            }
        }
    }

    internal sealed class LeagueBenchSwapWriteApiClient : ILeagueBenchSwapWriteApi, ILeagueScopedBenchSwapWriteApi, ILeagueChampionTradeWriteApi, ILeagueDraftSwapWriteApi, ILeagueAramRerollWriteApi, IDisposable
    {
        internal const string RerollPath = "/lol-champ-select/v1/session/my-selection/reroll";
        internal const string LegacySwapPathPrefix = "/lol-champ-select/v1/session/bench/swap/";
        internal const string TeamBuilderSwapPathPrefix = "/lol-lobby-team-builder/champ-select/v1/session/bench/swap/";
        internal const string ChampionTradePathPrefix = "/lol-champ-select/v1/session/champion-swaps/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueBenchSwapWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public Task<LeagueClientWriteResponse> TrySwapAsync(
            int championId,
            LeagueBenchSwapRoute route,
            CancellationToken cancellationToken)
        {
            if (!IsValidChampionIdForSmokeTest(championId))
                throw new ArgumentOutOfRangeException(nameof(championId), "Champion id must be positive.");

            return SendBodylessPostAsync(BuildPathForSmokeTest(championId, route), cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TrySwapAsync(int championId, LeagueBenchSwapRoute route,
            LeagueClientSession expected, CancellationToken token)
        {
            if (championId <= 0 || expected == null) return Task.FromResult<LeagueClientWriteResponse>(null);
            return SendBodylessPostAsync(BuildPathForSmokeTest(championId, route), token, expected);
        }

        internal static string OngoingSwapPath(LeagueDraftSwapKind kind)
        {
            switch (kind)
            {
                case LeagueDraftSwapKind.Champion: return "/lol-champ-select/v1/ongoing-champion-swap";
                case LeagueDraftSwapKind.Position: return "/lol-champ-select/v1/ongoing-position-swap";
                case LeagueDraftSwapKind.PickOrder: return "/lol-champ-select/v1/ongoing-pick-order-swap";
                default: return string.Empty;
            }
        }

        internal static string BuildDraftSwapPath(LeagueDraftSwapKind kind, int id, LeagueDraftSwapAction action)
        {
            if (id <= 0) return string.Empty;
            string prefix;
            switch (kind)
            {
                case LeagueDraftSwapKind.Champion: prefix = ChampionTradePathPrefix; break;
                case LeagueDraftSwapKind.Position: prefix = "/lol-champ-select/v1/session/position-swaps/"; break;
                case LeagueDraftSwapKind.PickOrder: prefix = "/lol-champ-select/v1/session/pick-order-swaps/"; break;
                default: return string.Empty;
            }
            string suffix;
            switch (action)
            {
                case LeagueDraftSwapAction.Accept: suffix = "/accept"; break;
                case LeagueDraftSwapAction.Decline: suffix = "/decline"; break;
                case LeagueDraftSwapAction.Cancel: suffix = "/cancel"; break;
                default: return string.Empty;
            }
            return prefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix;
        }

        public Task<LeagueClientWriteResponse> TryHandleSwapAsync(LeagueDraftSwapKind kind, int id,
            LeagueDraftSwapAction action, LeagueClientSession expected, CancellationToken token)
        {
            var path = BuildDraftSwapPath(kind, id, action);
            if (expected == null || path.Length == 0) return Task.FromResult<LeagueClientWriteResponse>(null);
            return SendBodylessPostAsync(path, token, expected);
        }

        public Task<LeagueClientWriteResponse> TryAnswerAsync(
            int tradeId, bool accept, CancellationToken cancellationToken)
        {
            if (tradeId <= 0) throw new ArgumentOutOfRangeException(nameof(tradeId));
            return SendBodylessPostAsync(BuildTradePathForSmokeTest(tradeId, accept), cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TryRerollAsync(LeagueClientSession expected, CancellationToken token)
        {
            return expected == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                SendBodylessPostAsync(RerollPath, token, expected);
        }

        private async Task<LeagueClientWriteResponse> SendBodylessPostAsync(
            string path, CancellationToken cancellationToken, LeagueClientSession expected = null)
        {

            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null || expected != null && !LeagueEncounterReplayService.SessionMatches(expected, session)) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try
            {
                lease = _clients.Acquire(session);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            using (lease)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, path))
                    using (var response = await lease.Client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized ||
                            response.StatusCode == HttpStatusCode.Forbidden)
                            _sessions.Invalidate(session);

                        return new LeagueClientWriteResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            Body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                        };
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    _sessions.Invalidate(session);
                    return null;
                }
                catch (HttpRequestException)
                {
                    _sessions.Invalidate(session);
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
                catch
                {
                    _sessions.Invalidate(session);
                    return null;
                }
            }
        }

        internal static bool IsValidChampionIdForSmokeTest(int championId)
        {
            return championId > 0;
        }

        internal static string BuildPathForSmokeTest(int championId, LeagueBenchSwapRoute route)
        {
            if (championId <= 0) return string.Empty;
            return (route == LeagueBenchSwapRoute.TeamBuilder
                ? TeamBuilderSwapPathPrefix
                : LegacySwapPathPrefix) + championId;
        }

        internal static string BuildTradePathForSmokeTest(int tradeId, bool accept)
        {
            if (tradeId <= 0) return string.Empty;
            return ChampionTradePathPrefix + tradeId + (accept ? "/accept" : "/decline");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
