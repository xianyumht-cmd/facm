using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FACM.League
{
    internal enum LeagueEncounterReplayStatus
    {
        DownloadRequested,
        WatchRequested,
        Checking,
        Downloading,
        Incompatible,
        Unavailable,
        NoClient,
        WrongAccount
    }

    internal sealed class LeagueEncounterReplayResult
    {
        internal LeagueEncounterReplayStatus Status;
        internal int Progress;
    }

    // One explicit scoreboard action. Do not poll LCU replay events from a background service.
    internal sealed class LeagueEncounterReplayService
    {
        internal const string ConfigurationPath = "/lol-replays/v1/configuration";
        private readonly ILeagueClientApi _read;
        private readonly ILeagueReplayWriteApi _write;
        private readonly Func<LeagueClientSession> _session;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 };

        internal LeagueEncounterReplayService(ILeagueClientApi read, ILeagueReplayWriteApi write,
            Func<LeagueClientSession> session)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        // Player history is transient, not necessarily part of the saved encounter index.
        // Preserve its original session and copy only the metadata required for the write.
        internal Task<LeagueEncounterReplayResult> ExecuteForHistoryAsync(
            LeagueClientSession expected, string selfPuuid, string targetPuuid,
            LeaguePlayerMatchSummary selected, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = _session();
            if (!SessionMatches(expected, current))
                return Task.FromResult(Result(LeagueEncounterReplayStatus.NoClient));
            if (!CanOfferHistoryReplay(expected, current, selfPuuid, selected) ||
                !LeagueDynamicMessageService.SafePuuid(targetPuuid))
                return Task.FromResult(Result(LeagueEncounterReplayStatus.Unavailable));
            var match = new LeaguePlayerMatchSummary
            {
                GameId = selected.GameId,
                GameType = selected.GameType,
                QueueId = selected.QueueId,
                GameDurationSeconds = selected.GameDurationSeconds,
                GameCreationEpochMs = selected.GameCreationEpochMs
            };
            var record = new LeagueEncounterRecord
            {
                GameId = match.GameId, SelfPuuid = selfPuuid, Puuid = targetPuuid,
                Region = expected.Region, PlatformId = expected.PlatformId
            };
            return ExecuteCoreAsync(record, match, expected, token);
        }

        internal Task<LeagueEncounterReplayResult> ExecuteAsync(
            LeagueEncounterRecord record, LeaguePlayerMatchSummary match, CancellationToken token)
        {
            return ExecuteCoreAsync(record, match, null, token);
        }

        private async Task<LeagueEncounterReplayResult> ExecuteCoreAsync(
            LeagueEncounterRecord record, LeaguePlayerMatchSummary match,
            LeagueClientSession expected, CancellationToken token)
        {
            var initial = _session();
            if (initial == null) return Result(LeagueEncounterReplayStatus.NoClient);
            if (expected != null && !SessionMatches(expected, initial))
                return Result(LeagueEncounterReplayStatus.NoClient);
            if (record == null || match == null || record.GameId <= 0 || match.GameId != record.GameId ||
                !LeagueDynamicMessageService.SafePuuid(record.SelfPuuid) ||
                !LeagueDynamicMessageService.SafePuuid(record.Puuid))
                return Result(LeagueEncounterReplayStatus.Unavailable);
            if (!Same(initial.Region, record.Region) || !Same(initial.PlatformId, record.PlatformId))
                return Result(LeagueEncounterReplayStatus.WrongAccount);

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var configuration = Parse(await _read.TryGetBytesAsync(ConfigurationPath, deadline.Token)
                        .ConfigureAwait(false));
                    if (!IsCurrent(initial)) return Result(LeagueEncounterReplayStatus.NoClient);
                    if (configuration == null || !Boolean(configuration, "isReplaysEnabled") ||
                        !Boolean(configuration, "isReplaysForMatchHistoryEnabled"))
                        return Result(LeagueEncounterReplayStatus.Unavailable);
                    var version = String(configuration, "gameVersion");
                    if (string.IsNullOrWhiteSpace(version)) return Result(LeagueEncounterReplayStatus.Unavailable);

                    // The source creates metadata before inspecting download/watch availability.
                    if (match.QueueId <= 0 || match.GameDurationSeconds <= 0 ||
                        match.GameCreationEpochMs <= 0 || string.IsNullOrWhiteSpace(match.GameType))
                        return Result(LeagueEncounterReplayStatus.Unavailable);
                    var owner = await VerifyOwnerAsync(initial, record, deadline.Token).ConfigureAwait(false);
                    if (owner != LeagueEncounterReplayStatus.Checking) return Result(owner);
                    var created = await _write.TryCreateMetadataAsync(initial, match, version, deadline.Token)
                        .ConfigureAwait(false);
                    if (!IsCurrent(initial)) return Result(LeagueEncounterReplayStatus.NoClient);
                    if (created == null || !created.IsSuccessStatusCode)
                        return Result(LeagueEncounterReplayStatus.Unavailable);

                    var metadata = Parse(await _read.TryGetBytesAsync(
                        "/lol-replays/v1/metadata/" + match.GameId, deadline.Token).ConfigureAwait(false));
                    if (!IsCurrent(initial)) return Result(LeagueEncounterReplayStatus.NoClient);
                    if (metadata == null) return Result(LeagueEncounterReplayStatus.Unavailable);
                    object returnedId;
                    long metadataId;
                    if (metadata.TryGetValue("gameId", out returnedId) &&
                        (!long.TryParse(Convert.ToString(returnedId,
                            System.Globalization.CultureInfo.InvariantCulture), out metadataId) ||
                         metadataId != match.GameId))
                        return Result(LeagueEncounterReplayStatus.Unavailable);
                    var state = String(metadata, "state");
                    if (string.Equals(state, "checking", StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterReplayStatus.Checking);
                    if (string.Equals(state, "downloading", StringComparison.OrdinalIgnoreCase))
                        return new LeagueEncounterReplayResult
                        {
                            Status = LeagueEncounterReplayStatus.Downloading,
                            Progress = DownloadPercent(metadata)
                        };
                    if (string.Equals(state, "incompatible", StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterReplayStatus.Incompatible);
                    if (!string.Equals(state, "download", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(state, "watch", StringComparison.OrdinalIgnoreCase))
                        return Result(LeagueEncounterReplayStatus.Unavailable);
                    var canWatch = string.Equals(state, "watch", StringComparison.OrdinalIgnoreCase);
                    owner = await VerifyOwnerAsync(initial, record, deadline.Token).ConfigureAwait(false);
                    if (owner != LeagueEncounterReplayStatus.Checking) return Result(owner);
                    var action = await _write.TryReplayAsync(initial, match.GameId, canWatch, deadline.Token)
                        .ConfigureAwait(false);
                    if (!IsCurrent(initial)) return Result(LeagueEncounterReplayStatus.NoClient);
                    return Result(action != null && action.IsSuccessStatusCode
                        ? canWatch ? LeagueEncounterReplayStatus.WatchRequested : LeagueEncounterReplayStatus.DownloadRequested
                        : LeagueEncounterReplayStatus.Unavailable);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    return Result(LeagueEncounterReplayStatus.Unavailable);
                }
                catch (Exception)
                {
                    return Result(LeagueEncounterReplayStatus.Unavailable);
                }
            }
        }

        private async Task<LeagueEncounterReplayStatus> VerifyOwnerAsync(LeagueClientSession initial,
            LeagueEncounterRecord record, CancellationToken token)
        {
            var profile = Parse(await _read.TryGetBytesAsync(
                LeagueDashboardDetailsService.SummonerPath, token).ConfigureAwait(false));
            if (!IsCurrent(initial)) return LeagueEncounterReplayStatus.NoClient;
            if (profile == null || string.IsNullOrWhiteSpace(String(profile, "puuid")))
                return LeagueEncounterReplayStatus.Unavailable;
            return Same(String(profile, "puuid"), record.SelfPuuid)
                ? LeagueEncounterReplayStatus.Checking : LeagueEncounterReplayStatus.WrongAccount;
        }

        private bool IsCurrent(LeagueClientSession initial)
        {
            return SessionMatches(initial, _session());
        }

        internal static bool SessionMatches(LeagueClientSession expected, LeagueClientSession current)
        {
            return expected != null && expected.Matches(current) &&
                Same(expected.Region, current.Region) && Same(expected.PlatformId, current.PlatformId);
        }

        internal static bool CanOfferHistoryReplay(LeagueClientSession expected,
            LeagueClientSession current, string selfPuuid, LeaguePlayerMatchSummary match)
        {
            return SessionMatches(expected, current) &&
                LeagueDynamicMessageService.SafePuuid(selfPuuid) &&
                match != null && match.GameId > 0 &&
                (match.DataSource != LeaguePlayerHistorySource.Sgp ||
                    (!string.IsNullOrEmpty(match.SgpServerId) && string.Equals(match.SgpServerId,
                        LeagueSgpMatchHistoryApiClient.CurrentServerId(expected), StringComparison.OrdinalIgnoreCase)));
        }

        private Dictionary<string, object> Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 128 * 1024) return null;
            return _json.DeserializeObject(Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
        }

        private static string String(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value as string : null;
        }

        private static bool Boolean(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static int DownloadPercent(Dictionary<string, object> source)
        {
            object value;
            double result;
            if (source == null || !source.TryGetValue("downloadProgress", out value) ||
                !double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out result) || double.IsNaN(result) || double.IsInfinity(result)) return 0;
            if (result > 0 && result <= 1) result *= 100;
            return (int)Math.Round(Math.Max(0, Math.Min(100, result)));
        }

        private static bool Same(string first, string second)
        {
            return string.Equals((first ?? string.Empty).Trim(), (second ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static LeagueEncounterReplayResult Result(LeagueEncounterReplayStatus status)
        {
            return new LeagueEncounterReplayResult { Status = status };
        }
    }
}
