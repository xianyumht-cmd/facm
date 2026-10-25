using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueAutoSelectAction
    {
        public int Id { get; set; }
        public int ChampionId { get; set; }
        public string Type { get; set; }
        public bool Completed { get; set; }
    }

    internal sealed class LeagueAutoPickBanController : IDisposable
    {
        internal const string SessionPath = "/lol-champ-select/v1/session";
        internal const string PickablePath = "/lol-champ-select/v1/pickable-champion-ids";
        internal const string BannablePath = "/lol-champ-select/v1/bannable-champion-ids";
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly ILeagueClientApi _read;
        private readonly ILeagueChampSelectActionWriteApi _write;
        private readonly ILeagueMatchmakingClock _clock;
        private readonly object _sync = new object();
        private CancellationTokenSource _episode;
        private Task _running;
        private bool _inChampSelect;
        private bool _autoPick;
        private bool _autoBan;
        private int _pickId;
        private int _banId;
        private bool _disposed;

        public LeagueAutoPickBanController(ILeagueClientApi read, ILeagueChampSelectActionWriteApi write)
            : this(read, write, new LeagueMatchmakingSystemClock())
        {
        }

        internal LeagueAutoPickBanController(
            ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write,
            ILeagueMatchmakingClock clock)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Configure(bool autoPick, int pickId, bool autoBan, int banId)
        {
            pickId = Math.Max(0, Math.Min(10000, pickId));
            banId = Math.Max(0, Math.Min(10000, banId));
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || (_autoPick == autoPick && _autoBan == autoBan &&
                    _pickId == pickId && _banId == banId)) return;
                _autoPick = autoPick;
                _autoBan = autoBan;
                _pickId = pickId;
                _banId = banId;
                previous = _episode;
                _episode = null;
            }
            Cancel(previous);
            StartIfEligible();
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            var active = state != null && state.Connected &&
                string.Equals(state.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase);
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                _inChampSelect = active;
                if (!active)
                {
                    previous = _episode;
                    _episode = null;
                }
            }
            Cancel(previous);
            if (active) StartIfEligible();
        }

        private void StartIfEligible()
        {
            CancellationTokenSource episode;
            lock (_sync)
            {
                if (_disposed || !_inChampSelect || _episode != null ||
                    (!_autoPick || _pickId <= 0) && (!_autoBan || _banId <= 0)) return;
                episode = new CancellationTokenSource();
                _episode = episode;
            }
            var running = RunEpisodeAsync(episode);
            lock (_sync) if (ReferenceEquals(_episode, episode)) _running = running;
        }

        private async Task RunEpisodeAsync(CancellationTokenSource episode)
        {
            var trace = CompactTraceLog.NewId();
            var attempted = new HashSet<string>(StringComparer.Ordinal);
            var reported = new HashSet<string>(StringComparer.Ordinal);
            CompactTraceLog.Record(trace, "autoselect", "phase", "armed", 0);
            try
            {
                while (IsCurrent(episode))
                {
                    try { await TryStepAsync(episode, attempted, reported, trace).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (episode.IsCancellationRequested) { break; }
                    catch (Exception exception)
                    {
                        var reason = "failed_" + exception.GetType().Name;
                        if (reported.Add(reason))
                        {
                            CompactTraceLog.Record(trace, "autoselect", "step", reason, 0);
                            AppLog.Info("Auto-select step skipped: " + exception.GetType().Name);
                        }
                    }
                    if (!IsCurrent(episode)) break;
                    await _clock.Delay(PollInterval, episode.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (episode.IsCancellationRequested) { }
            catch (Exception exception)
            {
                CompactTraceLog.Record(trace, "autoselect", "loop", "failed_" + exception.GetType().Name, 0);
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_episode, episode)) _episode = null;
                CompactTraceLog.Record(trace, "autoselect", "phase", "stopped", 0);
                episode.Dispose();
            }
        }

        private bool IsCurrent(CancellationTokenSource episode)
        {
            lock (_sync)
                return !_disposed && _inChampSelect && ReferenceEquals(_episode, episode) &&
                    !episode.IsCancellationRequested;
        }

        private async Task TryStepAsync(CancellationTokenSource episode, HashSet<string> attempted,
            HashSet<string> reported, string trace)
        {
            bool pick;
            bool ban;
            int pickId;
            int banId;
            lock (_sync)
            {
                pick = _autoPick;
                ban = _autoBan;
                pickId = _pickId;
                banId = _banId;
            }
            var session = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            if (session == null)
            {
                RecordOnce(reported, trace, "session", "unavailable");
                return;
            }
            var action = ParseCurrentAction(session);
            if (action == null)
            {
                RecordOnce(reported, trace, "turn", "not_local");
                return;
            }
            var desired = string.Equals(action.Type, "pick", StringComparison.Ordinal) ? (pick ? pickId : 0) :
                string.Equals(action.Type, "ban", StringComparison.Ordinal) && ban ? banId : 0;
            if (desired <= 0 || action.Completed) return;
            if (action.ChampionId > 0 && action.ChampionId != desired)
            {
                RecordOnce(reported, trace, "turn", "manual_choice");
                return;
            }

            var key = action.Type + ":" + action.Id.ToString(CultureInfo.InvariantCulture);
            if (!attempted.Add(key)) return;

            var watch = Stopwatch.StartNew();
            var availabilityPath = action.Type == "pick" ? PickablePath : BannablePath;
            var available = await _read.TryGetBytesAsync(availabilityPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            if (!IsAvailable(available, desired))
            {
                CompactTraceLog.Record(trace, "autoselect", "eligibility", "unavailable", watch.ElapsedMilliseconds);
                return;
            }

            // Recheck the active local action: a manual user choice or turn transition wins.
            var confirmation = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            var fresh = ParseCurrentAction(confirmation);
            if (fresh == null || fresh.Id != action.Id || fresh.Type != action.Type || fresh.Completed ||
                fresh.ChampionId > 0 && fresh.ChampionId != desired)
            {
                CompactTraceLog.Record(trace, "autoselect", "preflight", "phase_mismatch", watch.ElapsedMilliseconds);
                return;
            }

            CompactTraceLog.Record(trace, "autoselect", "preflight", "ok", watch.ElapsedMilliseconds);
            var result = await _write.TryApplyAsync(action.Type, action.Id, desired, true, episode.Token)
                .ConfigureAwait(false);
            CompactTraceLog.Record(trace, "autoselect", "write",
                result == null ? "failed_unavailable" :
                result.IsSuccessStatusCode ? "submitted" : "failed_http_" + result.StatusCode,
                watch.ElapsedMilliseconds);
        }

        private static void RecordOnce(HashSet<string> reported, string trace, string stage, string result)
        {
            if (reported.Add(stage + ":" + result)) CompactTraceLog.Record(trace, "autoselect", stage, result, 0);
        }

        internal Task RunOnceForSmokeTestAsync(CancellationToken cancellationToken)
        {
            // Exercises the production planner and transport with the same preflight, but without a timer.
            var episode = new CancellationTokenSource();
            lock (_sync)
            {
                _inChampSelect = true;
                _episode = episode;
            }
            return RunOnceAndDisposeAsync(episode, cancellationToken);
        }

        internal Task RunningForSmokeTest
        {
            get { lock (_sync) return _running ?? Task.FromResult(0); }
        }

        private async Task RunOnceAndDisposeAsync(CancellationTokenSource episode, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => Cancel(episode)))
            {
                try
                {
                    await TryStepAsync(episode, new HashSet<string>(StringComparer.Ordinal),
                        new HashSet<string>(StringComparer.Ordinal), CompactTraceLog.NewId()).ConfigureAwait(false);
                }
                finally
                {
                    lock (_sync) if (ReferenceEquals(_episode, episode)) _episode = null;
                    Cancel(episode);
                    episode.Dispose();
                }
            }
        }

        internal static LeagueAutoSelectAction ParseCurrentAction(byte[] session)
        {
            if (session == null || session.Length == 0 || session.Length > 2 * 1024 * 1024) return null;
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
                var root = json.DeserializeObject(Encoding.UTF8.GetString(session)) as Dictionary<string, object>;
                if (root == null || !ReadString(root, "timer.phase").Equals("BAN_PICK", StringComparison.OrdinalIgnoreCase))
                    return null;
                var myCell = ReadInt(root, "localPlayerCellId");
                if (myCell < 0 || !root.ContainsKey("localPlayerCellId")) return null;
                object groups;
                if (!root.TryGetValue("actions", out groups)) return null;
                foreach (var group in groups as IEnumerable ?? Array.Empty<object>())
                {
                    var actions = new List<Dictionary<string, object>>();
                    var completeGroup = true;
                    foreach (var value in group as IEnumerable ?? Array.Empty<object>())
                    {
                        var item = value as Dictionary<string, object>;
                        if (item == null) return null;
                        actions.Add(item);
                        if (!ReadBool(item, "completed")) completeGroup = false;
                    }
                    if (completeGroup) continue;
                    foreach (var action in actions)
                    {
                        if (!action.ContainsKey("actorCellId") ||
                            !action.ContainsKey("completed") || !action.ContainsKey("isInProgress") ||
                            ReadInt(action, "actorCellId") != myCell || ReadBool(action, "completed") ||
                            !ReadBool(action, "isInProgress")) continue;
                        var type = ReadString(action, "type");
                        var id = ReadInt(action, "id");
                        if (id < 0 || !action.ContainsKey("id") || type != "pick" && type != "ban") continue;
                        return new LeagueAutoSelectAction
                        {
                            Id = id,
                            Type = type,
                            ChampionId = ReadInt(action, "championId"),
                            Completed = false
                        };
                    }
                    return null; // Only the first unfinished action group can contain our current turn.
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return null;
        }

        internal static bool IsAvailable(byte[] bytes, int championId)
        {
            if (championId <= 0 || bytes == null || bytes.Length > 65536) return false;
            try
            {
                var ids = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (ids == null) return false;
                foreach (var value in ids)
                    if (Convert.ToInt32(value, CultureInfo.InvariantCulture) == championId) return true;
            }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
            return false;
        }

        private static int ReadInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value)) return 0;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch (FormatException) { return 0; }
            catch (InvalidCastException) { return 0; }
            catch (OverflowException) { return 0; }
        }

        private static bool ReadBool(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            if (key == "timer.phase")
            {
                object timer;
                return row.TryGetValue("timer", out timer) && timer is Dictionary<string, object>
                    ? ReadString((Dictionary<string, object>)timer, "phase") : string.Empty;
            }
            object value;
            return row.TryGetValue(key, out value) ? value as string ?? string.Empty : string.Empty;
        }

        private static void Cancel(CancellationTokenSource value)
        {
            if (value == null) return;
            try { value.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                previous = _episode;
                _episode = null;
            }
            Cancel(previous);
        }
    }
}
