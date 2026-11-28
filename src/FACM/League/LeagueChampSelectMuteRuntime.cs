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
        public bool StateUnavailable { get; set; }
        public int AlreadyMuted { get; set; }

        public string Summary
        {
            get
            {
                if (NotInChampSelect) return "不在选人阶段";
                if (StateUnavailable) return "静音状态不可用" + (Succeeded > 0 ? "，已确认静音 " + Succeeded + " 人" : "");
                if (Requested == 0) return AlreadyMuted > 0 ? "玩家已静音" : "没有需要静音的玩家";
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
        internal const string PhasePath = "/lol-gameflow/v1/gameflow-phase";

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
                var result = new LeagueChampSelectMuteResult();
                var session = await ReadActiveSessionAsync(cancellationToken).ConfigureAwait(false);
                if (session == null) { result.NotInChampSelect = true; return result; }
                var selfCell = ReadId(session, "localPlayerCellId");
                if (selfCell == null || selfCell < 0) { result.StateUnavailable = true; return result; }
                var targets = CollectIdentities(session, selfCell.Value);
                if (targets == null) { result.StateUnavailable = true; return result; }
                if (targets.Count == 0) return result;
                var roster = RosterKey(session);
                if (roster == null) { result.StateUnavailable = true; return result; }

                foreach (var target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = await ReadActiveSessionAsync(cancellationToken).ConfigureAwait(false);
                    if (current == null || !string.Equals(roster, RosterKey(current), StringComparison.Ordinal))
                    { result.StateUnavailable = true; break; }

                    var muted = ReadMutedIdentities(await _reader.TryGetBytesAsync(
                        MutedPlayersPath, cancellationToken).ConfigureAwait(false));
                    if (muted == null) { result.StateUnavailable = true; break; }
                    if (muted.Contains(target)) { result.AlreadyMuted++; continue; }

                    result.Requested++;
                    var response = await _writer.TryToggleAsync(target, cancellationToken).ConfigureAwait(false);
                    var settled = ReadMutedIdentities(await _reader.TryGetBytesAsync(
                        MutedPlayersPath, cancellationToken).ConfigureAwait(false));
                    if (response != null && response.IsSuccessStatusCode && settled != null && settled.Contains(target))
                        result.Succeeded++;
                    else
                    {
                        result.Failed++;
                        // A toggle with an uncertain outcome must not be retried.
                        if (response == null || settled == null) { result.StateUnavailable = true; break; }
                    }
                }

                AppLog.Info("Champ select mute-all: requested=" + result.Requested +
                            "; already=" + result.AlreadyMuted + "; ok=" + result.Succeeded + "; failed=" + result.Failed);
                return result;
            }
            finally { _gate.Release(); }
        }

        private async Task<Dictionary<string, object>> ReadActiveSessionAsync(CancellationToken token)
        {
            var phase = await _reader.TryGetBytesAsync(PhasePath, token).ConfigureAwait(false);
            if (phase == null || !string.Equals(Encoding.UTF8.GetString(phase).Trim().Trim('"'),
                    "ChampSelect", StringComparison.OrdinalIgnoreCase)) return null;
            return Parse(await _reader.TryGetBytesAsync(SessionPath, token).ConfigureAwait(false));
        }

        private static List<string> CollectIdentities(Dictionary<string, object> session, long selfCell)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in new[] { "myTeam", "theirTeam" })
            {
                object raw;
                var members = session.TryGetValue(key, out raw) ? raw as object[] : null;
                if (members == null) return null;
                foreach (var item in members)
                {
                    var member = item as Dictionary<string, object>;
                    var cellId = ReadId(member, "cellId");
                    if (cellId == null || cellId < 0 || cellId == selfCell) continue;
                    var identity = IdentityKey(member);
                    if (identity != null && seen.Add(identity)) list.Add(identity);
                }
            }
            return list;
        }

        private static string RosterKey(Dictionary<string, object> session)
        {
            var selfCell = ReadId(session, "localPlayerCellId");
            if (selfCell == null || selfCell < 0) return null;
            var entries = CollectIdentities(session, selfCell.Value);
            if (entries == null) return null;
            entries.Sort(StringComparer.Ordinal);
            return new JavaScriptSerializer().Serialize(new object[]
            {
                ReadString(session, "gameId"), selfCell.Value, entries
            });
        }

        private static string IdentityKey(Dictionary<string, object> value)
        {
            var summoner = ReadId(value, "summonerId");
            var obfuscated = ReadId(value, "obfuscatedSummonerId");
            var puuid = ReadString(value, "puuid");
            var obfuscatedPuuid = ReadString(value, "obfuscatedPuuid");
            if (summoner == null || obfuscated == null || summoner < 0 || obfuscated < 0 ||
                puuid == null || obfuscatedPuuid == null) return null;
            if (summoner == 0 && obfuscated == 0 && !IsMeaningfulIdentity(puuid) &&
                !IsMeaningfulIdentity(obfuscatedPuuid)) return null;
            return new JavaScriptSerializer().Serialize(new
            {
                summonerId = summoner.Value, puuid = puuid,
                obfuscatedSummonerId = obfuscated.Value, obfuscatedPuuid = obfuscatedPuuid
            });
        }

        private static bool IsMeaningfulIdentity(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.Equals(value, "00000000-0000-0000-0000-000000000000", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> ReadMutedIdentities(byte[] bytes)
        {
            var items = ParseObject(bytes) as object[];
            if (items == null) return null;
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var identity = IdentityKey(item as Dictionary<string, object>);
                if (identity == null) return null;
                result.Add(identity);
            }
            return result;
        }

        private static object ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static Dictionary<string, object> Parse(byte[] bytes)
        {
            return ParseObject(bytes) as Dictionary<string, object>;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        private static long? ReadId(Dictionary<string, object> value, string key)
        {
            long parsed;
            return long.TryParse(ReadString(value, key), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) ? (long?)parsed : null;
        }
    }
}
