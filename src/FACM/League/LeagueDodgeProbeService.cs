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
    /// Read-only diagnostic probe for Champion Select dodges. It consumes the shared Gameflow
    /// owner through Observe(), then temporarily samples only read endpoints while a ChampSelect
    /// episode is active (plus a short post-select grace window). It never receives a League write
    /// interface, so this component cannot accept, decline, dodge, search, pick, ban or swap.
    /// </summary>
    internal sealed class LeagueDodgeProbeService : IDisposable
    {
        internal const string SearchPath = "/lol-matchmaking/v1/search";
        internal const string ChampSelectPath = "/lol-champ-select/v1/session";

        private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan TeamRefreshInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan PostChampSelectGrace = TimeSpan.FromSeconds(12);

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };

        private CancellationTokenSource _episodeCancellation;
        private long _episodeGeneration;
        private string _phase = string.Empty;
        private DateTime _graceDeadlineUtc = DateTime.MinValue;
        private bool _disposed;

        public LeagueDodgeProbeService(ILeagueClientApi client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            AppLog.Info(
                "League Dodge Probe initialized; mode=read-only; search=" + SearchPath +
                "; champSelect=" + ChampSelectPath +
                "; sampleMs=" + ((int)SampleInterval.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) +
                "; postSelectGraceMs=" + ((int)PostChampSelectGrace.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            var nextPhase = state != null && state.Connected ? (state.Phase ?? string.Empty) : string.Empty;
            CancellationTokenSource previous = null;
            CancellationTokenSource started = null;
            long generation = 0;

            lock (_sync)
            {
                if (_disposed) return;

                var oldPhase = _phase ?? string.Empty;
                _phase = nextPhase;

                if (IsChampSelect(nextPhase))
                {
                    _graceDeadlineUtc = DateTime.MaxValue;
                    if (!IsChampSelect(oldPhase))
                    {
                        previous = _episodeCancellation;
                        generation = ++_episodeGeneration;
                        started = new CancellationTokenSource();
                        _episodeCancellation = started;
                    }
                }
                else if (_episodeCancellation != null)
                {
                    if (ShouldKeepPostSelectProbe(nextPhase))
                    {
                        if (_graceDeadlineUtc == DateTime.MaxValue || _graceDeadlineUtc == DateTime.MinValue)
                            _graceDeadlineUtc = DateTime.UtcNow + PostChampSelectGrace;
                    }
                    else
                    {
                        previous = _episodeCancellation;
                        _episodeCancellation = null;
                        _graceDeadlineUtc = DateTime.MinValue;
                        _episodeGeneration++;
                    }
                }
            }

            if (previous != null)
            {
                try { previous.Cancel(); }
                catch { }
                try { previous.Dispose(); }
                catch { }
            }

            if (started != null)
            {
                AppLog.Info("League Dodge Probe: episode-start; episode=" + generation.ToString(CultureInfo.InvariantCulture));
                Task.Run(() => RunEpisodeAsync(generation, started.Token));
            }
        }

        private async Task RunEpisodeAsync(long generation, CancellationToken cancellationToken)
        {
            var roster = new LeagueDodgeTeamSnapshot();
            string lastRosterFingerprint = null;
            string baselineDodgeFingerprint = null;
            string lastReportedDodgeFingerprint = null;
            bool baselineCaptured = false;
            bool searchUnavailableLogged = false;
            var nextTeamRefreshUtc = DateTime.MinValue;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    string phase;
                    DateTime graceDeadlineUtc;
                    lock (_sync)
                    {
                        if (_disposed || generation != _episodeGeneration) return;
                        phase = _phase ?? string.Empty;
                        graceDeadlineUtc = _graceDeadlineUtc;
                    }

                    if (!IsChampSelect(phase) && graceDeadlineUtc != DateTime.MaxValue &&
                        graceDeadlineUtc != DateTime.MinValue && DateTime.UtcNow >= graceDeadlineUtc)
                    {
                        AppLog.Info(
                            "League Dodge Probe: episode-end; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                            "; reason=post-select-grace-expired");
                        return;
                    }

                    if (IsChampSelect(phase) && DateTime.UtcNow >= nextTeamRefreshUtc)
                    {
                        var teamBytes = await _client.TryGetBytesAsync(ChampSelectPath, cancellationToken).ConfigureAwait(false);
                        var observedRoster = ParseTeamSnapshot(teamBytes);
                        if (observedRoster != null && observedRoster.SessionAvailable)
                        {
                            roster.MergeFrom(observedRoster);
                            var fingerprint = roster.Fingerprint;
                            if (!string.Equals(lastRosterFingerprint, fingerprint, StringComparison.Ordinal))
                            {
                                lastRosterFingerprint = fingerprint;
                                AppLog.Info(
                                    "League Dodge Probe: roster; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                                    "; phase=" + Safe(phase) +
                                    "; mySlots=" + roster.MyTeamSlots.ToString(CultureInfo.InvariantCulture) +
                                    "; myKnown=" + roster.MySummonerIds.Count.ToString(CultureInfo.InvariantCulture) +
                                    "; theirSlots=" + roster.TheirTeamSlots.ToString(CultureInfo.InvariantCulture) +
                                    "; theirKnown=" + roster.TheirSummonerIds.Count.ToString(CultureInfo.InvariantCulture) +
                                    "; mySummonerIds=" + FormatIds(roster.MySummonerIds) +
                                    "; theirSummonerIds=" + FormatIds(roster.TheirSummonerIds));
                            }
                        }
                        nextTeamRefreshUtc = DateTime.UtcNow + TeamRefreshInterval;
                    }

                    var searchBytes = await _client.TryGetBytesAsync(SearchPath, cancellationToken).ConfigureAwait(false);
                    var dodge = ParseDodgeSnapshot(searchBytes);
                    if (dodge == null)
                    {
                        if (!searchUnavailableLogged)
                        {
                            searchUnavailableLogged = true;
                            AppLog.Info(
                                "League Dodge Probe: search-unavailable; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                                "; phase=" + Safe(phase));
                        }
                    }
                    else
                    {
                        if (searchUnavailableLogged)
                        {
                            searchUnavailableLogged = false;
                            AppLog.Info(
                                "League Dodge Probe: search-recovered; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                                "; phase=" + Safe(phase));
                        }

                        var fingerprint = dodge.Fingerprint;
                        if (!baselineCaptured)
                        {
                            baselineCaptured = true;
                            baselineDodgeFingerprint = fingerprint;
                            AppLog.Info(
                                "League Dodge Probe: search-baseline; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                                "; phase=" + Safe(phase) +
                                "; dodgePresent=" + dodge.Present.ToString().ToLowerInvariant() +
                                (dodge.Present
                                    ? "; state=" + Safe(dodge.State) + "; dodgerId=" + dodge.DodgerId.ToString(CultureInfo.InvariantCulture)
                                    : string.Empty));
                        }
                        else if (dodge.Present &&
                                 !string.Equals(fingerprint, baselineDodgeFingerprint, StringComparison.Ordinal) &&
                                 !string.Equals(fingerprint, lastReportedDodgeFingerprint, StringComparison.Ordinal))
                        {
                            lastReportedDodgeFingerprint = fingerprint;
                            var classification = Classify(dodge, roster);
                            AppLog.Info(
                                "League Dodge Probe: DODGE; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                                "; phase=" + Safe(phase) +
                                "; state=" + Safe(dodge.State) +
                                "; dodgerId=" + dodge.DodgerId.ToString(CultureInfo.InvariantCulture) +
                                "; side=" + classification.Side +
                                "; basis=" + classification.Basis +
                                "; mySlots=" + roster.MyTeamSlots.ToString(CultureInfo.InvariantCulture) +
                                "; myKnown=" + roster.MySummonerIds.Count.ToString(CultureInfo.InvariantCulture) +
                                "; theirSlots=" + roster.TheirTeamSlots.ToString(CultureInfo.InvariantCulture) +
                                "; theirKnown=" + roster.TheirSummonerIds.Count.ToString(CultureInfo.InvariantCulture) +
                                "; mySummonerIds=" + FormatIds(roster.MySummonerIds) +
                                "; theirSummonerIds=" + FormatIds(roster.TheirSummonerIds));
                        }
                    }

                    await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Info(
                    "League Dodge Probe: episode-failed; episode=" + generation.ToString(CultureInfo.InvariantCulture) +
                    "; error=" + exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                lock (_sync)
                {
                    if (generation == _episodeGeneration && _episodeCancellation != null)
                    {
                        try { _episodeCancellation.Dispose(); }
                        catch { }
                        _episodeCancellation = null;
                        _graceDeadlineUtc = DateTime.MinValue;
                    }
                }
            }
        }

        internal LeagueDodgeTeamSnapshot ParseTeamSnapshot(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;

            var output = new LeagueDodgeTeamSnapshot { SessionAvailable = true };
            AppendTeam(output.MySummonerIds, ReadValue(root, "myTeam"), value => output.MyTeamSlots++);
            AppendTeam(output.TheirSummonerIds, ReadValue(root, "theirTeam"), value => output.TheirTeamSlots++);
            return output;
        }

        internal LeagueDodgeSnapshot ParseDodgeSnapshot(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var data = ReadDictionary(root, "dodgeData");
            if (data == null) return new LeagueDodgeSnapshot();

            var state = ReadString(data, "state");
            var dodgerId = ReadLong(data, "dodgerId");
            return new LeagueDodgeSnapshot
            {
                DodgerId = dodgerId,
                State = state,
                Present = dodgerId > 0 || !string.IsNullOrWhiteSpace(state)
            };
        }

        internal static LeagueDodgeClassification Classify(LeagueDodgeSnapshot dodge, LeagueDodgeTeamSnapshot roster)
        {
            roster = roster ?? new LeagueDodgeTeamSnapshot();
            if (dodge == null || !dodge.Present)
                return new LeagueDodgeClassification("unknown", "no-dodge-data");

            if (dodge.DodgerId > 0 && roster.MySummonerIds.Contains(dodge.DodgerId))
            {
                return IsPartyDodged(dodge.State)
                    ? new LeagueDodgeClassification("ally-party", "my-team-id+party-state")
                    : new LeagueDodgeClassification("ally", "my-team-id-match");
            }

            if (dodge.DodgerId > 0 && roster.TheirSummonerIds.Contains(dodge.DodgerId))
                return new LeagueDodgeClassification("enemy", "their-team-id-match");

            if (IsPartyDodged(dodge.State))
                return new LeagueDodgeClassification("ally-party", "party-state");

            if (dodge.DodgerId > 0 && roster.MyTeamSlots > 0 &&
                roster.MySummonerIds.Count == roster.MyTeamSlots)
            {
                return new LeagueDodgeClassification("enemy", "complete-my-team-roster-exclusion");
            }

            return new LeagueDodgeClassification("unknown", "insufficient-team-identity");
        }

        internal static void ValidateForSmokeTest()
        {
            var roster = new LeagueDodgeTeamSnapshot
            {
                SessionAvailable = true,
                MyTeamSlots = 5,
                TheirTeamSlots = 5
            };
            foreach (var id in new long[] { 11, 12, 13, 14, 15 }) roster.MySummonerIds.Add(id);
            roster.TheirSummonerIds.Add(21);

            var ally = Classify(new LeagueDodgeSnapshot { Present = true, DodgerId = 13, State = "StrangerDodged" }, roster);
            if (ally.Side != "ally" || ally.Basis != "my-team-id-match")
                throw new InvalidOperationException("Dodge Probe ally classification regressed.");

            var enemyKnown = Classify(new LeagueDodgeSnapshot { Present = true, DodgerId = 21, State = "StrangerDodged" }, roster);
            if (enemyKnown.Side != "enemy" || enemyKnown.Basis != "their-team-id-match")
                throw new InvalidOperationException("Dodge Probe enemy identity classification regressed.");

            var enemyExcluded = Classify(new LeagueDodgeSnapshot { Present = true, DodgerId = 99, State = "StrangerDodged" }, roster);
            if (enemyExcluded.Side != "enemy" || enemyExcluded.Basis != "complete-my-team-roster-exclusion")
                throw new InvalidOperationException("Dodge Probe complete-roster exclusion regressed.");

            var partial = new LeagueDodgeTeamSnapshot { SessionAvailable = true, MyTeamSlots = 5 };
            partial.MySummonerIds.Add(11);
            var unknown = Classify(new LeagueDodgeSnapshot { Present = true, DodgerId = 99, State = "StrangerDodged" }, partial);
            if (unknown.Side != "unknown")
                throw new InvalidOperationException("Dodge Probe must fail closed on partial identity data.");

            var party = Classify(new LeagueDodgeSnapshot { Present = true, DodgerId = 0, State = "PartyDodged" }, partial);
            if (party.Side != "ally-party")
                throw new InvalidOperationException("Dodge Probe party classification regressed.");
        }

        private static bool ShouldKeepPostSelectProbe(string phase)
        {
            return string.Equals(phase, "Matchmaking", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "PreEndOfGame", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsChampSelect(string phase)
        {
            return string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPartyDodged(string state)
        {
            return string.Equals(state, "PartyDodged", StringComparison.OrdinalIgnoreCase);
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

        private static void AppendTeam(HashSet<long> target, object value, Action<Dictionary<string, object>> onMember)
        {
            foreach (var member in EnumerateDictionaries(value))
            {
                if (onMember != null) onMember(member);
                var id = ReadLong(member, "summonerId");
                if (id > 0) target.Add(id);
            }
        }

        private Dictionary<string, object> ParseObject(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static object ReadValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> ReadDictionary(Dictionary<string, object> source, string key)
        {
            return ReadValue(source, key) as Dictionary<string, object>;
        }

        private static string ReadString(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            var value = ReadValue(source, key);
            if (value == null) return 0L;
            long output;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out output)
                ? output
                : 0L;
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            foreach (var item in EnumerateValues(value))
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null) yield return dictionary;
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

        public void Dispose()
        {
            CancellationTokenSource cancellation = null;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _episodeGeneration++;
                cancellation = _episodeCancellation;
                _episodeCancellation = null;
                _graceDeadlineUtc = DateTime.MinValue;
            }

            if (cancellation != null)
            {
                try { cancellation.Cancel(); }
                catch { }
                try { cancellation.Dispose(); }
                catch { }
            }
        }
    }

    internal sealed class LeagueDodgeTeamSnapshot
    {
        public bool SessionAvailable { get; set; }
        public int MyTeamSlots { get; set; }
        public int TheirTeamSlots { get; set; }
        public HashSet<long> MySummonerIds { get; private set; } = new HashSet<long>();
        public HashSet<long> TheirSummonerIds { get; private set; } = new HashSet<long>();

        public string Fingerprint
        {
            get
            {
                return MyTeamSlots.ToString(CultureInfo.InvariantCulture) + ":" + Join(MySummonerIds) +
                       "|" + TheirTeamSlots.ToString(CultureInfo.InvariantCulture) + ":" + Join(TheirSummonerIds);
            }
        }

        public void MergeFrom(LeagueDodgeTeamSnapshot other)
        {
            if (other == null || !other.SessionAvailable) return;
            SessionAvailable = true;
            MyTeamSlots = Math.Max(MyTeamSlots, other.MyTeamSlots);
            TheirTeamSlots = Math.Max(TheirTeamSlots, other.TheirTeamSlots);
            foreach (var id in other.MySummonerIds) if (id > 0) MySummonerIds.Add(id);
            foreach (var id in other.TheirSummonerIds) if (id > 0) TheirSummonerIds.Add(id);
        }

        private static string Join(IEnumerable<long> ids)
        {
            var values = ids == null ? new long[0] : ids.Where(value => value > 0).Distinct().OrderBy(value => value).ToArray();
            return values.Length == 0
                ? "-"
                : string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
    }

    internal sealed class LeagueDodgeSnapshot
    {
        public bool Present { get; set; }
        public long DodgerId { get; set; }
        public string State { get; set; }

        public string Fingerprint
        {
            get
            {
                return Present
                    ? DodgerId.ToString(CultureInfo.InvariantCulture) + "|" + (State ?? string.Empty).Trim()
                    : string.Empty;
            }
        }
    }

    internal sealed class LeagueDodgeClassification
    {
        public LeagueDodgeClassification(string side, string basis)
        {
            Side = string.IsNullOrWhiteSpace(side) ? "unknown" : side;
            Basis = string.IsNullOrWhiteSpace(basis) ? "unknown" : basis;
        }

        public string Side { get; private set; }
        public string Basis { get; private set; }
    }
}
