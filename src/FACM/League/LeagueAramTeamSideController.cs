using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    // Observes the existing gameflow feed; no new poller is kept alive while disabled.
    internal sealed class LeagueAramTeamSideController : IDisposable
    {
        internal const string GameflowPath = "/lol-gameflow/v1/session";
        internal const string ChampSelectPath = "/lol-champ-select/v1/session";
        internal const string ConversationsPath = "/lol-chat/v1/conversations";
        private static readonly long RetryTicks = Math.Max(1, Stopwatch.Frequency * 2);

        private readonly ILeagueClientApi _read;
        private readonly ILeagueChampSelectSideWriteApi _write;
        private readonly object _sync = new object();
        private readonly HashSet<string> _attemptedConversations = new HashSet<string>(StringComparer.Ordinal);
        private CancellationTokenSource _active;
        private Task _running;
        private long _lastAttempt;
        private int _generation;
        private bool _enabled;
        private bool _visibleToTeam;
        private bool _inChampSelect;
        private bool _disposed;

        internal LeagueAramTeamSideController(ILeagueClientApi read, ILeagueChampSelectSideWriteApi write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        internal void Configure(bool enabled, bool visibleToTeam)
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || (_enabled == enabled && _visibleToTeam == visibleToTeam)) return;
                _enabled = enabled;
                _visibleToTeam = visibleToTeam;
                _generation++;
                _lastAttempt = 0;
                previous = _active;
                _active = null;
            }
            Cancel(previous);
            StartIfEligible();
        }

        internal void Observe(LeagueDashboardPhaseState state)
        {
            var active = state != null && state.Connected &&
                string.Equals(state.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase);
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed) return;
                if (_inChampSelect && !active)
                {
                    _generation++;
                    _attemptedConversations.Clear();
                    _lastAttempt = 0;
                    previous = _active;
                    _active = null;
                }
                _inChampSelect = active;
            }
            Cancel(previous);
            if (active) StartIfEligible();
        }

        private void StartIfEligible()
        {
            lock (_sync)
            {
                if (_disposed || !_enabled || !_inChampSelect || _active != null ||
                    _attemptedConversations.Count > 0) return;
                var now = Stopwatch.GetTimestamp();
                if (_lastAttempt != 0 && now - _lastAttempt < RetryTicks) return;
                _lastAttempt = now;
                var active = new CancellationTokenSource();
                _active = active;
                var generation = _generation;
                var visible = _visibleToTeam;
                _running = Task.Run(() => RunAsync(active, generation, visible));
            }
        }

        private async Task RunAsync(CancellationTokenSource active, int generation, bool visibleToTeam)
        {
            try
            {
                await AttemptAsync(generation, visibleToTeam, active.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("League ARAM side reminder skipped: " + exception.GetType().Name);
            }
            finally
            {
                lock (_sync)
                    if (ReferenceEquals(_active, active)) _active = null;
                active.Dispose();
            }
        }

        private async Task AttemptAsync(int generation, bool visibleToTeam, CancellationToken token)
        {
            // Require both endpoints: normal gameflow's map mode, and champ select's actual side.
            var mode = ParseMode(await _read.TryGetBytesAsync(GameflowPath, token).ConfigureAwait(false));
            if (mode == null) return;
            var team = ParseTeam(await _read.TryGetBytesAsync(ChampSelectPath, token).ConfigureAwait(false));
            if (team == 0) return;
            var conversation = ParseConversation(await _read.TryGetBytesAsync(ConversationsPath, token).ConfigureAwait(false));
            if (conversation == null) return;

            lock (_sync)
                if (!IsCurrent(generation, visibleToTeam) || _attemptedConversations.Contains(conversation)) return;

            // LCU can replace the whole champ-select episode between these asynchronous reads.
            if (!string.Equals(mode,
                    ParseMode(await _read.TryGetBytesAsync(GameflowPath, token).ConfigureAwait(false)),
                    StringComparison.Ordinal)) return;
            if (team != ParseTeam(await _read.TryGetBytesAsync(ChampSelectPath, token).ConfigureAwait(false))) return;
            if (!string.Equals(conversation,
                    ParseConversation(await _read.TryGetBytesAsync(ConversationsPath, token).ConfigureAwait(false)),
                    StringComparison.Ordinal)) return;

            lock (_sync)
            {
                if (!IsCurrent(generation, visibleToTeam) || !_attemptedConversations.Add(conversation)) return;
            }
            token.ThrowIfCancellationRequested();
            var message = (visibleToTeam ? string.Empty : "[GGman] ") +
                (team == 1 ? "本局游戏你的队伍位于 🟦蓝色方 (↙️ 左下侧)" :
                    "本局游戏你的队伍位于 🟥红色方 (↗️ 右上侧)");
            // An uncertain response must not cause an automatic duplicate in team chat.
            var response = await _write.TrySendSideAsync(conversation, message, visibleToTeam, token)
                .ConfigureAwait(false);
            AppLog.Info("League ARAM side reminder: " +
                (response != null && response.IsSuccessStatusCode ? "submitted" : "unverified"));
        }

        private bool IsCurrent(int generation, bool visible)
        {
            return !_disposed && _inChampSelect && _enabled && _generation == generation &&
                _visibleToTeam == visible;
        }

        internal static string ParseMode(byte[] bytes)
        {
            var root = Decode(bytes) as Dictionary<string, object>;
            var map = Read(root, "map") as Dictionary<string, object>;
            var mode = Read(map, "gameMode") as string;
            return mode == "ARAM" || mode == "KIWI" ? mode : null;
        }

        internal static int ParseTeam(byte[] bytes)
        {
            var root = Decode(bytes) as Dictionary<string, object>;
            if (!ObjectBool(Read(root, "benchEnabled"))) return 0;
            var cell = ObjectInt(Read(root, "localPlayerCellId"));
            if (cell <= 0) return 0;
            var members = Read(root, "myTeam") as IEnumerable;
            if (members == null || members is string) return 0;
            var matching = 0;
            var team = 0;
            foreach (var item in members)
            {
                var member = item as Dictionary<string, object>;
                if (ObjectInt(Read(member, "cellId")) != cell) continue;
                if (++matching > 1) return 0;
                team = ObjectInt(Read(member, "team"));
            }
            return matching == 1 && (team == 1 || team == 2) ? team : 0;
        }

        internal static string ParseConversation(byte[] bytes)
        {
            var rows = Decode(bytes) as IEnumerable;
            if (rows == null || rows is string) return null;
            var count = 0;
            string selected = null;
            foreach (var item in rows)
            {
                if (++count > 100) return null;
                var row = item as Dictionary<string, object>;
                if (!string.Equals(Read(row, "type") as string, "championSelect", StringComparison.Ordinal)) continue;
                var id = Read(row, "id") as string;
                if (!LeagueMatchmakingWriteApiClient.IsSafeChampionSelectConversationId(id) || selected != null) return null;
                selected = id;
            }
            return selected;
        }

        private static object Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 256 * 1024) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = 256 * 1024 }
                .DeserializeObject(Encoding.UTF8.GetString(bytes)); }
            catch { return null; }
        }

        private static object Read(Dictionary<string, object> row, string field)
        {
            object value;
            return row != null && row.TryGetValue(field, out value) ? value : null;
        }

        private static int ObjectInt(object value)
        {
            try { return value == null ? 0 : Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static bool ObjectBool(object value)
        {
            return value is bool && (bool)value;
        }

        private static void Cancel(CancellationTokenSource source)
        {
            if (source == null) return;
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal Task AwaitCurrentForSmokeTestAsync()
        {
            lock (_sync) return _running ?? Task.CompletedTask;
        }

        public void Dispose()
        {
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _generation++;
                previous = _active;
                _active = null;
                _attemptedConversations.Clear();
            }
            Cancel(previous);
        }
    }
}
