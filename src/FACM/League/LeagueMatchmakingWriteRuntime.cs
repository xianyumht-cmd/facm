using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal interface ILeagueMatchmakingWriteApi
    {
        Task<LeagueClientWriteResponse> TrySendAsync(
            string method,
            string path,
            CancellationToken cancellationToken);
    }

    internal interface ILeagueLobbyInviteWriteApi
    {
        Task<LeagueClientWriteResponse> TryInviteAsync(long summonerId, CancellationToken cancellationToken);
    }

    internal interface ILeagueLobbyNoticeWriteApi
    {
        Task<LeagueClientWriteResponse> TrySendNoticeAsync(string conversationId, string message, CancellationToken cancellationToken);
    }

    internal sealed class LeagueMatchmakingWriteApiClient : ILeagueMatchmakingWriteApi, ILeagueLobbyInviteWriteApi, ILeagueLobbyNoticeWriteApi, IDisposable
    {
        internal const string SearchPath = "/lol-lobby/v2/lobby/matchmaking/search";
        internal const string AcceptPath = "/lol-matchmaking/v1/ready-check/accept";
        internal const string InvitationsPrefix = "/lol-lobby/v2/received-invitations/";
        internal const string PromotePrefix = "/lol-lobby/v2/lobby/members/";
        internal const string OutgoingInvitationsPath = "/lol-lobby/v2/lobby/invitations";
        internal const string ConversationsPrefix = "/lol-chat/v1/conversations/";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueMatchmakingWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TrySendAsync(
            string method,
            string path,
            CancellationToken cancellationToken)
        {
            var verb = (method ?? string.Empty).Trim().ToUpperInvariant();
            var normalizedPath = NormalizePath(path);
            if (!IsAllowedTarget(verb, normalizedPath))
                throw new ArgumentException("LCU write target is not allowed by the Gate 7 transport.", nameof(path));

            return await SendAllowedAsync(verb, normalizedPath, null, cancellationToken).ConfigureAwait(false);
        }

        public Task<LeagueClientWriteResponse> TryInviteAsync(long summonerId, CancellationToken cancellationToken)
        {
            if (!IsAllowedOutgoingInviteForSmokeTest(summonerId))
                throw new ArgumentOutOfRangeException(nameof(summonerId));
            var body = BuildOutgoingInvitationJson(summonerId);
            return SendAllowedAsync("POST", OutgoingInvitationsPath, body, cancellationToken);
        }

        public Task<LeagueClientWriteResponse> TrySendNoticeAsync(
            string conversationId, string message, CancellationToken cancellationToken)
        {
            if (!IsSafeConversationId(conversationId)) throw new ArgumentException("Invalid room conversation.", nameof(conversationId));
            return SendAllowedAsync("POST", ConversationsPrefix + conversationId + "/messages",
                BuildRoomNoticeJson(conversationId, message), cancellationToken);
        }

        internal static string BuildRoomNoticeJson(string conversationId, string message)
        {
            if (!IsSafeConversationId(conversationId)) throw new ArgumentException("Invalid room conversation.", nameof(conversationId));
            if (string.IsNullOrWhiteSpace(message) || message.Length > 128 || message.Any(char.IsControl))
                throw new ArgumentException("Invalid room notification.", nameof(message));
            return new JavaScriptSerializer().Serialize(new
            {
                body = message,
                fromPid = "",
                fromSummonerId = 0,
                id = conversationId,
                isHistorical = false,
                timestamp = "",
                type = "celebration"
            });
        }

        internal static bool IsSafeConversationId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 180) return false;
            foreach (var c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.' && c != '@' && c != '~') return false;
            return true;
        }

        internal static string BuildOutgoingInvitationJson(long summonerId)
        {
            if (!IsAllowedOutgoingInviteForSmokeTest(summonerId))
                throw new ArgumentOutOfRangeException(nameof(summonerId));
            return "[{\"toSummonerId\":" + summonerId.ToString(CultureInfo.InvariantCulture) + "}]";
        }

        private async Task<LeagueClientWriteResponse> SendAllowedAsync(
            string verb, string normalizedPath, string json, CancellationToken cancellationToken)
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
                    using (var request = new HttpRequestMessage(new HttpMethod(verb), normalizedPath))
                    {
                        if (json != null)
                            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await lease.Client.SendAsync(
                            request,
                            HttpCompletionOption.ResponseHeadersRead,
                            cancellationToken).ConfigureAwait(false))
                        {
                            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                                _sessions.Invalidate(session);
                            return new LeagueClientWriteResponse
                            {
                                StatusCode = (int)response.StatusCode,
                                Body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                            };
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

        internal static bool IsAllowedTargetForSmokeTest(string method, string path)
        {
            return IsAllowedTarget((method ?? string.Empty).Trim().ToUpperInvariant(), NormalizePath(path));
        }

        internal static bool IsAllowedOutgoingInviteForSmokeTest(long summonerId)
        {
            return summonerId > 0;
        }

        private static bool IsAllowedTarget(string verb, string path)
        {
            if (string.Equals(path, SearchPath, StringComparison.Ordinal))
                return string.Equals(verb, "POST", StringComparison.Ordinal) ||
                       string.Equals(verb, "DELETE", StringComparison.Ordinal);
            if (string.Equals(path, AcceptPath, StringComparison.Ordinal))
                return string.Equals(verb, "POST", StringComparison.Ordinal);
            if (string.Equals(verb, "POST", StringComparison.Ordinal) && path.StartsWith(InvitationsPrefix, StringComparison.Ordinal))
            {
                var segment = path.Substring(InvitationsPrefix.Length);
                var slash = segment.LastIndexOf('/');
                if (slash <= 0) return false;
                var action = segment.Substring(slash + 1);
                return (action == "accept" || action == "decline") && IsSafeInvitationId(segment.Substring(0, slash));
            }
            if (string.Equals(verb, "POST", StringComparison.Ordinal) &&
                path.StartsWith(PromotePrefix, StringComparison.Ordinal) &&
                path.EndsWith("/promote", StringComparison.Ordinal))
            {
                var segment = path.Substring(PromotePrefix.Length, path.Length - PromotePrefix.Length - "/promote".Length);
                long summonerId;
                return long.TryParse(segment, out summonerId) && summonerId > 0 &&
                    string.Equals(summonerId.ToString(System.Globalization.CultureInfo.InvariantCulture), segment, StringComparison.Ordinal);
            }
            return false;
        }

        internal static bool IsSafeInvitationId(string invitationId)
        {
            if (string.IsNullOrEmpty(invitationId) || invitationId.Length > 90) return false;
            foreach (var character in invitationId)
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= 'a' && character <= 'z') && character != '-') return false;
            return true;
        }

        private static string NormalizePath(string path)
        {
            var value = (path ?? string.Empty).Trim();
            if (value.Length == 0) return "/";
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }
}
