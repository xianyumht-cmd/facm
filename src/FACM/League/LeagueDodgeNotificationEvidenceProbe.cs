using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Read-only empirical probe for /lol-lobby/v2/notifications.
    ///
    /// Generated LCU schemas expose notificationReason + summonerIds, but Tencent payload semantics
    /// are not assumed here. A notification id that positively intersects the already-known local
    /// ChampSelect roster may refine an unknown dodge to ally. An id outside the local roster is
    /// recorded only as evidence until live Tencent logs prove that summonerIds identifies the dodger.
    /// </summary>
    internal sealed class LeagueDodgeNotificationEvidenceProbe
    {
        internal const string NotificationsPath = "/lol-lobby/v2/notifications";

        private static readonly TimeSpan EvidenceFreshness = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

        private readonly ILeagueClientApi _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        private readonly long _episode;

        private DateTime _nextSampleUtc = DateTime.MinValue;
        private string _lastShapeFingerprint;
        private bool _lastAvailable;
        private DateTime _recentDodgeUtc = DateTime.MinValue;
        private string _recentReason;
        private HashSet<long> _recentIds = new HashSet<long>();
        private HashSet<long> _recentOwnMatches = new HashSet<long>();
        private HashSet<long> _recentOutsideOwn = new HashSet<long>();

        public LeagueDodgeNotificationEvidenceProbe(ILeagueClientApi client, long episode)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _episode = episode;
        }

        public async Task SampleAsync(
            LeagueDodgeTeamSnapshot roster,
            CancellationToken cancellationToken,
            bool force = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            var now = DateTime.UtcNow;
            if (!force && now < _nextSampleUtc) return;
            _nextSampleUtc = now + SampleInterval;

            var bytes = await _client.TryGetBytesAsync(NotificationsPath, cancellationToken).ConfigureAwait(false);
            List<Dictionary<string, object>> rows;
            var available = TryParseRows(bytes, out rows);
            rows = rows ?? new List<Dictionary<string, object>>();

            var dodgeRows = rows
                .Where(row => IsDodgeReason(ReadString(row, "notificationReason")))
                .ToArray();
            var reasons = dodgeRows
                .Select(row => ReadString(row, "notificationReason"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var ids = new HashSet<long>();
            foreach (var row in dodgeRows)
            {
                foreach (var id in ReadLongValues(ReadValue(row, "summonerIds")))
                    if (id > 0) ids.Add(id);
                var scalar = ReadLong(row, "summonerId");
                if (scalar > 0) ids.Add(scalar);
            }

            var ownMatches = new HashSet<long>(ids.Where(roster.MySummonerIds.Contains));
            var outsideOwn = new HashSet<long>(ids.Where(id => !roster.MySummonerIds.Contains(id)));
            var shape = available.ToString().ToLowerInvariant() + "|" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                        "|" + string.Join(",", reasons) + "|" + FormatIds(ids) +
                        "|own=" + FormatIds(ownMatches) + "|outside=" + FormatIds(outsideOwn);
            if (!string.Equals(shape, _lastShapeFingerprint, StringComparison.Ordinal))
            {
                _lastShapeFingerprint = shape;
                AppLog.Info(
                    "League Dodge Probe: lobby-notifications; episode=" + EpisodeText +
                    "; available=" + available.ToString().ToLowerInvariant() +
                    "; count=" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                    "; dodgeCount=" + dodgeRows.Length.ToString(CultureInfo.InvariantCulture) +
                    "; reasons=" + (reasons.Length == 0 ? "-" : string.Join(",", reasons)) +
                    "; summonerIds=" + FormatIds(ids) +
                    "; ownMatches=" + FormatIds(ownMatches) +
                    "; outsideOwn=" + FormatIds(outsideOwn) +
                    "; ownRosterComplete=" + IsOwnRosterComplete(roster).ToString().ToLowerInvariant());
            }

            _lastAvailable = available;
            if (dodgeRows.Length > 0)
            {
                _recentDodgeUtc = now;
                _recentReason = reasons.FirstOrDefault();
                _recentIds = ids;
                _recentOwnMatches = ownMatches;
                _recentOutsideOwn = outsideOwn;
            }
        }

        public LeagueDodgeClassification Refine(
            LeagueDodgeClassification current,
            LeagueDodgeTeamSnapshot roster)
        {
            current = current ?? new LeagueDodgeClassification("unknown", "missing-base-classification");
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            if (!string.Equals(current.Side, "unknown", StringComparison.OrdinalIgnoreCase)) return current;
            if (!IsFresh(_recentDodgeUtc)) return current;

            if (string.Equals(_recentReason, "PartyDodged", StringComparison.OrdinalIgnoreCase))
                return new LeagueDodgeClassification("ally-party", "lobby-notification-party-state");

            if (_recentOwnMatches.Count > 0)
                return new LeagueDodgeClassification("ally", "lobby-notification-own-id");

            // Do NOT infer enemy from a StrangerDodged notification or from lack of an ally match.
            // Tencent must first prove that notification summonerIds is the dodger identity.
            return current;
        }

        public string BuildSummary(LeagueDodgeTeamSnapshot roster)
        {
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            var fresh = IsFresh(_recentDodgeUtc);
            return "notificationAvailable=" + _lastAvailable.ToString().ToLowerInvariant() +
                   ",notificationReason=" + (fresh ? Safe(_recentReason) : "-") +
                   ",notificationIds=" + (fresh ? FormatIds(_recentIds) : "-") +
                   ",notificationOwnMatches=" + (fresh ? FormatIds(_recentOwnMatches) : "-") +
                   ",notificationOutsideOwn=" + (fresh ? FormatIds(_recentOutsideOwn) : "-") +
                   ",notificationEnemyCandidate=" +
                   (fresh && _recentOutsideOwn.Count > 0 && _recentOwnMatches.Count == 0 && IsOwnRosterComplete(roster))
                       .ToString().ToLowerInvariant();
        }

        internal static void ValidateForSmokeTest()
        {
            var json = "[{\"notificationId\":\"n1\",\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[13],\"timestamp\":1}]";
            var probe = new LeagueDodgeNotificationEvidenceProbe(new FixedLeagueClientApi(Encoding.UTF8.GetBytes(json)), 1);
            var roster = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 5 };
            foreach (var id in new long[] { 11, 12, 13, 14, 15 }) roster.MySummonerIds.Add(id);
            probe.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var refined = probe.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (refined.Side != "ally" || refined.Basis != "lobby-notification-own-id")
                throw new InvalidOperationException("Dodge notification own-roster refinement regressed.");

            var outsideJson = "[{\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[99]}]";
            var outside = new LeagueDodgeNotificationEvidenceProbe(new FixedLeagueClientApi(Encoding.UTF8.GetBytes(outsideJson)), 2);
            outside.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var unknown = outside.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (unknown.Side != "unknown")
                throw new InvalidOperationException("Dodge notification probe must not infer enemy from unverified outside-roster ids.");
        }

        private bool TryParseRows(byte[] bytes, out List<Dictionary<string, object>> rows)
        {
            rows = null;
            if (bytes == null || bytes.Length == 0) return false;
            object root;
            try { root = _json.DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return false; }

            rows = new List<Dictionary<string, object>>();
            var direct = root as Dictionary<string, object>;
            if (direct != null)
            {
                rows.Add(direct);
                return true;
            }
            foreach (var item in EnumerateValues(root))
            {
                var row = item as Dictionary<string, object>;
                if (row != null) rows.Add(row);
            }
            return true;
        }

        private static bool IsDodgeReason(string reason)
        {
            return string.Equals(reason, "PartyDodged", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(reason, "StrangerDodged", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(reason, "TournamentDodged", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOwnRosterComplete(LeagueDodgeTeamSnapshot roster)
        {
            return roster != null && roster.MyTeamSlots > 0 && roster.MySummonerIds.Count == roster.MyTeamSlots;
        }

        private static IEnumerable<long> ReadLongValues(object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                long parsed;
                if (item != null && long.TryParse(
                    Convert.ToString(item, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed) && parsed > 0)
                    yield return parsed;
            }
        }

        private static IEnumerable<object> EnumerateValues(object value)
        {
            if (value == null) yield break;
            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array) yield return item;
                yield break;
            }
            var list = value as ArrayList;
            if (list != null)
            {
                foreach (var item in list) yield return item;
            }
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            long parsed;
            return value != null && long.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : 0L;
        }

        private static string FormatIds(IEnumerable<long> ids)
        {
            if (ids == null) return "-";
            var values = ids.Where(value => value > 0).Distinct().OrderBy(value => value).ToArray();
            return values.Length == 0
                ? "-"
                : string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            return value.Replace("\r", " ").Replace("\n", " ").Replace(";", ",").Trim();
        }

        private static bool IsFresh(DateTime timestampUtc)
        {
            return timestampUtc != DateTime.MinValue && DateTime.UtcNow - timestampUtc <= EvidenceFreshness;
        }

        private string EpisodeText
        {
            get { return _episode.ToString(CultureInfo.InvariantCulture); }
        }

        private sealed class FixedLeagueClientApi : ILeagueClientApi
        {
            private readonly byte[] _bytes;
            public FixedLeagueClientApi(byte[] bytes) { _bytes = bytes; }
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                return Task.FromResult(_bytes);
            }
        }
    }
}
