using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FACM.AppHost;
using FACM.League;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class LeagueClientModule : IFacmModule, ILeagueClientApi, ILeagueRiotAliasLookupApi, ILeagueSgpMatchHistoryApi, ILeagueSgpTaggedHistoryApi, ILeagueSgpPlayerChallengesApi, ILeagueSgpGsmApi, ILeagueSgpStatsEogApi, ILeagueMasteryReadApi, ILeagueClientWriteApi, ILeagueGameflowReconnectWriteApi, ILeagueChampSelectActionWriteApi, ILeagueScopedChampSelectActionWriteApi, ILeaguePostGameWriteApi, ILeagueScopedPostGameWriteApi, ILeagueReplayWriteApi, ILeagueMatchmakingWriteApi, ILeagueScopedMatchmakingWriteApi, ILeagueLobbyRoomWriteApi, ILeagueLobbyInviteWriteApi, ILeagueLobbyNoticeWriteApi, ILeagueChampSelectSideWriteApi, ILeaguePrivateChatWriteApi, ILeagueQuickMessageWriteApi, ILeagueTaggedPlayerNoticeWriteApi, ILeagueBenchSwapWriteApi, ILeagueScopedBenchSwapWriteApi, ILeagueAramRerollWriteApi, ILeagueChampionTradeWriteApi, ILeagueDraftSwapWriteApi, ILeagueChampSelectQuitWriteApi, ILeaguePresenceWriteApi, ILeagueScopedPresenceWriteApi, ILeagueProfileWriteApi, ILeagueRegaliaWriteApi, ILeagueChallengePreferencesWriteApi, ILeagueEmoteLoadoutWriteApi, ILeagueClientUxRepairWriteApi, ILeagueChampSelectMuteWriteApi
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();
        private readonly ILeagueClientSessionDiscovery _discovery;
        private LeagueClientSessionProvider _sessions;
        private LeagueClientApiClient _api;
        private LeagueRiotAliasLookupApiClient _riotAlias;
        private LeagueSgpMatchHistoryApiClient _sgpHistory;
        private LeagueClientWriteApiClient _writer;
        private LeaguePostGameWriteApiClient _postGameWriter;
        private LeagueReplayWriteApiClient _replayWriter;
        private LeagueMatchmakingWriteApiClient _matchmakingWriter;
        private LeagueChampSelectActionWriteApiClient _champSelectActionWriter;
        private LeagueBenchSwapWriteApiClient _benchSwapWriter;
        private LeagueChampSelectQuitWriteApiClient _champSelectQuitWriter;
        private LeagueChampSelectMuteWriteApiClient _champSelectMuteWriter;
        private LeagueInsightService _insights;
        private LeaguePresenceWriteApiClient _presenceWriter;
        private LeagueProfileWriteApiClient _profileWriter;
        private LeagueRegaliaWriteApiClient _regaliaWriter;
        private LeagueChallengePreferencesWriteApiClient _challengePreferencesWriter;
        private LeagueEmoteLoadoutWriteApiClient _emoteLoadoutWriter;
        private LeagueClientUxRepairWriteApiClient _uxRepairWriter;

        public LeagueClientModule() : this(new ResilientLeagueClientSessionDiscovery()) { }

        internal LeagueClientModule(ILeagueClientSessionDiscovery discovery)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        }

        public const string ModuleId = "league-client";
        public string Id { get { return ModuleId; } }
        public IReadOnlyList<string> Dependencies { get { return NoDependencies; } }

        public void Initialize()
        {
            if (_api != null) return;
            _sessions = new LeagueClientSessionProvider(_discovery);
            _api = new LeagueClientApiClient(_sessions);
            _riotAlias = new LeagueRiotAliasLookupApiClient(_sessions);
            _sgpHistory = new LeagueSgpMatchHistoryApiClient(_sessions, _api);
            _writer = new LeagueClientWriteApiClient(_sessions);
            _postGameWriter = new LeaguePostGameWriteApiClient(_sessions);
            _replayWriter = new LeagueReplayWriteApiClient(_sessions);
            _matchmakingWriter = new LeagueMatchmakingWriteApiClient(_sessions);
            _champSelectActionWriter = new LeagueChampSelectActionWriteApiClient(_sessions);
            _benchSwapWriter = new LeagueBenchSwapWriteApiClient(_sessions);
            _champSelectQuitWriter = new LeagueChampSelectQuitWriteApiClient(_sessions);
            _champSelectMuteWriter = new LeagueChampSelectMuteWriteApiClient(_sessions);
            _insights = new LeagueInsightService(_api);
            _presenceWriter = new LeaguePresenceWriteApiClient(_sessions);
            _profileWriter = new LeagueProfileWriteApiClient(_sessions);
            _regaliaWriter = new LeagueRegaliaWriteApiClient(_sessions);
            _challengePreferencesWriter = new LeagueChallengePreferencesWriteApiClient(_sessions);
            _emoteLoadoutWriter = new LeagueEmoteLoadoutWriteApiClient(_sessions);
            _uxRepairWriter = new LeagueClientUxRepairWriteApiClient(_sessions);
            AppLog.Info("LeagueClient module initialized; local LCU session discovery is on-demand.");
        }

        internal async Task<LeagueInsightSnapshot> ReadInsightsAsync(string phase, CancellationToken token)
        {
            var owner = _insights;
            if (!LeagueInsightService.IsApplicablePhase(phase)) return new LeagueInsightSnapshot();
            var session = CurrentSession;
            if (owner == null || session == null) return null;
            var before = await ReadInsightAccountAsync(token).ConfigureAwait(false);
            if (!LeagueDynamicMessageService.SafePuuid(before) || !LeagueEncounterReplayService.SessionMatches(session, CurrentSession)) return null;
            var snapshot = await owner.ReadAsync(phase, token).ConfigureAwait(false);
            var after = await ReadInsightAccountAsync(token).ConfigureAwait(false);
            return ReferenceEquals(owner, _insights) && string.Equals(before, after, StringComparison.Ordinal) &&
                LeagueEncounterReplayService.SessionMatches(session, CurrentSession) ? snapshot : null;
        }

        private async Task<string> ReadInsightAccountAsync(CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var bytes = await TryGetBytesAsync("/lol-summoner/v1/current-summoner", deadline.Token).ConfigureAwait(false);
                    if (bytes == null || bytes.Length == 0) return null;
                    var root = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(
                        System.Text.Encoding.UTF8.GetString(bytes)) as Dictionary<string, object>;
                    object value;
                    return root != null && root.TryGetValue("puuid", out value) ? value as string : null;
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return null; }
                catch (ArgumentException) { return null; }
                catch (InvalidOperationException) { return null; }
            }
        }

        public Task<byte[]> TryGetBytesAsync(string path, CancellationToken cancellationToken)
        {
            var api = _api;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetBytesAsync(path, cancellationToken);
        }

        Task<byte[]> ILeagueRiotAliasLookupApi.TryLookupAsync(
            string gameName, string tagLine, CancellationToken cancellationToken)
        {
            var api = _riotAlias;
            return api == null ? Task.FromResult<byte[]>(null) :
                api.TryLookupAsync(gameName, tagLine, cancellationToken);
        }

        Task<byte[]> ILeagueMasteryReadApi.TryGetMasteryTopAsync(LeagueClientSession expected,
            string targetPuuid, CancellationToken token)
        {
            var api = _api;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetMasteryTopAsync(expected, targetPuuid, token);
        }

        Task<byte[]> ILeagueSgpPlayerChallengesApi.TryGetChallengesAsync(string targetPuuid,
            CancellationToken token, string serverId)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetChallengesAsync(targetPuuid, token, serverId);
        }

        Task<byte[]> ILeagueSgpMatchHistoryApi.TryGetSummaryAsync(
            string targetPuuid, int startIndex, int count, CancellationToken cancellationToken, string serverId)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) :
                api.TryGetSummaryAsync(targetPuuid, startIndex, count, cancellationToken, serverId);
        }

        Task<byte[]> ILeagueSgpTaggedHistoryApi.TryGetTaggedSummaryAsync(
            string targetPuuid, int startIndex, int count, CancellationToken token, string serverId, string tag)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) :
                api.TryGetTaggedSummaryAsync(targetPuuid, startIndex, count, token, serverId, tag);
        }

        Task<byte[]> ILeagueSgpMatchHistoryApi.TryGetGameSummaryAsync(long gameId, CancellationToken token, string serverId)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetGameSummaryAsync(gameId, token, serverId);
        }

        Task<byte[]> ILeagueSgpMatchHistoryApi.TryGetGameDetailsAsync(long gameId, CancellationToken token, string serverId)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetGameDetailsAsync(gameId, token, serverId);
        }

        Task<byte[]> ILeagueSgpMatchHistoryApi.TryGetSummonerAsync(string puuid, CancellationToken token, string serverId)
        {
            var api = _sgpHistory;
            return api == null ? Task.FromResult<byte[]>(null) : api.TryGetSummonerAsync(puuid, token, serverId);
        }

        Task<byte[]> ILeagueSgpGsmApi.TryGetGsmByPuuidAsync(string puuid, CancellationToken token)
        {
            return _sgpHistory == null ? Task.FromResult<byte[]>(null) :
                _sgpHistory.TryGetGsmByPuuidAsync(puuid, token);
        }

        Task<byte[]> ILeagueSgpGsmApi.TryGetGsmByGameIdAsync(long gameId, CancellationToken token)
        {
            return _sgpHistory == null ? Task.FromResult<byte[]>(null) :
                _sgpHistory.TryGetGsmByGameIdAsync(gameId, token);
        }

        Task<byte[]> ILeagueSgpStatsEogApi.TryGetStatsEogAsync(long gameId, string puuid, CancellationToken token)
        {
            return _sgpHistory == null ? Task.FromResult<byte[]>(null) :
                _sgpHistory.TryGetStatsEogAsync(gameId, puuid, token);
        }

        public Task<LeagueClientWriteResponse> TrySendJsonAsync(string method, string path, string json, CancellationToken cancellationToken)
        {
            var writer = _writer;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) : writer.TrySendJsonAsync(method, path, json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueGameflowReconnectWriteApi.TryReconnectAsync(CancellationToken cancellationToken)
        {
            var writer = _writer;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySendJsonAsync("POST", LeagueClientWriteApiClient.ReconnectPath, null, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeaguePostGameWriteApi.TrySendAsync(string method, string path, string json, CancellationToken cancellationToken)
        {
            var writer = _postGameWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) : writer.TrySendAsync(method, path, json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedPostGameWriteApi.TrySendAsync(
            string method, string path, string json, LeagueClientSession expected, CancellationToken cancellationToken)
        {
            var writer = _postGameWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendAsync(method, path, json, expected, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueReplayWriteApi.TryCreateMetadataAsync(
            LeagueClientSession session, LeaguePlayerMatchSummary match, string gameVersion, CancellationToken token)
        {
            return _replayWriter == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                _replayWriter.TryCreateMetadataAsync(session, match, gameVersion, token);
        }

        Task<LeagueClientWriteResponse> ILeagueReplayWriteApi.TryReplayAsync(
            LeagueClientSession session, long gameId, bool watch, CancellationToken token)
        {
            return _replayWriter == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                _replayWriter.TryReplayAsync(session, gameId, watch, token);
        }

        Task<LeagueClientWriteResponse> ILeagueMatchmakingWriteApi.TrySendAsync(string method, string path, CancellationToken cancellationToken)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) : writer.TrySendAsync(method, path, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedMatchmakingWriteApi.TrySendScopedAsync(string method,
            string path, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendScopedAsync(method, path, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedMatchmakingWriteApi.TrySendScopedNoticeAsync(string conversationId,
            string message, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendScopedNoticeAsync(conversationId, message, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueLobbyRoomWriteApi.TryApplyRoomAsync(LeagueLobbyRoomAction action,
            long value, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TryApplyRoomAsync(action, value, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueLobbyInviteWriteApi.TryInviteAsync(long summonerId, CancellationToken cancellationToken)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) : writer.TryInviteAsync(summonerId, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueLobbyNoticeWriteApi.TrySendNoticeAsync(
            string conversationId, string message, CancellationToken cancellationToken)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendNoticeAsync(conversationId, message, cancellationToken);
        }

        internal LeagueClientSession CurrentSession { get { return _sessions?.GetSession(); } }

        Task<LeagueClientWriteResponse> ILeaguePrivateChatWriteApi.TryReplyAsync(LeagueClientSession session,
            string conversationId, string text, CancellationToken token)
        {
            return _matchmakingWriter == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                _matchmakingWriter.TryReplyAsync(session, conversationId, text, token);
        }

        Task<LeagueClientWriteResponse> ILeagueQuickMessageWriteApi.TrySendQuickMessageAsync(
            LeagueClientSession session, string conversationId, string message, CancellationToken token)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendQuickMessageAsync(session, conversationId, message, token);
        }

        Task<LeagueClientWriteResponse> ILeagueTaggedPlayerNoticeWriteApi.TrySendTaggedNoticeAsync(
            LeagueClientSession session, string conversationId, string message, CancellationToken token)
        {
            return _matchmakingWriter == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                _matchmakingWriter.TrySendTaggedNoticeAsync(session, conversationId, message, token);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectSideWriteApi.TrySendSideAsync(
            string conversationId, string message, bool visibleToTeam, CancellationToken cancellationToken)
        {
            var writer = _matchmakingWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySendSideAsync(conversationId, message, visibleToTeam, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectActionWriteApi.TryApplyAsync(
            string actionType, int actionId, int championId, bool completed, CancellationToken cancellationToken)
        {
            var writer = _champSelectActionWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryApplyAsync(actionType, actionId, championId, completed, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectActionWriteApi.TryIntentAsync(
            int actionId, int championId, CancellationToken cancellationToken)
        {
            var writer = _champSelectActionWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryIntentAsync(actionId, championId, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedChampSelectActionWriteApi.TryApplyAsync(
            string actionType, int actionId, int championId, bool completed, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _champSelectActionWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TryApplyAsync(actionType, actionId, championId, completed, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedChampSelectActionWriteApi.TryIntentAsync(
            int actionId, int championId, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _champSelectActionWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TryIntentAsync(actionId, championId, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueBenchSwapWriteApi.TrySwapAsync(int championId, LeagueBenchSwapRoute route, CancellationToken cancellationToken)
        {
            var writer = _benchSwapWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySwapAsync(championId, route, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedBenchSwapWriteApi.TrySwapAsync(int championId,
            LeagueBenchSwapRoute route, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _benchSwapWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySwapAsync(championId, route, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueDraftSwapWriteApi.TryHandleSwapAsync(LeagueDraftSwapKind kind,
            int id, LeagueDraftSwapAction action, LeagueClientSession expected, CancellationToken token)
        {
            var writer = _benchSwapWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TryHandleSwapAsync(kind, id, action, expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueAramRerollWriteApi.TryRerollAsync(LeagueClientSession expected, CancellationToken token)
        {
            var writer = _benchSwapWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) : writer.TryRerollAsync(expected, token);
        }

        Task<LeagueClientWriteResponse> ILeagueChampionTradeWriteApi.TryAnswerAsync(
            int tradeId, bool accept, CancellationToken cancellationToken)
        {
            var writer = _benchSwapWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryAnswerAsync(tradeId, accept, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectQuitWriteApi.TryQuitAsync(CancellationToken cancellationToken)
        {
            var writer = _champSelectQuitWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryQuitAsync(cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectMuteWriteApi.TryToggleAsync(
            string identityJson, LeagueClientSession expected, CancellationToken cancellationToken)
        {
            var writer = _champSelectMuteWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryToggleAsync(identityJson, expected, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChampSelectQuitWriteApi.TryRequestLobbyAsync(CancellationToken cancellationToken)
        {
            var writer = _champSelectQuitWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryRequestLobbyAsync(cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeaguePresenceWriteApi.TrySetPresenceAsync(string json, CancellationToken cancellationToken)
        {
            var writer = _presenceWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySetPresenceAsync(json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueScopedPresenceWriteApi.TrySetPresenceAsync(
            string json, LeagueClientSession expected, CancellationToken cancellationToken)
        {
            var writer = _presenceWriter;
            return writer == null ? Task.FromResult<LeagueClientWriteResponse>(null) :
                writer.TrySetPresenceAsync(json, expected, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueProfileWriteApi.TrySetSummonerProfileAsync(string json, CancellationToken cancellationToken)
        {
            var writer = _profileWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySetSummonerProfileAsync(json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueRegaliaWriteApi.TrySetRegaliaAsync(string json, CancellationToken cancellationToken)
        {
            var writer = _regaliaWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TrySetRegaliaAsync(json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueChallengePreferencesWriteApi.TryUpdatePlayerPreferencesAsync(
            string json,
            CancellationToken cancellationToken)
        {
            var writer = _challengePreferencesWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryUpdatePlayerPreferencesAsync(json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueEmoteLoadoutWriteApi.TryPatchEmotesAsync(
            string loadoutId,
            string json,
            CancellationToken cancellationToken)
        {
            var writer = _emoteLoadoutWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryPatchEmotesAsync(loadoutId, json, cancellationToken);
        }

        Task<LeagueClientWriteResponse> ILeagueClientUxRepairWriteApi.TryRestartUxAsync(CancellationToken cancellationToken)
        {
            var writer = _uxRepairWriter;
            return writer == null
                ? Task.FromResult<LeagueClientWriteResponse>(null)
                : writer.TryRestartUxAsync(cancellationToken);
        }

        public void Dispose()
        {
            var uxRepairWriter = _uxRepairWriter;
            var emoteLoadoutWriter = _emoteLoadoutWriter;
            var challengePreferencesWriter = _challengePreferencesWriter;
            var regaliaWriter = _regaliaWriter;
            var profileWriter = _profileWriter;
            var presenceWriter = _presenceWriter;
            var benchSwapWriter = _benchSwapWriter;
            var champSelectQuitWriter = _champSelectQuitWriter;
            var champSelectMuteWriter = _champSelectMuteWriter;
            var matchmakingWriter = _matchmakingWriter;
            var champSelectActionWriter = _champSelectActionWriter;
            var postGameWriter = _postGameWriter;
            var replayWriter = _replayWriter;
            var writer = _writer;
            var api = _api;
            var riotAlias = _riotAlias;
            var sgpHistory = _sgpHistory;
            _uxRepairWriter = null;
            _emoteLoadoutWriter = null;
            _challengePreferencesWriter = null;
            _regaliaWriter = null;
            _profileWriter = null;
            _presenceWriter = null;
            _benchSwapWriter = null;
            _champSelectQuitWriter = null;
            _champSelectMuteWriter = null;
            _insights = null;
            _matchmakingWriter = null;
            _champSelectActionWriter = null;
            _postGameWriter = null;
            _replayWriter = null;
            _writer = null;
            _api = null;
            _riotAlias = null;
            _sgpHistory = null;
            _sessions = null;
            if (uxRepairWriter != null) uxRepairWriter.Dispose();
            if (emoteLoadoutWriter != null) emoteLoadoutWriter.Dispose();
            if (challengePreferencesWriter != null) challengePreferencesWriter.Dispose();
            if (regaliaWriter != null) regaliaWriter.Dispose();
            if (profileWriter != null) profileWriter.Dispose();
            if (presenceWriter != null) presenceWriter.Dispose();
            if (benchSwapWriter != null) benchSwapWriter.Dispose();
            if (champSelectQuitWriter != null) champSelectQuitWriter.Dispose();
            if (champSelectMuteWriter != null) champSelectMuteWriter.Dispose();
            if (matchmakingWriter != null) matchmakingWriter.Dispose();
            if (champSelectActionWriter != null) champSelectActionWriter.Dispose();
            if (postGameWriter != null) postGameWriter.Dispose();
            if (replayWriter != null) replayWriter.Dispose();
            if (writer != null) writer.Dispose();
            if (api != null) api.Dispose();
            if (riotAlias != null) riotAlias.Dispose();
            if (sgpHistory != null) sgpHistory.Dispose();
        }
    }
}
