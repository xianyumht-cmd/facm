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
    /// are not assumed here. A newly observed notification whose ids positively intersect the
    /// already-known local ChampSelect roster may refine an unknown dodge to ally. An id outside
    /// the local roster is recorded only as evidence until live Tencent logs prove that summonerIds
    /// identifies the dodger.
    ///
    /// The endpoint may retain historical rows. To avoid stale rows being reinterpreted as the
    /// current dodge, the first successful sample is treated as an episode baseline and only a new
    /// row fingerprint can become fresh side evidence. Reasons and ids are kept correlated per row;
    /// they are never unioned across multiple notifications for classification.
    /// </summary>
    internal sealed class LeagueDodgeNotificationEvidenceProbe
    {
        internal const string NotificationsPath = "/lol-lobby/v2/notifications";

        private static readonly TimeSpan EvidenceFreshness = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

        private readonly ILeagueClientApi _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        private readonly long _episode;
        private readonly HashSet<string> _baselineFingerprints = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

        private DateTime _nextSampleUtc = DateTime.MinValue;
        private string _lastShapeFingerprint;
        private bool _lastAvailable;
        private bool _baselineCaptured;
        private DateTime _recentDodgeUtc = DateTime.MinValue;
        private string _recentReason;
        private string _recentNotificationId;
        private string _recentTimestamp;
        private bool _recentAmbiguousBatch;
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
                .Select(row => BuildEvidenceRow(row, roster))
                .Where(row => row != null)
                .ToArray();

            var reasons = dodgeRows
                .Select(row => row.Reason)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var allIds = new HashSet<long>(dodgeRows.SelectMany(row => row.Ids));
            var allOwnMatches = new HashSet<long>(dodgeRows.SelectMany(row => row.OwnMatches));
            var allOutsideOwn = new HashSet<long>(dodgeRows.SelectMany(row => row.OutsideOwn));

            var newRows = new List<NotificationEvidenceRow>();
            if (available)
            {
                if (!_baselineCaptured)
                {
                    _baselineCaptured = true;
                    foreach (var row in dodgeRows)
                    {
                        _baselineFingerprints.Add(row.Fingerprint);
                        _seenFingerprints.Add(row.Fingerprint);
                    }

                    AppLog.Info(
                        "League Dodge Probe: lobby-notification-baseline; episode=" + EpisodeText +
                        "; rows=" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                        "; dodgeRows=" + dodgeRows.Length.ToString(CultureInfo.InvariantCulture) +
                        "; fingerprints=" + dodgeRows.Length.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    foreach (var row in dodgeRows)
                    {
                        if (_seenFingerprints.Add(row.Fingerprint))
                            newRows.Add(row);
                    }
                }
            }

            foreach (var row in newRows)
            {
                AppLog.Info(
                    "League Dodge Probe: lobby-notification-event; episode=" + EpisodeText +
                    "; notificationId=" + Safe(row.NotificationId) +
                    "; timestamp=" + Safe(row.Timestamp) +
                    "; reason=" + Safe(row.Reason) +
                    "; summonerIds=" + FormatIds(row.Ids) +
                    "; ownMatches=" + FormatIds(row.OwnMatches) +
                    "; outsideOwn=" + FormatIds(row.OutsideOwn) +
                    "; baseline=false");
            }

            if (newRows.Count == 1)
            {
                SetRecent(newRows[0], now, false);
            }
            else if (newRows.Count > 1)
            {
                var batchIds = new HashSet<long>(newRows.SelectMany(row => row.Ids));
                var batchOwn = new HashSet<long>(newRows.SelectMany(row => row.OwnMatches));
                var batchOutside = new HashSet<long>(newRows.SelectMany(row => row.OutsideOwn));
                _recentDodgeUtc = now;
                _recentReason = string.Join(",", newRows.Select(row => row.Reason)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
                _recentNotificationId = "multiple";
                _recentTimestamp = "multiple";
                _recentAmbiguousBatch = true;
                _recentIds = batchIds;
                _recentOwnMatches = batchOwn;
                _recentOutsideOwn = batchOutside;

                AppLog.Info(
                    "League Dodge Probe: lobby-notification-ambiguous-batch; episode=" + EpisodeText +
                    "; newRows=" + newRows.Count.ToString(CultureInfo.InvariantCulture) +
                    "; reasons=" + Safe(_recentReason) +
                    "; summonerIds=" + FormatIds(batchIds) +
                    "; ownMatches=" + FormatIds(batchOwn) +
                    "; outsideOwn=" + FormatIds(batchOutside));
            }

            var shape = available.ToString().ToLowerInvariant() + "|" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                        "|" + string.Join(",", reasons) + "|" + FormatIds(allIds) +
                        "|own=" + FormatIds(allOwnMatches) + "|outside=" + FormatIds(allOutsideOwn) +
                        "|baseline=" + _baselineCaptured.ToString().ToLowerInvariant() +
                        "|new=" + newRows.Count.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(shape, _lastShapeFingerprint, StringComparison.Ordinal))
            {
                _lastShapeFingerprint = shape;
                AppLog.Info(
                    "League Dodge Probe: lobby-notifications; episode=" + EpisodeText +
                    "; available=" + available.ToString().ToLowerInvariant() +
                    "; count=" + rows.Count.ToString(CultureInfo.InvariantCulture) +
                    "; dodgeCount=" + dodgeRows.Length.ToString(CultureInfo.InvariantCulture) +
                    "; newDodgeCount=" + newRows.Count.ToString(CultureInfo.InvariantCulture) +
                    "; baselineCaptured=" + _baselineCaptured.ToString().ToLowerInvariant() +
                    "; reasons=" + (reasons.Length == 0 ? "-" : string.Join(",", reasons)) +
                    "; summonerIds=" + FormatIds(allIds) +
                    "; ownMatches=" + FormatIds(allOwnMatches) +
                    "; outsideOwn=" + FormatIds(allOutsideOwn) +
                    "; ownRosterComplete=" + IsOwnRosterComplete(roster).ToString().ToLowerInvariant());
            }

            _lastAvailable = available;
        }

        public LeagueDodgeClassification Refine(
            LeagueDodgeClassification current,
            LeagueDodgeTeamSnapshot roster)
        {
            current = current ?? new LeagueDodgeClassification("unknown", "missing-base-classification");
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            if (!string.Equals(current.Side, "unknown", StringComparison.OrdinalIgnoreCase)) return current;
            if (!IsFresh(_recentDodgeUtc) || _recentAmbiguousBatch) return current;

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
                   ",notificationBaselineCaptured=" + _baselineCaptured.ToString().ToLowerInvariant() +
                   ",notificationReason=" + (fresh ? Safe(_recentReason) : "-") +
                   ",notificationId=" + (fresh ? Safe(_recentNotificationId) : "-") +
                   ",notificationTimestamp=" + (fresh ? Safe(_recentTimestamp) : "-") +
                   ",notificationAmbiguousBatch=" + (fresh && _recentAmbiguousBatch).ToString().ToLowerInvariant() +
                   ",notificationIds=" + (fresh ? FormatIds(_recentIds) : "-") +
                   ",notificationOwnMatches=" + (fresh ? FormatIds(_recentOwnMatches) : "-") +
                   ",notificationOutsideOwn=" + (fresh ? FormatIds(_recentOutsideOwn) : "-") +
                   ",notificationEnemyCandidate=" +
                   (fresh && !_recentAmbiguousBatch && _recentOutsideOwn.Count > 0 &&
                    _recentOwnMatches.Count == 0 && IsOwnRosterComplete(roster))
                       .ToString().ToLowerInvariant();
        }

        internal static void ValidateForSmokeTest()
        {
            var roster = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 5 };
            foreach (var id in new long[] { 11, 12, 13, 14, 15 }) roster.MySummonerIds.Add(id);

            var ownSequence = new SequenceLeagueClientApi(
                "[]",
                "[{\"notificationId\":\"n1\",\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[13],\"timestamp\":2}]");
            var own = new LeagueDodgeNotificationEvidenceProbe(ownSequence, 1);
            own.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var baselineUnknown = own.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (baselineUnknown.Side != "unknown")
                throw new InvalidOperationException("Dodge notification baseline must not classify stale rows.");
            own.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var refined = own.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (refined.Side != "ally" || refined.Basis != "lobby-notification-own-id")
                throw new InvalidOperationException("Dodge notification own-roster refinement regressed.");

            var outsideSequence = new SequenceLeagueClientApi(
                "[]",
                "[{\"notificationId\":\"n2\",\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[99],\"timestamp\":2}]");
            var outside = new LeagueDodgeNotificationEvidenceProbe(outsideSequence, 2);
            outside.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            outside.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var outsideUnknown = outside.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (outsideUnknown.Side != "unknown")
                throw new InvalidOperationException("Dodge notification probe must not infer enemy from unverified outside-roster ids.");

            var stalePartySequence = new SequenceLeagueClientApi(
                "[{\"notificationId\":\"old\",\"notificationReason\":\"PartyDodged\",\"summonerIds\":[11],\"timestamp\":1}]",
                "[{\"notificationId\":\"old\",\"notificationReason\":\"PartyDodged\",\"summonerIds\":[11],\"timestamp\":1},{\"notificationId\":\"new\",\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[99],\"timestamp\":2}]");
            var staleParty = new LeagueDodgeNotificationEvidenceProbe(stalePartySequence, 3);
            staleParty.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            staleParty.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var stalePartyUnknown = staleParty.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (stalePartyUnknown.Side != "unknown")
                throw new InvalidOperationException("Stale PartyDodged notification must not contaminate a new StrangerDodged event.");

            var ambiguousSequence = new SequenceLeagueClientApi(
                "[]",
                "[{\"notificationId\":\"a\",\"notificationReason\":\"PartyDodged\",\"summonerIds\":[11],\"timestamp\":2},{\"notificationId\":\"b\",\"notificationReason\":\"StrangerDodged\",\"summonerIds\":[99],\"timestamp\":2}]");
            var ambiguous = new LeagueDodgeNotificationEvidenceProbe(ambiguousSequence, 4);
            ambiguous.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            ambiguous.SampleAsync(roster, CancellationToken.None, true).GetAwaiter().GetResult();
            var ambiguousUnknown = ambiguous.Refine(new LeagueDodgeClassification("unknown", "test"), roster);
            if (ambiguousUnknown.Side != "unknown")
                throw new InvalidOperationException("Multiple new notification rows must fail closed instead of cross-associating evidence.");
        }

        private void SetRecent(NotificationEvidenceRow row, DateTime now, bool ambiguousBatch)
        {
            row = row ?? new NotificationEvidenceRow();
            _recentDodgeUtc = now;
            _recentReason = row.Reason;
            _recentNotificationId = row.NotificationId;
            _recentTimestamp = row.Timestamp;
            _recentAmbiguousBatch = ambiguousBatch;
            _recentIds = new HashSet<long>(row.Ids);
            _recentOwnMatches = new HashSet<long>(row.OwnMatches);
            _recentOutsideOwn = new HashSet<long>(row.OutsideOwn);
        }

        private static NotificationEvidenceRow BuildEvidenceRow(
            Dictionary<string, object> row,
            LeagueDodgeTeamSnapshot roster)
        {
            if (row == null) return null;
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            var reason = ReadString(row, "notificationReason");
            if (!IsDodgeReason(reason)) return null;

            var ids = new HashSet<long>();
            foreach (var id in ReadLongValues(ReadValue(row, "summonerIds")))
                if (id > 0) ids.Add(id);
            var scalar = ReadLong(row, "summonerId");
            if (scalar > 0) ids.Add(scalar);

            var notificationId = ReadPrimitiveText(row, "notificationId");
            var timestamp = ReadPrimitiveText(row, "timestamp");
            if (string.IsNullOrWhiteSpace(timestamp)) timestamp = ReadPrimitiveText(row, "createdAt");
            var ownMatches = new HashSet<long>(ids.Where(roster.MySummonerIds.Contains));
            var outsideOwn = new HashSet<long>(ids.Where(id => !roster.MySummonerIds.Contains(id)));
            var fingerprint = "id=" + Safe(notificationId) +
                              "|ts=" + Safe(timestamp) +
                              "|reason=" + Safe(reason) +
                              "|ids=" + FormatIds(ids);

            return new NotificationEvidenceRow
            {
                NotificationId = notificationId,
                Timestamp = timestamp,
                Reason = reason,
                Fingerprint = fingerprint,
                Ids = ids,
                OwnMatches = ownMatches,
                OutsideOwn = outsideOwn
            };
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

        private static string ReadPrimitiveText(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            if (value == null) return null;
            if (value is string || value is bool || value is byte || value is sbyte || value is short ||
                value is ushort || value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return null;
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

        private sealed class NotificationEvidenceRow
        {
            public string NotificationId { get; set; }
            public string Timestamp { get; set; }
            public string Reason { get; set; }
            public string Fingerprint { get; set; }
            public HashSet<long> Ids { get; set; } = new HashSet<long>();
            public HashSet<long> OwnMatches { get; set; } = new HashSet<long>();
            public HashSet<long> OutsideOwn { get; set; } = new HashSet<long>();
        }

        private sealed class SequenceLeagueClientApi : ILeagueClientApi
        {
            private readonly Queue<byte[]> _responses = new Queue<byte[]>();
            private byte[] _last;

            public SequenceLeagueClientApi(params string[] responses)
            {
                foreach (var response in responses ?? new string[0])
                    _responses.Enqueue(Encoding.UTF8.GetBytes(response ?? string.Empty));
            }

            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
            {
                if (_responses.Count > 0) _last = _responses.Dequeue();
                return Task.FromResult(_last);
            }
        }
    }
}
