using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal interface ILeaguePostGameClock
    {
        Task Delay(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal sealed class LeaguePostGameSystemClock : ILeaguePostGameClock
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }

    internal sealed class LeagueHonorCandidate
    {
        public string Puuid { get; set; }
        public bool BotPlayer { get; set; }
    }

    internal sealed class LeagueHonorBallot
    {
        public long GameId { get; set; }
        public int Votes { get; set; }
        public List<LeagueHonorCandidate> Allies { get; private set; } = new List<LeagueHonorCandidate>();
    }

    internal sealed class LeaguePostGameAutomationController : IDisposable
    {
        internal const string BallotPath = "/lol-honor-v2/v1/ballot/";
        internal const string CurrentSummonerPath = "/lol-summoner/v1/current-summoner";
        private static readonly TimeSpan BallotPollInterval = TimeSpan.FromMilliseconds(650);
        private static readonly TimeSpan BallotWaitLimit = TimeSpan.FromMilliseconds(3250);
        private static readonly TimeSpan WaitingForStatsFallbackRemainder = TimeSpan.FromMilliseconds(6750);

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeaguePostGameWriteApi _write;
        private readonly ILeaguePostGameClock _clock;
        private readonly Func<int, int> _chooseIndex;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private CancellationTokenSource _cycleCancellation;
        private bool _autoHonor;
        private bool _autoReturn;
        private bool _insidePostGame;
        private bool _cycleStarted;
        private bool _disposed;

        public LeaguePostGameAutomationController(ILeagueClientApi read, ILeaguePostGameWriteApi write)
            : this(read, write, new LeaguePostGameSystemClock(), CreateRandomIndex)
        {
        }

        internal LeaguePostGameAutomationController(
            ILeagueClientApi read,
            ILeaguePostGameWriteApi write,
            ILeaguePostGameClock clock,
            Func<int, int> chooseIndex)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _chooseIndex = chooseIndex ?? throw new ArgumentNullException(nameof(chooseIndex));
        }

        public void Configure(bool autoHonor, bool autoReturn)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _autoHonor = autoHonor;
                _autoReturn = autoReturn;
                if (!_autoHonor && !_autoReturn) CancelCycleLocked();
            }
        }

        public void Observe(LeagueDashboardPhaseState state)
        {
            string phase;
            CancellationToken token;
            if (!TryBeginCycle(state, out phase, out token)) return;
            RunCycleAsync(phase, token).Forget("League post-game automation");
        }

        internal async Task ObserveForSmokeTestAsync(LeagueDashboardPhaseState state)
        {
            string phase;
            CancellationToken token;
            if (!TryBeginCycle(state, out phase, out token)) return;
            await RunCycleAsync(phase, token).ConfigureAwait(false);
        }

        internal async Task RunCycleForSmokeTestAsync(
            string phase,
            bool autoHonor,
            bool autoReturn,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _autoHonor = autoHonor;
                _autoReturn = autoReturn;
                _insidePostGame = true;
            }
            await RunCycleAsync(phase, cancellationToken).ConfigureAwait(false);
        }

        private bool TryBeginCycle(LeagueDashboardPhaseState state, out string phase, out CancellationToken token)
        {
            phase = state == null ? null : state.Phase;
            token = CancellationToken.None;
            var postGame = state != null && state.Connected && IsPostGamePhase(phase);
            lock (_sync)
            {
                if (_disposed) return false;
                if (!postGame)
                {
                    _insidePostGame = false;
                    _cycleStarted = false;
                    CancelCycleLocked();
                    return false;
                }

                _insidePostGame = true;
                if (_cycleStarted || (!_autoHonor && !_autoReturn)) return false;
                _cycleStarted = true;
                CancelCycleLocked();
                _cycleCancellation = new CancellationTokenSource();
                token = _cycleCancellation.Token;
                return true;
            }
        }

        private async Task RunCycleAsync(string initialPhase, CancellationToken cancellationToken)
        {
            bool honor;
            bool playAgain;
            lock (_sync)
            {
                honor = _autoHonor;
                playAgain = _autoReturn;
            }

            LeagueHonorBallot ballot = null;
            if (honor)
            {
                ballot = await WaitForBallotAsync(cancellationToken).ConfigureAwait(false);
                if (ballot != null && ballot.GameId > 0 && ballot.Votes > 0)
                    await TryHonorOneAllyAsync(ballot, cancellationToken).ConfigureAwait(false);
            }

            if (!playAgain) return;
            if (!honor)
            {
                await _clock.Delay(ResolveReturnDelay(initialPhase), cancellationToken).ConfigureAwait(false);
            }
            else if (ballot != null)
            {
                await _clock.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(initialPhase, "WaitingForStats", StringComparison.OrdinalIgnoreCase))
            {
                // Akari keeps a 10s WaitingForStats fallback. FACM spends at most the first 3.25s
                // looking for an honor ballot, then only waits the remaining 6.75s before returning.
                await _clock.Delay(WaitingForStatsFallbackRemainder, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsStillEnabledForReturn()) return;
            var response = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.PlayAgainPath,
                null,
                cancellationToken).ConfigureAwait(false);
            AppLog.Info("League auto return lobby: " + (response != null && response.IsSuccessStatusCode ? "success" : "failed"));
        }

        private async Task<LeagueHonorBallot> WaitForBallotAsync(CancellationToken cancellationToken)
        {
            var elapsed = TimeSpan.Zero;
            while (elapsed < BallotWaitLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStillEnabledForHonor()) return null;
                var bytes = await _read.TryGetBytesAsync(BallotPath, cancellationToken).ConfigureAwait(false);
                var ballot = ParseBallot(bytes);
                if (ballot != null && ballot.GameId > 0) return ballot;

                var remaining = BallotWaitLimit - elapsed;
                var delay = remaining < BallotPollInterval ? remaining : BallotPollInterval;
                if (delay <= TimeSpan.Zero) break;
                await _clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                elapsed += delay;
            }
            return null;
        }

        private async Task TryHonorOneAllyAsync(LeagueHonorBallot ballot, CancellationToken cancellationToken)
        {
            var selfPuuid = await TryReadSelfPuuidAsync(cancellationToken).ConfigureAwait(false);
            var candidates = ballot.Allies
                .Where(item => item != null && !item.BotPlayer && !string.IsNullOrWhiteSpace(item.Puuid))
                .Where(item => string.IsNullOrEmpty(selfPuuid) || !string.Equals(item.Puuid, selfPuuid, StringComparison.Ordinal))
                .GroupBy(item => item.Puuid, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (candidates.Count == 0) return;

            var index = _chooseIndex(candidates.Count);
            if (index < 0 || index >= candidates.Count) index = 0;
            var selected = candidates[index];
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "honorType", "HEART" },
                { "recipientPuuid", selected.Puuid }
            });
            var honorResponse = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.HonorPath,
                body,
                cancellationToken).ConfigureAwait(false);
            if (honorResponse == null || !honorResponse.IsSuccessStatusCode)
            {
                AppLog.Info("League auto honor teammate: failed");
                return;
            }

            var ballotResponse = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.HonorBallotSubmitPath,
                null,
                cancellationToken).ConfigureAwait(false);
            AppLog.Info("League auto honor teammate: " + (ballotResponse != null && ballotResponse.IsSuccessStatusCode ? "success" : "ballot-submit-failed"));
        }

        private async Task<string> TryReadSelfPuuidAsync(CancellationToken cancellationToken)
        {
            var bytes = await _read.TryGetBytesAsync(CurrentSummonerPath, cancellationToken).ConfigureAwait(false);
            var root = ParseObject(bytes);
            object value;
            return root != null && root.TryGetValue("puuid", out value) && value != null ? Convert.ToString(value) : null;
        }

        internal LeagueHonorBallot ParseBallot(byte[] bytes)
        {
            var root = ParseObject(bytes);
            if (root == null) return null;
            var ballot = new LeagueHonorBallot { GameId = ReadLong(root, "gameId") };
            var votePool = ReadDictionary(root, "votePool");
            ballot.Votes = ReadInt(votePool, "votes");
            foreach (var row in EnumerateDictionaries(ReadValue(root, "eligibleAllies")))
            {
                ballot.Allies.Add(new LeagueHonorCandidate
                {
                    Puuid = ReadString(row, "puuid"),
                    BotPlayer = ReadBool(row, "botPlayer")
                });
            }
            return ballot;
        }

        internal static bool IsPostGamePhase(string phase)
        {
            return string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "PreEndOfGame", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(phase, "EndOfGame", StringComparison.OrdinalIgnoreCase);
        }

        internal static TimeSpan ResolveReturnDelay(string phase)
        {
            if (string.Equals(phase, "WaitingForStats", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromSeconds(10);
            if (string.Equals(phase, "PreEndOfGame", StringComparison.OrdinalIgnoreCase)) return TimeSpan.FromMilliseconds(3250);
            return TimeSpan.FromMilliseconds(1575);
        }

        private bool IsStillEnabledForHonor()
        {
            lock (_sync) return !_disposed && _insidePostGame && _autoHonor;
        }

        private bool IsStillEnabledForReturn()
        {
            lock (_sync) return !_disposed && _insidePostGame && _autoReturn;
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
            return value == null ? null : Convert.ToString(value);
        }

        private static long ReadLong(Dictionary<string, object> source, string key)
        {
            try { return Convert.ToInt64(ReadValue(source, key)); }
            catch { return 0; }
        }

        private static int ReadInt(Dictionary<string, object> source, string key)
        {
            try { return Convert.ToInt32(ReadValue(source, key)); }
            catch { return 0; }
        }

        private static bool ReadBool(Dictionary<string, object> source, string key)
        {
            try { return Convert.ToBoolean(ReadValue(source, key)); }
            catch { return false; }
        }

        private static IEnumerable<Dictionary<string, object>> EnumerateDictionaries(object value)
        {
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) yield break;
            foreach (var item in enumerable)
            {
                var row = item as Dictionary<string, object>;
                if (row != null) yield return row;
            }
        }

        private static int CreateRandomIndex(int count)
        {
            if (count <= 1) return 0;
            return new Random(unchecked(Environment.TickCount * 31 + Thread.CurrentThread.ManagedThreadId)).Next(count);
        }

        private void CancelCycleLocked()
        {
            var cancellation = _cycleCancellation;
            _cycleCancellation = null;
            if (cancellation != null)
            {
                try { cancellation.Cancel(); }
                catch { }
                cancellation.Dispose();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CancelCycleLocked();
            }
        }
    }

    internal static class LeagueTaskExtensions
    {
        public static async void Forget(this Task task, string operation)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { AppLog.Error(operation + " failed", exception); }
        }
    }
}
