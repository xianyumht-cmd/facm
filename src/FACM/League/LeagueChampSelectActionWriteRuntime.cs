using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal interface ILeagueChampSelectActionWriteApi
    {
        Task<LeagueClientWriteResponse> TryApplyAsync(
            string actionType, int actionId, int championId, bool completed, CancellationToken cancellationToken);
        Task<LeagueClientWriteResponse> TryIntentAsync(
            int actionId, int championId, CancellationToken cancellationToken);
    }

    internal sealed class LeagueChampSelectActionWriteApiClient : ILeagueChampSelectActionWriteApi, IDisposable
    {
        internal const string ActionPathPrefix = "/lol-champ-select/v1/session/actions/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();

        public LeagueChampSelectActionWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public Task<LeagueClientWriteResponse> TryApplyAsync(
            string actionType, int actionId, int championId, bool completed, CancellationToken cancellationToken)
        {
            if (!IsAllowedForSmokeTest("PATCH", actionType, actionId, championId))
                throw new ArgumentException("Champ Select action target is not allowed.", nameof(actionId));

            var json = "{\"type\":\"" + actionType + "\",\"championId\":" +
                championId.ToString(CultureInfo.InvariantCulture) +
                ",\"completed\":" + (completed ? "true" : "false") + "}";
            return SendActionPatchAsync(actionId, json, cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TryIntentAsync(
            int actionId, int championId, CancellationToken cancellationToken)
        {
            if (!IsAllowedIntentForSmokeTest(actionId, championId))
                throw new ArgumentException("Champ Select intent target is not allowed.", nameof(actionId));
            var json = "{\"championId\":" + championId.ToString(CultureInfo.InvariantCulture) + "}";
            return SendActionPatchAsync(actionId, json, cancellationToken);
        }

        private async Task<LeagueClientWriteResponse> SendActionPatchAsync(
            int actionId, string json, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _sessions.GetSession();
            if (session == null) return null;

            LeagueSessionHttpClientPool.Lease lease;
            try { lease = _clients.Acquire(session); }
            catch (ObjectDisposedException) { return null; }

            using (lease)
            {
                try
                {
                    var path = ActionPathPrefix + actionId.ToString(CultureInfo.InvariantCulture);
                    using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), path))
                    {
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await lease.Client.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                                response.StatusCode == HttpStatusCode.Forbidden)
                                _sessions.Invalidate(session);
                            return new LeagueClientWriteResponse { StatusCode = (int)response.StatusCode };
                        }
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
                catch (ObjectDisposedException) { return null; }
                catch
                {
                    _sessions.Invalidate(session);
                    return null;
                }
            }
        }

        internal static bool IsAllowedForSmokeTest(string method, string actionType, int actionId, int championId)
        {
            return string.Equals(method, "PATCH", StringComparison.Ordinal) &&
                   (string.Equals(actionType, "pick", StringComparison.Ordinal) ||
                    string.Equals(actionType, "ban", StringComparison.Ordinal)) &&
                   actionId >= 0 && (championId > 0 && championId <= 10000 ||
                       actionType == "pick" && championId == -3 ||
                       actionType == "ban" && championId == -1);
        }

        internal static bool IsAllowedIntentForSmokeTest(int actionId, int championId)
        {
            return actionId >= 0 && (championId > 0 && championId <= 10000 || championId == -3);
        }

        public void Dispose()
        {
            _clients.Dispose();
        }
    }
}
