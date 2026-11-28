using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueChampSelectMuteResult
    {
        public int Requested { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public bool NotInChampSelect { get; set; }

        public string Summary
        {
            get
            {
                if (NotInChampSelect) return "不在选人阶段";
                if (Requested == 0) return "没有需要静音的玩家";
                return Failed == 0
                    ? "已静音 " + Succeeded + " 人"
                    : "静音 " + Succeeded + "/" + Requested + "，失败 " + Failed;
            }
        }
    }

    internal interface ILeagueChampSelectMuteWriteApi
    {
        Task<LeagueClientWriteResponse> TryToggleAsync(string identityJson, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Writes only to the legacy champion-select mute route. The team-builder plugin does not
    /// expose toggle-player-muted (verified against a live client), so this path must stay on
    /// /lol-champ-select/v1 even during a team-builder champion select.
    /// The request body mirrors the client's own component: four identity fields, not a cellId.
    /// </summary>
    internal sealed class LeagueChampSelectMuteWriteApiClient : ILeagueChampSelectMuteWriteApi, IDisposable
    {
        internal const string TogglePath = "/lol-champ-select/v1/toggle-player-muted";

        private readonly LeagueClientSessionProvider _sessions;
        private readonly LeagueSessionHttpClientPool _clients = new LeagueSessionHttpClientPool();
        private bool _disposed;

        public LeagueChampSelectMuteWriteApiClient(LeagueClientSessionProvider sessions)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public async Task<LeagueClientWriteResponse> TryToggleAsync(
            string identityJson, CancellationToken cancellationToken)
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
                    using (var request = new HttpRequestMessage(HttpMethod.Post, TogglePath))
                    {
                        request.Content = new StringContent(
                            identityJson ?? "{}",
                            Encoding.UTF8,
                            "application/json");

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
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Dispose();
        }
    }

    internal sealed class LeagueChampSelectMuteService
    {
        internal const string SessionPath = "/lol-lobby-team-builder/champ-select/v1/session";
        internal const string MutedPlayersPath = "/lol-champ-select/v1/muted-players";

        private readonly ILeagueClientApi _reader;
        private readonly ILeagueChampSelectMuteWriteApi _writer;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public LeagueChampSelectMuteService(ILeagueClientApi reader, ILeagueChampSelectMuteWriteApi writer)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public async Task<LeagueChampSelectMuteResult> MuteAllAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var sessionBytes = await _reader.TryGetBytesAsync(SessionPath, cancellationToken).ConfigureAwait(false);
                var session = Parse(sessionBytes);
                if (session == null) return new LeagueChampSelectMuteResult { NotInChampSelect = true };

                var selfCell = ReadInt(session, "localPlayerCellId");
                var targets = CollectIdentities(session, selfCell);
                if (targets.Count == 0) return new LeagueChampSelectMuteResult();

                var result = new LeagueChampSelectMuteResult();
                foreach (var identity in targets)
                {
                    result.Requested++;
                    var response = await _writer.TryToggleAsync(identity, cancellationToken).ConfigureAwait(false);
                    if (response != null && response.IsSuccessStatusCode) result.Succeeded++;
                    else result.Failed++;
                }

                AppLog.Info("Champ select mute-all: requested=" + result.Requested +
                            "; ok=" + result.Succeeded + "; failed=" + result.Failed);
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// 客户端自己发的 body 是四元组，不是 cellId。
        private static List<string> CollectIdentities(Dictionary<string, object> session, int selfCell)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in new[] { "myTeam", "theirTeam" })
            {
                object raw;
                var members = session.TryGetValue(key, out raw) ? raw as object[] : null;
                if (members == null) continue;
                foreach (var item in members)
                {
                    var member = item as Dictionary<string, object>;
                    if (member == null) continue;
                    var cellId = ReadInt(member, "cellId");
                    if (cellId == selfCell) continue;

                    var summonerId = ReadInt(member, "summonerId");
                    var puuid = ReadString(member, "puuid") ?? string.Empty;
                    var obfuscatedSummonerId = ReadInt(member, "obfuscatedSummonerId");
                    var obfuscatedPuuid = ReadString(member, "obfuscatedPuuid") ?? string.Empty;

                    var identity = "{\"summonerId\":" + summonerId +
                                   ",\"puuid\":\"" + puuid.Replace("\"", string.Empty) +
                                   "\",\"obfuscatedSummonerId\":" + obfuscatedSummonerId +
                                   ",\"obfuscatedPuuid\":\"" + obfuscatedPuuid.Replace("\"", string.Empty) + "\"}";
                    if (seen.Add(identity)) list.Add(identity);
                }
            }
            return list;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        private static List<int> ReadMutedCells(byte[] bytes)
        {
            var cells = new List<int>();
            if (bytes == null || bytes.Length == 0) return cells;
            object raw;
            try
            {
                raw = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes));
            }
            catch (ArgumentException) { return cells; }
            catch (InvalidOperationException) { return cells; }

            var items = raw as object[];
            if (items == null) return cells;
            foreach (var item in items)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                var cellId = ReadInt(entry, "cellId");
                if (cellId >= 0) cells.Add(cellId);
            }
            return cells;
        }

        private static Dictionary<string, object> Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                return new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes))
                    as Dictionary<string, object>;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static int ReadInt(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return -1;
            int parsed;
            return int.TryParse(Convert.ToString(entry, System.Globalization.CultureInfo.InvariantCulture),
                out parsed) ? parsed : -1;
        }
    }
}
