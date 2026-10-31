using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal static class LeagueSgpSmokeTest
    {
        internal static void Validate()
        {
            var current = Session("HN10", "TENCENT");
            var local = new TokenApi();
            var handler = new CaptureHandler();
            using (var api = new LeagueSgpMatchHistoryApiClient(() => current, local, handler))
            {
                Require(LeagueSgpMatchHistoryApiClient.AvailableServers(current).Length == 10,
                    "Tencent discovery must include the exact pinned server catalog.");
                var aliases = new Dictionary<string, string> {
                    { "NA", "NA1" }, { "BR", "BR1" }, { "TR", "TR1" },
                    { "LAN", "LA1" }, { "LAS", "LA2" }, { "OCE", "OC1" },
                    { "EUW1", "EUW" }, { "JP1", "JP" } };
                foreach (var alias in aliases)
                {
                    var regionalSession = Session("unused-platform", alias.Key);
                    Require(LeagueSgpMatchHistoryApiClient.CurrentServerId(regionalSession) == alias.Value &&
                        LeagueSgpMatchHistoryApiClient.ResolveServer(regionalSession)?.Id == alias.Value,
                        "Pinned SGP region alias mapping failed for " + alias.Key);
                }
                Require(LeagueSgpMatchHistoryApiClient.ResolveServer(Session("unused-platform", "UNKNOWN")) == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("unused-platform", "NA"), "EUW") == null,
                    "Unknown and unrelated region selectors must remain unavailable.");
                var euw = LeagueSgpMatchHistoryApiClient.ResolveServer(Session("EUW1", "EUW"));
                Require(euw != null && euw.PathRegion == "EUW1" &&
                    euw.MatchHistory == "https://euc1-red.pp.sgp.pvp.net" &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(current, "NA1") == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("NA1", "NA1"), "EUW") == null,
                    "International path overrides and Tencent-only cross-region capability must remain explicit.");
                api.TryGetGameSummaryAsync(601, CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Url == "https://hn1-k8s-sgp.lol.qq.com:21019/" +
                    "match-history-query/v1/products/lol/HN1_601/SUMMARY" &&
                    handler.Authorization == "Bearer history-token" && local.Paths[0] == LeagueSgpMatchHistoryApiClient.EntitlementsPath,
                    "A selected regional game summary must use that server's path and Entitlements token.");
                api.TryGetGameDetailsAsync(601, CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/HN10_601/DETAILS", StringComparison.Ordinal),
                    "SGP timeline details must use DETAILS rather than the participant summary payload.");
                api.TryGetSummonerAsync("target-puuid", CancellationToken.None, "TENCENT_TJ100")
                    .GetAwaiter().GetResult();
                Require(handler.Url == "https://tj100-sgp.lol.qq.com:21019/summoner-ledge/v1/regions/TJ100/summoners/puuids" &&
                    handler.Method == HttpMethod.Post && handler.Body == "[\"target-puuid\"]" &&
                    handler.Authorization == "Bearer profile-token" &&
                    local.Paths[local.Paths.Count - 1] == LeagueSgpMatchHistoryApiClient.LeagueSessionTokenPath,
                    "Summoner profiles require the League session token, exact region and bounded JSON POST body.");
                api.TryGetRankedStatsAsync("target-puuid", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url == "https://hn10-k8s-sgp.lol.qq.com:21019/leagues-ledge/v2/rankedStats/puuid/target-puuid" &&
                    handler.Method == HttpMethod.Get && handler.Authorization == "Bearer profile-token",
                    "Same-region SGP ranked fallback must use league session token and pinned leagues-ledge path.");
                var rankedCalls = handler.Calls;
                var rankedTokenReads = local.Paths.Count;
                api.TryGetRankedStatsAsync("target-puuid", CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Calls == rankedCalls && local.Paths.Count == rankedTokenReads,
                    "Source-ranked league stats must never use cross-region Tencent access.");
                api.TryGetChallengesAsync("target-puuid", CancellationToken.None, "TENCENT_HN1").GetAwaiter().GetResult();
                Require(handler.Url == "https://hn1-k8s-sgp.lol.qq.com:21019/challenges-client/v2/all-player-data/?puuid=target-puuid" &&
                    handler.Method == HttpMethod.Post && handler.Body == "[]" &&
                    handler.Authorization == "Bearer profile-token",
                    "Regional challenges must preserve the trailing slash, empty friends array and League-session token.");
                api.TryGetGsmByPuuidAsync("local-puuid", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url == "https://hn10-k8s-sgp.lol.qq.com:21019/gsm/v1/ledge/region/HN10/puuid/local-puuid" &&
                    handler.Method == HttpMethod.Get && handler.Authorization == "Bearer profile-token",
                    "Pinned GSM live-team read must use current region and League-session token.");
                api.TryGetGsmByGameIdAsync(601, CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/gsm/v1/ledge/region/HN10/gameId/601", StringComparison.Ordinal),
                    "Pinned GSM gameId read must use the current region.");
                api.TryGetStatsEogAsync(601, "local-puuid", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/stats/endOfGame/region/HN10/gameId/601/puuid/local-puuid", StringComparison.Ordinal) &&
                    handler.Authorization == "Bearer profile-token",
                    "Pinned SGP end-of-game read must be current-region and League-session scoped.");
                api.TryGetStatsEogAsync(601, "other-puuid", CancellationToken.None)
                    .GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/stats/endOfGame/region/HN10/gameId/601/puuid/other-puuid",
                    StringComparison.Ordinal),
                    "Current-region EOG must support a selected remote player without using the owner's PUUID.");
                ValidateSupportedQueues(api, handler, local);
                var calls = handler.Calls;
                var reads = local.Paths.Count;
                api.TryGetSummaryAsync("../unsafe", 0, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummaryAsync("target", int.MaxValue, 10, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetGameSummaryAsync(0, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetSummonerAsync("target", CancellationToken.None, "https://other.example")
                    .GetAwaiter().GetResult();
                api.TryGetChallengesAsync("../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetChallengesAsync("target", CancellationToken.None, "NA1").GetAwaiter().GetResult();
                api.TryGetRankedStatsAsync("../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetGsmByPuuidAsync("../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetGsmByGameIdAsync(0, CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetStatsEogAsync(-1, "local-puuid", CancellationToken.None).GetAwaiter().GetResult();
                api.TryGetStatsEogAsync(601, "../unsafe", CancellationToken.None).GetAwaiter().GetResult();
                Require(handler.Calls == calls && local.Paths.Count == reads,
                    "Invalid target inputs must fail before credentials or HTTP are requested.");
                handler.AfterRead = () => current = Session("HN1", "TENCENT");
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null,
                    "Even an unchanged LCU port/password cannot publish a response after region replacement.");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel();
                    var cancelled = false;
                    try { api.TryGetGameSummaryAsync(601, cancel.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Require(cancelled, "External cancellation must remain observable by the caller.");
                }
            }
            ValidateRecovery();
            ValidateCatalogRefresh();
            ValidateEogProjection();
            ValidateEogOnlyFallback();
            ValidateQueueNames();
            ValidateCurrentLobbyQueue();
            Require(LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"profile-token\"")) == "profile-token" &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("{\"token\":\"wrong-shape\"}")) == null &&
                LeagueSgpMatchHistoryApiClient.ReadLeagueSessionToken(Bytes("\"bad\\u007fheader\"")) == null,
                "League session credentials must be a bounded printable JSON string.");
        }

        private static void ValidateQueueNames()
        {
            var names = LeagueSgpSupportedQueues.ParseQueueNames(Bytes(
                @"[{""id"":420,""name"":""Ranked Solo""},{""id"":450,""name"":""ARAM""},
                    {""id"":450,""name"":""Duplicate""},{""id"":-1,""name"":""Bad""},
                    {""id"":430,""name"":""Line\nBreak""}]"));
            Require(names.Count == 2 && names[420] == "Ranked Solo" && names[450] == "ARAM",
                "LCU queue metadata must supply trusted display names without duplicate or unsafe labels.");
            Require(LeagueSgpSupportedQueues.ParseQueueNames(Bytes(@"{}")).Count == 0 &&
                LeagueSgpSupportedQueues.ParseQueueNames(null).Count == 0,
                "Unavailable or malformed queue metadata must preserve numeric fallback.");
        }

        private static void ValidateCurrentLobbyQueue()
        {
            Require(LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(
                    @"{""gameConfig"":{""queueId"":420}}")) == 420 &&
                LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(
                    @"{""gameConfig"":{""queueId"":450}}")) == 450 &&
                LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(
                    @"{""gameConfig"":{""queueId"":""420""}}")) == null &&
                LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(
                    @"{""gameConfig"":{""queueId"":-1}}")) == null &&
                LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(
                    @"{""gameConfig"":{""queueId"":420.5}}")) == null &&
                LeagueSgpSupportedQueues.ParseLobbyQueueId(Bytes(@"{}")) == null,
                "Lobby queue suggestions require an exact bounded numeric LCU gameConfig.queueId.");
            var session = Session("HN1", "TENCENT");
            var active = session;
            var local = new LobbyApi();
            var service = new LeaguePlayerDataService(local,
                new FACM.Performance.PerformanceBudgetProvider(), null, null, () => active);
            Require(service.LoadCurrentLobbyQueueIdAsync("self", session, CancellationToken.None)
                    .GetAwaiter().GetResult() == 420 && local.Calls == 3,
                "Current lobby queue must be read once between two verified local account reads.");
            local.AfterLobby = () => local.Owner = "other";
            Require(service.LoadCurrentLobbyQueueIdAsync("self", session, CancellationToken.None)
                    .GetAwaiter().GetResult() == null,
                "A changed owner after the lobby read must discard the queue suggestion.");
            local.Owner = "self";
            local.AfterLobby = () => active = Session("HN10", "TENCENT");
            Require(service.LoadCurrentLobbyQueueIdAsync("self", session, CancellationToken.None)
                    .GetAwaiter().GetResult() == null,
                "A changed region/session after the lobby read must discard the queue suggestion.");
        }

        private sealed class LobbyApi : ILeagueClientApi
        {
            internal string Owner = "self";
            internal int Calls;
            internal Action AfterLobby;
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls++;
                if (path == "/lol-lobby/v2/lobby")
                {
                    AfterLobby?.Invoke();
                    return Task.FromResult(Bytes(@"{""gameConfig"":{""queueId"":420}}"));
                }
                return Task.FromResult(Bytes(@"{""puuid"":""" + Owner + @"""}"));
            }
        }

        private static void ValidateEogProjection()
        {
            var fixture = Bytes(@"{""gameId"":601,""puuid"":""local-puuid"",""gameMode"":""CLASSIC"",
                ""queueId"":420,""gameLength"":1800,""roomPassword"":""never-show"",""mucJwtDto"":{""jwt"":""secret""},
                ""teamPlayerParticipantStats"":[{""gameId"":601,""puuid"":""local-puuid"",
                    ""teamId"":100,""championId"":12,""riotIdGameName"":""Local"",
                    ""level"":30,""wins"":12,""losses"":4,""eloChange"":-12.5,
                    ""leaver"":false,""botPlayer"":false,
                    ""riotIdTagLine"":""CN1"",""spell1Id"":4,""spell2Id"":14,
                    ""detectedTeamPosition"":""MIDDLE"",""statistics"":[
                        {""statTypeName"":""CHAMPIONS_KILLED"",""value"":5},
                        {""statTypeName"":""NUM_DEATHS"",""value"":2},
                        {""statTypeName"":""ASSISTS"",""value"":8},
                        {""statTypeName"":""WIN"",""value"":1},
                        {""statTypeName"":""DAMAGE_RATIO"",""value"":0.75},
                        {""statTypeName"":""RANK_CHANGE"",""value"":-12}]}],
                ""otherTeamPlayerParticipantStats"":[{""gameId"":601,""puuid"":""other-puuid"",
                    ""teamId"":200,""championId"":13,""statistics"":[]}]}");
            var parsed = LeagueSgpEogProjection.Parse(fixture, 601, "local-puuid");
            Require(parsed != null && parsed.Players.Count == 2 &&
                parsed.GameLengthSeconds == 1800 &&
                parsed.Players[0].Name == "Local#CN1" && parsed.Players[0].Kills == 5 &&
                parsed.Players[0].Win == true && parsed.Players[0].Spell1Id == 4 &&
                parsed.Players[0].Spell2Id == 14 && parsed.Players[0].Position == "MIDDLE" &&
                parsed.Players[0].Statistics.Count == 6 &&
                parsed.Players[0].Statistics["CHAMPIONS_KILLED"] == 5 &&
                parsed.Players[0].Statistics["DAMAGE_RATIO"] == 0.75 &&
                parsed.Players[0].Statistics["RANK_CHANGE"] == -12 &&
                parsed.Players[0].Metadata["LEVEL"] == "30" &&
                parsed.Players[0].Metadata["ELO_CHANGE"] == "-12.5" &&
                parsed.Players[0].Metadata["LEAVER"] == "false" &&
                parsed.Players[0].Metadata["BOT_PLAYER"] == "false" &&
                parsed.Players[0].Metadata.Count == 6 &&
                parsed.Players[1].Metadata.Count == 0 &&
                !parsed.Players[0].Damage.HasValue &&
                !parsed.Players[1].Win.HasValue,
                "Pinned EOG team and participant stats must distinguish known values from missing metrics.");

            var p1 = new LeagueSgpEogPlayer {
                TeamId = 100, Kills = 5, Deaths = 2, Assists = 8,
                Damage = 15000, Gold = 12000, Minions = 150, NeutralMinions = 20, Vision = 30 };
            var p2 = new LeagueSgpEogPlayer {
                TeamId = 100, Kills = 10, Deaths = 3, Assists = 4,
                Damage = 10000, Gold = 8000, Minions = 90, NeutralMinions = 10, Vision = 12 };
            var balanced = new LeagueSgpEogResult {
                GameId = 601, GameLengthSeconds = 1800,
                Players = new List<LeagueSgpEogPlayer> { p1, p2 } };
            var metrics = LeagueSgpEogProjection.Analyze(balanced, p1);
            Require(metrics.Kda == 6.5 && Math.Abs(metrics.KillParticipation.Value - 13d / 15d) < 0.00001 &&
                Math.Abs(metrics.DamageShare.Value - 0.6) < 0.00001 &&
                Math.Abs(metrics.GoldShare.Value - 0.6) < 0.00001 &&
                metrics.DamagePerMinute == 500 && metrics.GoldPerMinute == 400 &&
                Math.Abs(metrics.CsPerMinute.Value - 170d / 30d) < 0.00001 &&
                metrics.VisionPerMinute == 1,
                "EOG source game duration and complete team evidence must support native per-player analysis.");
            p2.Damage = null;
            Require(!LeagueSgpEogProjection.Analyze(balanced, p1).DamageShare.HasValue &&
                LeagueSgpEogProjection.Analyze(balanced, p1).DamagePerMinute == 500,
                "Missing peer damage must hide team share without discarding a sourced per-minute metric.");
            balanced.GameLengthSeconds = null;
            Require(!LeagueSgpEogProjection.Analyze(balanced, p1).CsPerMinute.HasValue &&
                LeagueSgpEogProjection.Analyze(balanced, p1).Kda == 6.5,
                "Missing duration must not fabricate per-minute metrics or erase complete KDA.");
            Require(!LeagueSgpEogProjection.Analyze(balanced, new LeagueSgpEogPlayer { TeamId = 100 }).Kda.HasValue,
                "Unverified players must not receive derived EOG statistics.");
            using (var dialog = new LeagueSgpEogOnlyDialog(FACM.Services.UiTextCatalog.Load(),
                new LeagueEncounterMatchDetailResult {
                    Status = LeagueEncounterMatchDetailStatus.Ready,
                    Match = new LeaguePlayerMatchSummary { GameId = 601 },
                    EogOnly = parsed, IsCurrentSession = () => true }))
            {
                var layout = dialog.Controls.OfType<System.Windows.Forms.TableLayoutPanel>().FirstOrDefault();
                var split = layout?.Controls.OfType<System.Windows.Forms.SplitContainer>().FirstOrDefault();
                var rows = split?.Panel1.Controls.OfType<System.Windows.Forms.ListView>().FirstOrDefault();
                Require(dialog.Controls.Count > 0 && dialog.Text.Length > 0 &&
                    rows != null && rows.Columns.Count == 11 && rows.Items.Count == 2,
                    "A validated EOG-only match must construct complete native team/player rows without SUMMARY.");
            }
            Require(LeagueSgpEogProjection.Parse(fixture, 602, "local-puuid") == null &&
                LeagueSgpEogProjection.Parse(fixture, 601, "wrong-owner") == null &&
                LeagueSgpEogProjection.Parse(Bytes(@"{""gameId"":601,""puuid"":""local-puuid"",
                    ""teamPlayerParticipantStats"":[],""otherTeamPlayerParticipantStats"":[]}"),
                    601, "local-puuid") == null &&
                LeagueSgpEogProjection.Parse(Bytes(@"{""gameId"":601,""puuid"":""local-puuid"",
                    ""teamPlayerParticipantStats"":[{""gameId"":601,""puuid"":""local-puuid"",
                        ""teamId"":100,""championId"":12,""statistics"":[]}],
                    ""otherTeamPlayerParticipantStats"":[{""gameId"":601,
                        ""puuid"":""local-puuid"",""teamId"":200,""championId"":13,
                        ""statistics"":[]}]}"), 601, "local-puuid") == null,
                "EOG wrong game, wrong owner, empty roster and duplicate identity must fail closed.");
        }

        private static void ValidateEogOnlyFallback()
        {
            var local = Session("HN1", "TENCENT");
            var foreign = Session("NA1", "NA1");
            Require(LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(local, null, true) &&
                LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(local, "TENCENT_HN1", true) &&
                !LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(local, "TENCENT_HN10", true) &&
                !LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(local, null, false) &&
                !LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(foreign, "TENCENT_HN1", true) &&
                !LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(Session("UNKNOWN", "UNKNOWN"), null, true) &&
                !LeagueEncounterMatchDetailService.CanUseEogOnlyFallback(null, null, true),
                "EOG-only detail must be offered only for the current trusted region and non-LCU source.");
            Require(LeagueSgpEogOnlyDialog.FormatStatistic(-2.5) == "-2.5" &&
                LeagueSgpEogOnlyDialog.FormatStatistic(0.125) == "0.125" &&
                LeagueSgpEogOnlyDialog.FormatMetric(null) == "—" &&
                LeagueSgpEogOnlyDialog.FormatMetric(0) == "0",
                "EOG-only inspection must preserve signed/fractional values, true zero and missing metrics.");
        }

        private static void ValidateSupportedQueues(LeagueSgpMatchHistoryApiClient api,
            CaptureHandler handler, TokenApi local)
        {
            try
            {
                Require(LeagueSgpSupportedQueues.Snapshot().Length == 17 &&
                    LeagueSgpSupportedQueues.IsValidTag("q_420") &&
                    LeagueSgpSupportedQueues.IsValidTag("ranked") &&
                    LeagueSgpSupportedQueues.IsValidTag("normal"),
                    "Pinned supported queue defaults and general tags must be available.");
                api.TryGetTaggedSummaryAsync("target", 0, 10, CancellationToken.None, null, "q_420")
                    .GetAwaiter().GetResult();
                Require(handler.Url.EndsWith("/SUMMARY?startIndex=0&count=10&tag=q_420", StringComparison.Ordinal) &&
                    handler.Authorization == "Bearer history-token",
                    "SGP queue selection must use server-side tags with the existing entitlement owner.");
                var calls = handler.Calls;
                var reads = local.Paths.Count;
                api.TryGetTaggedSummaryAsync("target", 0, 10, CancellationToken.None, null, "q_999")
                    .GetAwaiter().GetResult();
                api.TryGetTaggedSummaryAsync("target", 0, 10, CancellationToken.None, null, "ranked&count=999")
                    .GetAwaiter().GetResult();
                Require(handler.Calls == calls && local.Paths.Count == reads,
                    "Unlisted and injected queue tags must fail before token reads.");
                var valid = Bytes("{\"updatedAt\":\"2026-08-01T00:00:00.000Z\",\"queues\":[420,450,9999]}");
                Require(LeagueSgpSupportedQueues.TryApply(valid) &&
                    LeagueSgpSupportedQueues.IsValidTag("q_9999") &&
                    !LeagueSgpSupportedQueues.IsValidTag("q_440") &&
                    LeagueSgpSupportedQueues.Snapshot().Length == 3,
                    "New supported queues must atomically replace the old snapshot.");
                Require(!LeagueSgpSupportedQueues.TryApply(valid) &&
                    !LeagueSgpSupportedQueues.TryApply(Bytes("{\"updatedAt\":\"2026-08-02T00:00:00.000Z\",\"queues\":[420,420]}")) &&
                    !LeagueSgpSupportedQueues.TryApply(Bytes("{\"updatedAt\":\"2026-08-02T00:00:00.000Z\",\"queues\":[-1]}")) &&
                    LeagueSgpSupportedQueues.IsValidTag("q_9999"),
                    "Stale, duplicate and invalid catalogs must preserve the active list.");
            }
            finally { LeagueSgpSupportedQueues.ResetForSmokeTest(); }
        }

        private static void ValidateCatalogRefresh()
        {
            var owner = Session("HN10", "TENCENT");
            var baseline = LeagueSgpMatchHistoryApiClient.ResolveServer(owner).MatchHistory;
            var valid = @"{""updatedAt"":""2026-08-01T00:00:00.000Z"",""servers"":{""TENCENT_HN10"":{""matchHistory"":""https://hn10-alt-sgp.lol.qq.com:21019"",""common"":""https://hn10-alt-sgp.lol.qq.com:21019"",""isTencent"":true}},""serverNames"":{""zh-CN"":{""TENCENT_HN10"":""黑玫瑰新区""}}}";
            try
            {
                Require(LeagueSgpServerCatalog.TryApply(Bytes(valid)) &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(owner).MatchHistory ==
                        "https://hn10-alt-sgp.lol.qq.com:21019" &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(owner).Name == "黑玫瑰新区",
                    "Newer trusted catalog must update existing region destination and label.");
                Require(!LeagueSgpServerCatalog.TryApply(Bytes(valid)) &&
                    !LeagueSgpServerCatalog.TryApply(Bytes(valid.Replace("2026-08-01", "2026-07-01"))),
                    "Catalog must reject stale or equal versions.");
                foreach (var bad in new[] {
                    valid.Replace("hn10-alt-sgp.lol.qq.com", "attacker.example"),
                    valid.Replace("hn10-alt-sgp.lol.qq.com", "hn1-alt-sgp.lol.qq.com"),
                    valid.Replace("21019", "443"),
                    valid.Replace("true", "false"),
                    valid.Replace("TENCENT_HN10", "UNKNOWN")
                })
                    Require(!LeagueSgpServerCatalog.TryApply(Bytes(bad.Replace("2026-08-01", "2026-08-02"))),
                        "Untrusted, wrong-port, wrong-owner and unknown-only catalogs must fail closed.");
                var newlyTrusted = @"{""updatedAt"":""2026-08-03T00:00:00.000Z"",""servers"":{""TENCENT_HN11"":{""matchHistory"":""https://hn11-k8s-sgp.lol.qq.com:21019"",""common"":""https://hn11-k8s-sgp.lol.qq.com:21019"",""isTencent"":true}},""serverNames"":{""zh-CN"":{""TENCENT_HN11"":""合成新区""}}}";
                Require(LeagueSgpServerCatalog.TryApply(Bytes(newlyTrusted)) &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("HN11", "TENCENT"))?.Id == "TENCENT_HN11" &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(owner, "TENCENT_HN11")?.PathRegion == "HN11" &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("HN11", "TENCENT"))?.Name == "合成新区",
                    "A source-announced Tencent platform must enroll only under its verified SGP host template.");
                foreach (var unsafeNew in new[] {
                    newlyTrusted.Replace("hn11-k8s-sgp.lol.qq.com", "hn11-attacker.lol.qq.com"),
                    newlyTrusted.Replace("hn11-k8s-sgp.lol.qq.com", "attacker.example"),
                    newlyTrusted.Replace("21019", "443"),
                    newlyTrusted.Replace(@"""isTencent"":true", @"""isTencent"":true,""regionPathParam"":""HN10"""),
                    newlyTrusted.Replace(@"""isTencent"":true", @"""isTencent"":false")
                })
                    Require(!LeagueSgpServerCatalog.TryApply(Bytes(unsafeNew.Replace("2026-08-03", "2026-08-04"))),
                        "A new platform must reject wrong provider, hostname, port or region identity.");
                Require(LeagueSgpServerCatalog.TryApply(Bytes(valid.Replace("2026-08-01", "2026-08-05"))) &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("HN11", "TENCENT")) == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(owner).Name == "黑玫瑰新区",
                    "A newer catalog must revoke an omitted dynamic platform and retain its own known entries.");
                Require(LeagueSgpMatchHistoryApiClient.ResolveServer(Session("NA1", "NA1"), "TENCENT_HN11") == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(Session("NA1", "NA1"), "TENCENT_HN10") == null &&
                    LeagueSgpMatchHistoryApiClient.ResolveServer(owner, "NA1") == null,
                    "Dynamic catalog must not widen cross-region permissions.");
            }
            finally { LeagueSgpServerCatalog.ResetForSmokeTest(); }
            Require(LeagueSgpMatchHistoryApiClient.ResolveServer(owner).MatchHistory == baseline,
                "Synthetic catalog must restore original endpoints.");
        }

        private static void ValidateRecovery()
        {
            Require(LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.BadGateway) &&
                LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.ServiceUnavailable) &&
                LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus(HttpStatusCode.GatewayTimeout) &&
                !LeagueSgpMatchHistoryApiClient.IsTransientGatewayStatus((HttpStatusCode)429),
                "Gateway retries must not include rate-limited responses.");
            var owner = Session("HN10", "TENCENT");
            var local = new TokenApi();
            var handler = new CaptureHandler();
            local.HistoryTokens.Enqueue("expired-token");
            local.HistoryTokens.Enqueue("fresh-token");
            handler.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            handler.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, local, handler))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() != null &&
                    handler.Calls == 2 && local.Paths.Count == 2 &&
                    handler.Authorization == "Bearer fresh-token",
                    "An expired entitlement token must be freshly read before one retry.");

            var unchanged = new CaptureHandler();
            unchanged.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            unchanged.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), unchanged))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    unchanged.Calls == 1,
                    "A rejected unchanged token must not trigger another HTTP request.");

            var gateway = new CaptureHandler();
            gateway.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            gateway.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), gateway))
                Require(api.TryGetSummonerAsync("target", CancellationToken.None).GetAwaiter().GetResult() != null &&
                    gateway.Calls == 2 && gateway.Bodies.Count == 2 &&
                    gateway.Bodies[0] == "[\"target\"]" && gateway.Bodies[1] == gateway.Bodies[0],
                    "A transient gateway error must retry the same JSON POST body.");

            var profileLocal = new TokenApi();
            profileLocal.ProfileTokens.Enqueue("expired-profile");
            profileLocal.ProfileTokens.Enqueue("fresh-profile");
            var profileHandler = new CaptureHandler();
            profileHandler.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, profileLocal, profileHandler))
                Require(api.TryGetSummonerAsync("target", CancellationToken.None).GetAwaiter().GetResult() != null &&
                    profileHandler.Calls == 2 &&
                    profileHandler.Authorization == "Bearer fresh-profile" &&
                    profileHandler.Bodies.Count == 2 &&
                    profileHandler.Bodies[0] == profileHandler.Bodies[1],
                    "League-session token refresh must retry an identical summoner POST only once.");

            var stillUnavailable = new CaptureHandler();
            stillUnavailable.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            stillUnavailable.Statuses.Enqueue(HttpStatusCode.ServiceUnavailable);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), stillUnavailable))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    stillUnavailable.Calls == 2, "Repeated gateway errors must not cause unbounded retries.");

            var connection = new CaptureHandler { ThrowOnce = true };
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), connection))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() != null &&
                    connection.Calls == 2, "A single transport failure must recover with a bounded retry.");

            var throttled = new CaptureHandler();
            throttled.Statuses.Enqueue((HttpStatusCode)429);
            throttled.Statuses.Enqueue(HttpStatusCode.OK);
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), throttled))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    throttled.Calls == 1,
                    "Rate-limited SGP responses must never be retried.");

            var replaced = new CaptureHandler();
            replaced.Statuses.Enqueue(HttpStatusCode.Unauthorized);
            replaced.AfterRead = () => owner = Session("HN1", "TENCENT");
            using (var api = new LeagueSgpMatchHistoryApiClient(() => owner, new TokenApi(), replaced))
                Require(api.TryGetGameSummaryAsync(601, CancellationToken.None).GetAwaiter().GetResult() == null &&
                    replaced.Calls == 1,
                    "Changing the LCU region must prevent credential retry.");
        }

        private static LeagueClientSession Session(string platform, string region)
        {
            return new LeagueClientSession("LeagueClientUx", 1, 12345, "local-fixture", "https", "fixture", platform, region);
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class TokenApi : ILeagueClientApi
        {
            internal readonly List<string> Paths = new List<string>();
            internal readonly Queue<string> HistoryTokens = new Queue<string>();
            internal readonly Queue<string> ProfileTokens = new Queue<string>();
            public Task<byte[]> TryGetBytesAsync(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Paths.Add(path);
                var historyToken = HistoryTokens.Count > 0 ? HistoryTokens.Dequeue() : "history-token";
                var profileToken = ProfileTokens.Count > 0 ? ProfileTokens.Dequeue() : "profile-token";
                return Task.FromResult(Bytes(path == LeagueSgpMatchHistoryApiClient.EntitlementsPath
                    ? "{\"accessToken\":\"" + historyToken + "\"}" : "\"" + profileToken + "\""));
            }
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            internal int Calls;
            internal readonly Queue<HttpStatusCode> Statuses = new Queue<HttpStatusCode>();
            internal bool ThrowOnce;
            internal readonly List<string> Bodies = new List<string>();
            internal string Url, Authorization, Body;
            internal HttpMethod Method;
            internal Action AfterRead;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Calls++;
                Url = request.RequestUri.AbsoluteUri;
                Authorization = request.Headers.Authorization.ToString();
                Method = request.Method;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                Bodies.Add(Body);
                if (ThrowOnce) { ThrowOnce = false; throw new HttpRequestException("synthetic network failure"); }
                AfterRead?.Invoke();
                return new HttpResponseMessage(Statuses.Count == 0 ? HttpStatusCode.OK : Statuses.Dequeue())
                    { Content = new ByteArrayContent(Bytes("{}")) };
            }
        }
    }
}
