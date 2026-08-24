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
        public long SummonerId { get; set; }
        public bool BotPlayer { get; set; }
    }

    internal sealed class LeagueHonorBallot
    {
        public long GameId { get; set; }
        public int Votes { get; set; }
        public bool HasVoteCount { get; set; }
        public List<LeagueHonorCandidate> Allies { get; private set; } = new List<LeagueHonorCandidate>();
        public List<string> HonoredPuuids { get; private set; } = new List<string>();
    }

    internal sealed class LeagueHonorAttemptStatus
    {
        public long GameId { get; set; }
        public string State { get; set; }
        public string Route { get; set; }
        public string Detail { get; set; }
        public int HttpStatus { get; set; }
        public int Attempts { get; set; }
        public long TargetSummonerId { get; set; }
        public string TargetPuuidSuffix { get; set; }
        public DateTime CompletedAtUtc { get; set; }

        public LeagueHonorAttemptStatus Clone()
        {
            return (LeagueHonorAttemptStatus)MemberwiseClone();
        }
    }

    internal sealed class LeagueHonorVerificationResult
    {
        public string State { get; set; }
        public string Detail { get; set; }
    }

    internal sealed class LeaguePostGameAutomationController : IDisposable
    {
        internal const string BallotPath = "/lol-honor-v2/v1/ballot";
        internal const string TeamChoicesPath = "/lol-honor-v2/v1/team-choices";
        internal const string VoteCompletionPath = "/lol-honor-v2/v1/vote-completion";
        internal const string CurrentSummonerPath = "/lol-summoner/v1/current-summoner";
        private static readonly TimeSpan BallotPollInterval = TimeSpan.FromMilliseconds(600);
        private static readonly TimeSpan BallotWaitLimit = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan[] VerificationDelays =
        {
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(2250)
        };

        private readonly object _sync = new object();
        private readonly ILeagueClientApi _read;
        private readonly ILeaguePostGameWriteApi _write;
        private readonly ILeaguePostGameClock _clock;
        private readonly Func<int, int> _chooseIndex;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private CancellationTokenSource _cycleCancellation;
        private LeagueHonorAttemptStatus _lastHonorStatus;
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

        public event Action<LeagueHonorAttemptStatus> HonorAttemptCompleted;

        public LeagueHonorAttemptStatus LastHonorStatus
        {
            get
            {
                lock (_sync) return _lastHonorStatus == null ? null : _lastHonorStatus.Clone();
            }
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
                if (ballot == null)
                {
                    PublishHonorStatus(Status(0, "skipped", "none", "ballot-timeout", 0, 0, null, false));
                }
                else if (ballot.GameId <= 0)
                {
                    PublishHonorStatus(Status(ballot.GameId, "skipped", "none", "invalid-game", 0, 0, null, false));
                }
                else if (ballot.Votes <= 0)
                {
                    PublishHonorStatus(Status(ballot.GameId, "skipped", "none", "no-votes", 0, 0, null, false));
                }
                else
                {
                    await TryHonorOneAllyAsync(ballot, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!playAgain) return;
            if (!honor)
            {
                await _clock.Delay(ResolveReturnDelay(initialPhase), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _clock.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
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
                .Where(item => !ballot.HonoredPuuids.Contains(item.Puuid, StringComparer.Ordinal))
                .GroupBy(item => item.Puuid, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (candidates.Count == 0)
            {
                PublishHonorStatus(Status(ballot.GameId, "skipped", "none", "no-eligible-ally", 0, 0, null, false));
                return;
            }

            var index = _chooseIndex(candidates.Count);
            if (index < 0 || index >= candidates.Count) index = 0;
            var selected = candidates[index];

            if (selected.SummonerId > 0)
            {
                var v2 = await TryHonorV2Async(ballot, selected, cancellationToken).ConfigureAwait(false);
                if (v2 != null)
                {
                    PublishHonorStatus(v2);
                    return;
                }
            }

            var legacy = await TryHonorLegacyAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
            PublishHonorStatus(legacy);
        }

        private async Task<LeagueHonorAttemptStatus> TryHonorV2Async(
            LeagueHonorBallot ballot,
            LeagueHonorCandidate selected,
            CancellationToken cancellationToken)
        {
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "summonerId", selected.SummonerId },
                { "puuid", selected.Puuid },
                { "honorType", "HEART" },
                { "gameId", ballot.GameId }
            });

            var attempts = 1;
            var response = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.HonorV2Path,
                body,
                cancellationToken).ConfigureAwait(false);

            if (response != null && (response.StatusCode == 404 || response.StatusCode == 405))
            {
                AppLog.Info("League auto honor V2 unavailable; falling back to legacy honor route. status=" + response.StatusCode);
                return null;
            }

            var verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
            if (string.Equals(verification.State, "confirmed", StringComparison.Ordinal))
                return Status(ballot.GameId, "success", "v2", verification.Detail, StatusCode(response), attempts, selected, true);

            var safeRetry = !IsSuccess(response) && string.Equals(verification.State, "not-applied", StringComparison.Ordinal);
            if (safeRetry)
            {
                await _clock.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                attempts++;
                response = await _write.TrySendAsync(
                    "POST",
                    LeaguePostGameWriteApiClient.HonorV2Path,
                    body,
                    cancellationToken).ConfigureAwait(false);
                verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
                if (string.Equals(verification.State, "confirmed", StringComparison.Ordinal))
                    return Status(ballot.GameId, "success", "v2", verification.Detail + ";safe-retry", StatusCode(response), attempts, selected, true);
            }

            if (IsSuccess(response))
                return Status(ballot.GameId, "unknown", "v2", "submitted-unverified:" + verification.Detail, StatusCode(response), attempts, selected, true);

            return Status(ballot.GameId, "failed", "v2", "submit-failed:" + verification.Detail, StatusCode(response), attempts, selected, true);
        }

        private async Task<LeagueHonorAttemptStatus> TryHonorLegacyAsync(
            LeagueHonorBallot ballot,
            LeagueHonorCandidate selected,
            CancellationToken cancellationToken)
        {
            var body = _json.Serialize(new Dictionary<string, object>
            {
                { "puuid", selected.Puuid },
                { "honorType", "HEART" }
            });
            var honorResponse = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.HonorPath,
                body,
                cancellationToken).ConfigureAwait(false);
            if (!IsSuccess(honorResponse))
                return Status(ballot.GameId, "failed", "legacy", "honor-submit-failed", StatusCode(honorResponse), 1, selected, true);

            var ballotResponse = await _write.TrySendAsync(
                "POST",
                LeaguePostGameWriteApiClient.HonorBallotSubmitPath,
                null,
                cancellationToken).ConfigureAwait(false);
            if (!IsSuccess(ballotResponse))
                return Status(ballot.GameId, "unknown", "legacy", "honor-sent-ballot-submit-failed", StatusCode(ballotResponse), 1, selected, true);

            var verification = await VerifyHonorAsync(ballot, selected, cancellationToken).ConfigureAwait(false);
            if (string.Equals(verification.State, "confirmed", StringComparison.Ordinal))
                return Status(ballot.GameId, "success", "legacy", verification.Detail, StatusCode(ballotResponse), 1, selected, true);

            return Status(ballot.GameId, "unknown", "legacy", "submitted-unverified:" + verification.Detail, StatusCode(ballotResponse), 1, selected, true);
        }

        private async Task<LeagueHonorVerificationResult> VerifyHonorAsync(
            LeagueHonorBallot before,
            LeagueHonorCandidate selected,
            CancellationToken cancellationToken)
        {
            var sameGameBallotSeen = false;
            var targetStillEligibleOnLastRead = false;
            var voteCountUnchangedOnLastRead = false;
            var completionSeen = false;
            var teamChoicesRead = false;
            var teamChoicesCount = 0;
            var teamChoicesShape = "none";

            foreach (var delay in VerificationDelays)
            {
                await _clock.Delay(delay, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var choicesBytes = await _read.TryGetBytesAsync(TeamChoicesPath, cancellationToken).ConfigureAwait(false);
                List<string> choices;
                string shape;
                if (TryParseTeamChoices(choicesBytes, out choices, out shape))
                {
                    teamChoicesRead = true;
                    teamChoicesCount = choices.Count;
                    teamChoicesShape = shape;
                    string matchKind;
                    if (MatchesTeamChoice(choices, selected, out matchKind))
                        return Verification("confirmed", "team-choices-confirmed:" + matchKind);
                }

                var ballotBytes = await _read.TryGetBytesAsync(BallotPath, cancellationToken).ConfigureAwait(false);
                var after = ParseBallot(ballotBytes);
                if (after != null && after.GameId == before.GameId)
                {
                    sameGameBallotSeen = true;
                    if (!string.IsNullOrWhiteSpace(selected.Puuid) &&
                        after.HonoredPuuids.Contains(selected.Puuid, StringComparer.Ordinal))
                        return Verification("confirmed", "ballot-honored-player-confirmed");

                    targetStillEligibleOnLastRead = ContainsCandidate(after, selected);
                    voteCountUnchangedOnLastRead = false;
                    if (before.HasVoteCount && after.HasVoteCount)
                    {
                        if (after.Votes < before.Votes)
                            return Verification("confirmed", "ballot-vote-decreased");
                        voteCountUnchangedOnLastRead = after.Votes == before.Votes;
                    }
                }

                var completionBytes = await _read.TryGetBytesAsync(VoteCompletionPath, cancellationToken).ConfigureAwait(false);
                long completionGameId;
                bool fullTeamVote;
                if (TryParseVoteCompletion(completionBytes, out completionGameId, out fullTeamVote) &&
                    (completionGameId == 0 || completionGameId == before.GameId))
                {
                    completionSeen = true;
                }
            }

            var diagnostic = teamChoicesRead
                ? ";team-choices-count=" + teamChoicesCount + ";team-choices-shape=" + teamChoicesShape
                : ";team-choices-unreadable";
            if (completionSeen) diagnostic += ";completion-readable";

            if (sameGameBallotSeen && targetStillEligibleOnLastRead && before.HasVoteCount && voteCountUnchangedOnLastRead)
                return Verification("not-applied", "same-game-ballot-unchanged" + diagnostic);

            return Verification("unknown", "no-authoritative-confirmation" + diagnostic);
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

            if (root.ContainsKey("numVotes"))
            {
                ballot.HasVoteCount = true;
                ballot.Votes = ReadInt(root, "numVotes");
            }
            else
            {
                var votePool = ReadDictionary(root, "votePool");
                if (votePool != null && votePool.ContainsKey("votes"))
                {
                    ballot.HasVoteCount = true;
                    ballot.Votes = ReadInt(votePool, "votes");
                }
            }

            var modernAllies = EnumerateDictionaries(ReadValue(root, "eligibleAllies")).ToList();
            var rows = modernAllies.Count > 0
                ? modernAllies
                : EnumerateDictionaries(ReadValue(root, "eligiblePlayers")).ToList();
            foreach (var row in rows)
            {
                ballot.Allies.Add(new LeagueHonorCandidate
                {
                    Puuid = ReadString(row, "puuid"),
                    SummonerId = ReadLongAny(row, "summonerId", "summonerID"),
                    BotPlayer = ReadBool(row, "botPlayer")
                });
            }

            foreach (var row in EnumerateDictionaries(ReadValue(root, "honoredPlayers")))
            {
                var puuid = ReadString(row, "puuid");
                if (!string.IsNullOrWhiteSpace(puuid) && !ballot.HonoredPuuids.Contains(puuid, StringComparer.Ordinal))
                    ballot.HonoredPuuids.Add(puuid);
            }

            if (!ballot.HasVoteCount && ballot.Allies.Count > 0)
                ballot.Votes = 1;
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

        private void PublishHonorStatus(LeagueHonorAttemptStatus status)
        {
            if (status == null) return;
            Action<LeagueHonorAttemptStatus> handler;
            lock (_sync)
            {
                _lastHonorStatus = status.Clone();
                handler = HonorAttemptCompleted;
            }

            AppLog.Info(
                "League auto honor result: gameId=" + status.GameId +
                "; state=" + (status.State ?? string.Empty) +
                "; route=" + (status.Route ?? string.Empty) +
                "; targetSummonerId=" + status.TargetSummonerId +
                "; targetPuuid=" + (status.TargetPuuidSuffix ?? string.Empty) +
                "; http=" + status.HttpStatus +
                "; attempts=" + status.Attempts +
                "; detail=" + (status.Detail ?? string.Empty));

            if (handler != null)
            {
                try { handler(status.Clone()); }
                catch (Exception exception) { AppLog.Info("League honor status observer skipped: " + exception.Message); }
            }
        }

        private static LeagueHonorAttemptStatus Status(
            long gameId,
            string state,
            string route,
            string detail,
            int httpStatus,
            int attempts,
            LeagueHonorCandidate selected,
            bool includeTarget)
        {
            return new LeagueHonorAttemptStatus
            {
                GameId = gameId,
                State = state,
                Route = route,
                Detail = detail,
                HttpStatus = httpStatus,
                Attempts = attempts,
                TargetSummonerId = includeTarget && selected != null ? selected.SummonerId : 0,
                TargetPuuidSuffix = includeTarget && selected != null ? MaskPuuid(selected.Puuid) : string.Empty,
                CompletedAtUtc = DateTime.UtcNow
            };
        }

        private static LeagueHonorVerificationResult Verification(string state, string detail)
        {
            return new LeagueHonorVerificationResult { State = state, Detail = detail };
        }

        private static int StatusCode(LeagueClientWriteResponse response)
        {
            return response == null ? 0 : response.StatusCode;
        }

        private static bool IsSuccess(LeagueClientWriteResponse response)
        {
            return response != null && response.IsSuccessStatusCode;
        }

        private static bool ContainsCandidate(LeagueHonorBallot ballot, LeagueHonorCandidate selected)
        {
            if (ballot == null || selected == null) return false;
            return ballot.Allies.Any(candidate => candidate != null &&
                ((selected.SummonerId > 0 && candidate.SummonerId == selected.SummonerId) ||
                 (!string.IsNullOrWhiteSpace(selected.Puuid) && string.Equals(candidate.Puuid, selected.Puuid, StringComparison.Ordinal))));
        }

        private bool TryParseTeamChoices(byte[] bytes, out List<string> values, out string shape)
        {
            values = new List<string>();
            shape = "none";
            if (bytes == null || bytes.Length == 0) return false;
            try
            {
                var parsed = _json.DeserializeObject(Encoding.UTF8.GetString(bytes));
                var enumerable = parsed as IEnumerable;
                if (enumerable == null || parsed is string) return false;
                var numeric = 0;
                var text = 0;
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    var value = Convert.ToString(item, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    values.Add(value);
                    long number;
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) numeric++;
                    else text++;
                }
                if (values.Count == 0) shape = "empty";
                else if (numeric == values.Count) shape = "numeric";
                else if (text == values.Count) shape = "text";
                else shape = "mixed";
                return true;
            }
            catch { return false; }
        }

        private static bool MatchesTeamChoice(List<string> choices, LeagueHonorCandidate selected, out string matchKind)
        {
            matchKind = null;
            if (choices == null || selected == null) return false;
            if (!string.IsNullOrWhiteSpace(selected.Puuid) && choices.Contains(selected.Puuid, StringComparer.Ordinal))
            {
                matchKind = "puuid";
                return true;
            }
            if (selected.SummonerId > 0)
            {
                var summonerId = selected.SummonerId.ToString(CultureInfo.InvariantCulture);
                if (choices.Contains(summonerId, StringComparer.Ordinal))
                {
                    matchKind = "summoner-id";
                    return true;
                }
            }
            return false;
        }

        private bool TryParseVoteCompletion(byte[] bytes, out long gameId, out bool fullTeamVote)
        {
            gameId = 0;
            fullTeamVote = false;
            var root = ParseObject(bytes);
            if (root == null) return false;
            gameId = ReadLongAny(root, "gameId", "game_id");
            fullTeamVote = ReadBoolAny(root, "fullTeamVote", "full_team_vote");
            return true;
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

        private static long ReadLongAny(Dictionary<string, object> source, params string[] keys)
        {
            if (source == null || keys == null) return 0;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key) || !source.ContainsKey(key)) continue;
                try { return Convert.ToInt64(ReadValue(source, key)); }
                catch { }
            }
            return 0;
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

        private static bool ReadBoolAny(Dictionary<string, object> source, params string[] keys)
        {
            if (source == null || keys == null) return false;
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key) || !source.ContainsKey(key)) continue;
                try { return Convert.ToBoolean(ReadValue(source, key)); }
                catch { }
            }
            return false;
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

        private static string MaskPuuid(string puuid)
        {
            var value = (puuid ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            return value.Length <= 8 ? "..." + value : "..." + value.Substring(value.Length - 8);
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
                HonorAttemptCompleted = null;
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
