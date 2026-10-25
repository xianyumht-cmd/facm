using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
        public int QueueId { get; set; }
        public int LocalCellId { get; set; }
        public string GameMode { get; set; }
        public string Role { get; set; }
        public int TimeLeftMs { get; set; }
    }

    internal sealed class LeagueAutoBenchEpisodeGuard
    {
        private int _heldChampion;
        private bool _blocked;

        internal bool AcceptObservedChampion(int id)
        {
            if (_blocked || id <= 0) return false;
            if (_heldChampion == 0) _heldChampion = id;
            if (_heldChampion == id) return true;
            _blocked = true; // A manual swap during this episode always takes precedence.
            return false;
        }

        internal void AcceptAutomaticSwap(int id) { _heldChampion = id; }
        internal void BlockUncertainWrite() { _blocked = true; }
    }

    internal sealed class LeagueChampionTradeRequest
    {
        public int Id { get; set; }
        public int RequestedChampionId { get; set; }
    }

    internal sealed class LeagueAutoPickBanController : IDisposable
    {
        internal const string SessionPath = "/lol-champ-select/v1/session";
        internal const string PickablePath = "/lol-champ-select/v1/pickable-champion-ids";
        internal const string BannablePath = "/lol-champ-select/v1/bannable-champion-ids";
        internal const string OngoingChampionTradePath = "/lol-champ-select/v1/ongoing-champion-swap";
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly ILeagueClientApi _read;
        private readonly ILeagueChampSelectActionWriteApi _write;
        private readonly ILeagueBenchSwapWriteApi _benchWrite;
        private readonly ILeagueChampionTradeWriteApi _tradeWrite;
        private readonly ILeagueMatchmakingClock _clock;
        private readonly object _sync = new object();
        private CancellationTokenSource _episode;
        private Task _running;
        private bool _inChampSelect;
        private bool _autoPick;
        private bool _autoBan;
        private bool _temporarilyDisabled;
        private int _pickId;
        private int _banId;
        private string _profilesJson = string.Empty;
        private IReadOnlyList<LeagueAutoSelectProfile> _profiles = Array.Empty<LeagueAutoSelectProfile>();
        private bool _disposed;

        public LeagueAutoPickBanController(ILeagueClientApi read, ILeagueChampSelectActionWriteApi write)
            : this(read, write, null, null, new LeagueMatchmakingSystemClock())
        {
        }

        public LeagueAutoPickBanController(ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write, ILeagueBenchSwapWriteApi benchWrite)
            : this(read, write, benchWrite, null, new LeagueMatchmakingSystemClock())
        {
        }

        public LeagueAutoPickBanController(ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write, ILeagueBenchSwapWriteApi benchWrite,
            ILeagueChampionTradeWriteApi tradeWrite)
            : this(read, write, benchWrite, tradeWrite, new LeagueMatchmakingSystemClock())
        {
        }

        internal LeagueAutoPickBanController(
            ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write,
            ILeagueMatchmakingClock clock)
            : this(read, write, null, null, clock)
        {
        }

        internal LeagueAutoPickBanController(
            ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write,
            ILeagueBenchSwapWriteApi benchWrite,
            ILeagueMatchmakingClock clock)
            : this(read, write, benchWrite, null, clock)
        {
        }

        internal LeagueAutoPickBanController(
            ILeagueClientApi read,
            ILeagueChampSelectActionWriteApi write,
            ILeagueBenchSwapWriteApi benchWrite,
            ILeagueChampionTradeWriteApi tradeWrite,
            ILeagueMatchmakingClock clock)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _benchWrite = benchWrite;
            _tradeWrite = tradeWrite;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void Configure(bool autoPick, int pickId, bool autoBan, int banId)
        {
            Configure(autoPick, pickId, autoBan, banId, string.Empty);
        }

        public void Configure(bool autoPick, int pickId, bool autoBan, int banId, string profilesJson)
        {
            pickId = Math.Max(0, Math.Min(10000, pickId));
            banId = Math.Max(0, Math.Min(10000, banId));
            profilesJson = profilesJson ?? string.Empty;
            var parsedProfiles = LeagueAutoSelectProfiles.Parse(profilesJson);
            CancellationTokenSource previous;
            lock (_sync)
            {
                if (_disposed || (_autoPick == autoPick && _autoBan == autoBan &&
                    _pickId == pickId && _banId == banId && _profilesJson == profilesJson)) return;
                _autoPick = autoPick;
                _autoBan = autoBan;
                _pickId = pickId;
                _banId = banId;
                _profilesJson = profilesJson;
                _profiles = parsedProfiles;
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

        public void SetTemporarilyDisabled(bool disabled)
        {
            CancellationTokenSource previous = null;
            lock (_sync)
            {
                if (_disposed || _temporarilyDisabled == disabled) return;
                _temporarilyDisabled = disabled;
                if (disabled)
                {
                    previous = _episode;
                    _episode = null;
                }
            }
            Cancel(previous);
            if (!disabled) StartIfEligible();
        }

        private void StartIfEligible()
        {
            CancellationTokenSource episode;
            lock (_sync)
            {
                if (_disposed || _temporarilyDisabled || !_inChampSelect || _episode != null ||
                    (!_autoPick || _pickId <= 0 && !_profiles.Any(p => p.Picks.Count > 0)) &&
                    (!_autoBan || _banId <= 0 && !_profiles.Any(p => p.Bans.Count > 0))) return;
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
            var benchGuard = new LeagueAutoBenchEpisodeGuard();
            CompactTraceLog.Record(trace, "autoselect", "phase", "armed", 0);
            try
            {
                while (IsCurrent(episode))
                {
                    try { await TryStepAsync(episode, attempted, reported, trace, benchGuard).ConfigureAwait(false); }
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
            HashSet<string> reported, string trace, LeagueAutoBenchEpisodeGuard benchGuard)
        {
            bool pick;
            bool ban;
            int pickId;
            int banId;
            IReadOnlyList<LeagueAutoSelectProfile> profiles;
            lock (_sync)
            {
                pick = _autoPick;
                ban = _autoBan;
                pickId = _pickId;
                banId = _banId;
                profiles = _profiles;
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
                if (pick && _tradeWrite != null && profiles.Any(p => p.BenchHandleTradeEnabled) &&
                    await TryTradeAsync(episode, session, profiles, pickId,
                        attempted, reported, trace).ConfigureAwait(false)) return;
                // Bench swaps happen after the draft's pick action is over. Reuse this same
                // already-read ChampSelect response and phase-scoped task, never another poller.
                if (pick && _benchWrite != null && profiles.Any(p => p.BenchAutoSwapEnabled))
                    await TryBenchSwapAsync(episode, session, profiles, pickId,
                        attempted, reported, trace, benchGuard).ConfigureAwait(false);
                else RecordOnce(reported, trace, "turn", "not_local");
                return;
            }
            var isPick = string.Equals(action.Type, "pick", StringComparison.Ordinal);
            if (action.Completed || isPick && !pick || !isPick && !ban) return;
            var mode = LeagueAutoSelectProfiles.ModeFor(action.QueueId, action.GameMode);
            var profile = LeagueAutoSelectProfiles.ResolveStrategy(profiles, mode, action.Role);
            var candidates = LeagueAutoSelectProfiles.Candidates(profiles, mode, action.Role, isPick,
                isPick ? pickId : banId);
            if (candidates.Count == 0) return;
            if (action.ChampionId > 0 && !candidates.Contains(action.ChampionId))
            {
                RecordOnce(reported, trace, "turn", "manual_choice");
                return;
            }

            var key = action.Type + ":" + action.Id.ToString(CultureInfo.InvariantCulture);
            if (!attempted.Add(key)) return;

            var watch = Stopwatch.StartNew();
            var availabilityPath = isPick ? PickablePath : BannablePath;
            var available = await _read.TryGetBytesAsync(availabilityPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            var availableIds = ParseAvailableIds(available);
            var desired = action.ChampionId > 0 && availableIds.Contains(action.ChampionId) ? action.ChampionId :
                candidates.FirstOrDefault(availableIds.Contains);
            if (desired <= 0)
            {
                CompactTraceLog.Record(trace, "autoselect", "eligibility", "unavailable", watch.ElapsedMilliseconds);
                return;
            }

            var strategy = isPick ? profile.PickStrategy : profile.BanStrategy;
            var delayMs = isPick ? profile.PickDelayMs : profile.BanDelayMs;
            delayMs = LimitDelay(delayMs, action.TimeLeftMs);
            if (delayMs > 0)
            {
                CompactTraceLog.Record(trace, "autoselect", "delay", "scheduled", 0);
                await _clock.Delay(TimeSpan.FromMilliseconds(delayMs), episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode)) return;
                var recheckedAvailable = await _read.TryGetBytesAsync(availabilityPath, episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode) || !IsAvailable(recheckedAvailable, desired)) return;
            }

            // Recheck the active local action: a manual user choice or turn transition wins.
            if (!await CheckPreflightAsync(episode, action, desired).ConfigureAwait(false))
            {
                CompactTraceLog.Record(trace, "autoselect", "preflight", "phase_mismatch", watch.ElapsedMilliseconds);
                return;
            }

            CompactTraceLog.Record(trace, "autoselect", "preflight", "ok", watch.ElapsedMilliseconds);
            var lockImmediately = strategy == "lock-in-immediately";
            var result = await _write.TryApplyAsync(action.Type, action.Id, desired, lockImmediately, episode.Token)
                .ConfigureAwait(false);
            CompactTraceLog.Record(trace, "autoselect", "write",
                result == null ? "failed_unavailable" :
                result.IsSuccessStatusCode ? (lockImmediately ? "lock_submitted" : "show_submitted") :
                "failed_http_" + result.StatusCode,
                watch.ElapsedMilliseconds);
            if (strategy != "show-and-lock-in" || result == null || !result.IsSuccessStatusCode ||
                !IsCurrent(episode)) return;

            // Show and lock are separate LCU state transitions. Allow the client to register the show.
            await _clock.Delay(TimeSpan.FromMilliseconds(Math.Max(2000, delayMs)), episode.Token)
                .ConfigureAwait(false);
            if (!await CheckPreflightAsync(episode, action, desired).ConfigureAwait(false))
            {
                CompactTraceLog.Record(trace, "autoselect", "lock_preflight", "stale", watch.ElapsedMilliseconds);
                return;
            }
            var locked = await _write.TryApplyAsync(action.Type, action.Id, desired, true, episode.Token)
                .ConfigureAwait(false);
            CompactTraceLog.Record(trace, "autoselect", "lock",
                locked == null ? "failed_unavailable" :
                locked.IsSuccessStatusCode ? "submitted" : "failed_http_" + locked.StatusCode,
                watch.ElapsedMilliseconds);
        }

        private async Task<bool> TryTradeAsync(CancellationTokenSource episode, byte[] session,
            IReadOnlyList<LeagueAutoSelectProfile> profiles, int pickId,
            HashSet<string> attempted, HashSet<string> reported, string trace)
        {
            var state = LeagueLiveDataService.ParseBenchPayload(session);
            if (!state.SessionAvailable || !state.BenchEnabled || state.LocalChampionId <= 0 ||
                !LeagueQueueModePolicy.IsBaseAram(state.QueueId, state.GameMode) &&
                !LeagueQueueModePolicy.IsAramMayhem(state.QueueId, state.GameMode) ||
                !string.Equals(state.TimerPhase, "FINALIZATION", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state.TimerPhase, "BAN_PICK", StringComparison.OrdinalIgnoreCase)) return false;

            var mode = LeagueAutoSelectProfiles.ModeFor(state.QueueId, state.GameMode);
            var profile = LeagueAutoSelectProfiles.ResolveStrategy(profiles, mode, "default");
            if (!profile.BenchHandleTradeEnabled) return false;

            // Only profiles with explicitly enabled trade handling generate this extra read;
            // all observation still happens in the same phase-scoped selection task.
            var response = await _read.TryGetBytesAsync(OngoingChampionTradePath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return true;
            if (response == null)
            {
                RecordOnce(reported, trace, "trade", "unavailable");
                return true; // Do not auto-swap the bench while trade status is unknown.
            }
            var incoming = ParseIncomingChampionTrade(response);
            if (incoming == null) return false;
            var key = "trade:" + incoming.Id;
            if (!attempted.Add(key)) return true;
            var priorities = LeagueAutoSelectProfiles.Candidates(profiles, mode, "default", true, pickId);
            var accept = SelectBenchTarget(priorities, state.LocalChampionId,
                new[] { incoming.RequestedChampionId }, profile.BenchPreferHigherPriority) == incoming.RequestedChampionId;
            var watch = Stopwatch.StartNew();
            var delay = LimitDelay(profile.PickDelayMs, state.TimerMillisecondsLeft);
            if (delay > 0)
            {
                CompactTraceLog.Record(trace, "autoselect-trade", "delay", "scheduled", 0);
                await _clock.Delay(TimeSpan.FromMilliseconds(delay), episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode)) return true;
            }

            var confirmedSession = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return true;
            var current = LeagueLiveDataService.ParseBenchPayload(confirmedSession);
            if (!SameBenchIdentity(state, current) || current.LocalChampionId != state.LocalChampionId)
            {
                CompactTraceLog.Record(trace, "autoselect-trade", "preflight", "phase_or_hero_changed",
                    watch.ElapsedMilliseconds);
                return true;
            }
            var freshTrade = await _read.TryGetBytesAsync(OngoingChampionTradePath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return true;
            var incomingNow = ParseIncomingChampionTrade(freshTrade);
            if (incomingNow == null || incomingNow.Id != incoming.Id ||
                incomingNow.RequestedChampionId != incoming.RequestedChampionId)
            {
                CompactTraceLog.Record(trace, "autoselect-trade", "preflight", "trade_changed",
                    watch.ElapsedMilliseconds);
                return true;
            }
            CompactTraceLog.Record(trace, "autoselect-trade", "preflight", "ok", watch.ElapsedMilliseconds);
            var written = await _tradeWrite.TryAnswerAsync(incoming.Id, accept, episode.Token).ConfigureAwait(false);
            CompactTraceLog.Record(trace, "autoselect-trade", "write",
                written == null ? "failed_unavailable" :
                written.IsSuccessStatusCode ? (accept ? "accept_submitted" : "decline_submitted") :
                "failed_http_" + written.StatusCode,
                watch.ElapsedMilliseconds);
            return true;
        }

        internal static LeagueChampionTradeRequest ParseIncomingChampionTrade(byte[] response)
        {
            if (response == null || response.Length == 0 || response.Length > 32768) return null;
            try
            {
                var data = new JavaScriptSerializer().DeserializeObject(
                    Encoding.UTF8.GetString(response)) as Dictionary<string, object>;
                object initiatedByLocal;
                if (data == null || ReadString(data, "state") != "RECEIVED" ||
                    ReadString(data, "type") != "CHAMPION" ||
                    !data.TryGetValue("initiatedByLocalPlayer", out initiatedByLocal) ||
                    !(initiatedByLocal is bool) || (bool)initiatedByLocal) return null;
                var id = ReadInt(data, "id");
                var champion = ReadInt(data, "requesterChampionId");
                return id > 0 && champion > 0 && champion <= 10000
                    ? new LeagueChampionTradeRequest { Id = id, RequestedChampionId = champion }
                    : null;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            return null;
        }

        private async Task TryBenchSwapAsync(CancellationTokenSource episode, byte[] session,
            IReadOnlyList<LeagueAutoSelectProfile> profiles, int pickId,
            HashSet<string> attempted, HashSet<string> reported, string trace,
            LeagueAutoBenchEpisodeGuard guard)
        {
            var state = await ReadBenchAsync(session, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            if (!state.SessionAvailable || !state.BenchEnabled || state.LocalChampionId <= 0 ||
                !LeagueQueueModePolicy.IsBaseAram(state.QueueId, state.GameMode) &&
                !LeagueQueueModePolicy.IsAramMayhem(state.QueueId, state.GameMode) ||
                !string.Equals(state.TimerPhase, "BAN_PICK", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state.TimerPhase, "FINALIZATION", StringComparison.OrdinalIgnoreCase)) return;

            var mode = LeagueAutoSelectProfiles.ModeFor(state.QueueId, state.GameMode);
            var profile = LeagueAutoSelectProfiles.ResolveStrategy(profiles, mode, "default");
            if (!profile.BenchAutoSwapEnabled) return;
            if (!guard.AcceptObservedChampion(state.LocalChampionId))
            {
                RecordOnce(reported, trace, "bench", "manual_choice_guard");
                return;
            }
            var candidates = LeagueAutoSelectProfiles.Candidates(profiles, mode, "default", true, pickId);
            var desired = SelectBenchTarget(candidates, state.LocalChampionId, state.ChampionIds,
                profile.BenchPreferHigherPriority);
            if (desired <= 0) return;

            var key = "bench:" + state.LocalChampionId + ":" + desired;
            if (!attempted.Add(key)) return;
            var watch = Stopwatch.StartNew();
            var delay = LimitDelay(profile.BenchSwapDelayMs, state.TimerMillisecondsLeft);
            if (delay > 0)
            {
                CompactTraceLog.Record(trace, "autoselect-bench", "delay", "scheduled", 0);
                await _clock.Delay(TimeSpan.FromMilliseconds(delay), episode.Token).ConfigureAwait(false);
                if (!IsCurrent(episode)) return;
            }

            // A single bounded fresh read verifies both the user's held champion and the
            // bench listing. An intervening manual swap or phase change cancels this write.
            var confirmation = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            var fresh = await ReadBenchAsync(confirmation, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            if (!SameBenchContext(state, fresh, desired) ||
                SelectBenchTarget(candidates, fresh.LocalChampionId, fresh.ChampionIds,
                    profile.BenchPreferHigherPriority) != desired)
            {
                CompactTraceLog.Record(trace, "autoselect-bench", "preflight", "stale", watch.ElapsedMilliseconds);
                return;
            }
            CompactTraceLog.Record(trace, "autoselect-bench", "preflight", "ok", watch.ElapsedMilliseconds);
            var response = await _benchWrite.TrySwapAsync(desired, fresh.SwapRoute, episode.Token)
                .ConfigureAwait(false);
            CompactTraceLog.Record(trace, "autoselect-bench", "write",
                response == null ? "failed_unavailable" :
                response.IsSuccessStatusCode ? "submitted" : "failed_http_" + response.StatusCode,
                watch.ElapsedMilliseconds);
            if (response == null || !response.IsSuccessStatusCode || !IsCurrent(episode)) return;

            // Do not retry the POST when the client has acknowledged it but state has not settled.
            // A confirmed new champion is the only permitted continuation of an automatic sequence.
            await _clock.Delay(TimeSpan.FromMilliseconds(250), episode.Token).ConfigureAwait(false);
            var result = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            var settled = await ReadBenchAsync(result, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return;
            if (SameBenchIdentity(state, settled) && settled.LocalChampionId == desired)
            {
                guard.AcceptAutomaticSwap(desired);
                CompactTraceLog.Record(trace, "autoselect-bench", "readback", "confirmed", watch.ElapsedMilliseconds);
            }
            else
            {
                guard.BlockUncertainWrite();
                CompactTraceLog.Record(trace, "autoselect-bench", "readback", "unconfirmed", watch.ElapsedMilliseconds);
            }
        }

        private async Task<LeagueBenchQuickPickState> ReadBenchAsync(byte[] session,
            CancellationToken cancellationToken)
        {
            var primary = LeagueLiveDataService.ParseBenchPayload(session);
            if (!primary.SessionAvailable || !primary.BenchEnabled ||
                primary.SwapRoute != LeagueBenchSwapRoute.TeamBuilder || primary.ChampionIds.Count > 0)
                return primary;

            // Tencent/Team Builder can expose the bench only on the companion endpoint.
            // Do not read it unless automatic bench handling is explicitly active.
            var extra = await _read.TryGetBytesAsync(
                LeagueLiveDataService.TeamBuilderChampSelectSessionPath, cancellationToken).ConfigureAwait(false);
            var fallback = LeagueLiveDataService.ParseBenchPayload(extra);
            if (!fallback.SessionAvailable || !fallback.BenchEnabled || fallback.ChampionIds.Count == 0 ||
                fallback.LocalPlayerCellId != primary.LocalPlayerCellId ||
                fallback.LocalChampionId != primary.LocalChampionId) return primary;
            fallback.QueueId = primary.QueueId;
            fallback.GameMode = primary.GameMode;
            fallback.SwapRoute = LeagueBenchSwapRoute.TeamBuilder;
            if (string.IsNullOrWhiteSpace(fallback.TimerPhase)) fallback.TimerPhase = primary.TimerPhase;
            if (fallback.TimerMillisecondsLeft <= 0) fallback.TimerMillisecondsLeft = primary.TimerMillisecondsLeft;
            return fallback;
        }

        internal static int SelectBenchTarget(IReadOnlyList<int> priorities, int held,
            IReadOnlyList<int> bench, bool upgrade)
        {
            if (held <= 0 || priorities == null || bench == null) return 0;
            var currentIndex = -1;
            for (var i = 0; i < priorities.Count; i++)
                if (priorities[i] == held) { currentIndex = i; break; }
            if (currentIndex == 0) return 0;
            for (var i = 0; i < priorities.Count; i++)
            {
                if (!bench.Contains(priorities[i]) || priorities[i] == held) continue;
                if (currentIndex < 0 || upgrade && i < currentIndex) return priorities[i];
                return 0;
            }
            return 0;
        }

        private static bool SameBenchIdentity(LeagueBenchQuickPickState expected,
            LeagueBenchQuickPickState fresh)
        {
            return expected.SessionAvailable && fresh.SessionAvailable && fresh.BenchEnabled &&
                expected.QueueId == fresh.QueueId && expected.GameMode == fresh.GameMode &&
                expected.LocalPlayerCellId == fresh.LocalPlayerCellId &&
                expected.SwapRoute == fresh.SwapRoute &&
                (string.Equals(fresh.TimerPhase, "BAN_PICK", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(fresh.TimerPhase, "FINALIZATION", StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameBenchContext(LeagueBenchQuickPickState expected,
            LeagueBenchQuickPickState fresh, int desired)
        {
            return SameBenchIdentity(expected, fresh) && fresh.LocalChampionId == expected.LocalChampionId &&
                fresh.ChampionIds.Contains(desired);
        }

        private async Task<bool> CheckPreflightAsync(
            CancellationTokenSource episode, LeagueAutoSelectAction expected, int desired)
        {
            var confirmation = await _read.TryGetBytesAsync(SessionPath, episode.Token).ConfigureAwait(false);
            if (!IsCurrent(episode)) return false;
            var fresh = ParseCurrentAction(confirmation);
            return fresh != null && fresh.Id == expected.Id && fresh.Type == expected.Type &&
                fresh.LocalCellId == expected.LocalCellId &&
                fresh.QueueId == expected.QueueId && fresh.Role == expected.Role &&
                fresh.GameMode == expected.GameMode && !fresh.Completed &&
                (fresh.ChampionId == 0 || fresh.ChampionId == desired);
        }

        private static int LimitDelay(int configuredMs, int timeLeftMs)
        {
            if (configuredMs <= 0) return 0;
            return timeLeftMs > 0 ? Math.Min(configuredMs, Math.Max(0, timeLeftMs - 1000)) : configuredMs;
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
                        new HashSet<string>(StringComparer.Ordinal), CompactTraceLog.NewId(),
                        new LeagueAutoBenchEpisodeGuard()).ConfigureAwait(false);
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
                            Completed = false,
                            QueueId = ReadInt(root, "queueId"),
                            LocalCellId = myCell,
                            GameMode = ReadString(root, "gameMode"),
                            Role = LeagueAutoSelectProfiles.LocalAssignedRole(root),
                            TimeLeftMs = ReadInt((Dictionary<string, object>)root["timer"],
                                "adjustedTimeLeftInPhase")
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
            return championId > 0 && ParseAvailableIds(bytes).Contains(championId);
        }

        private static HashSet<int> ParseAvailableIds(byte[] bytes)
        {
            var available = new HashSet<int>();
            if (bytes == null || bytes.Length > 65536) return available;
            try
            {
                var ids = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
                if (ids == null) return available;
                foreach (var value in ids)
                {
                    var id = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    if (id > 0 && id <= 10000) available.Add(id);
                }
            }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
            return available;
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
