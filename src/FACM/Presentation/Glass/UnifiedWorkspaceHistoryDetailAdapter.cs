using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // One request owner for both local and regional match selections.
    internal sealed class UnifiedWorkspaceHistoryDetailAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Action _clearSelected;
        private readonly Action _invalidateHistory;
        private readonly Action<string, LeagueEncounterMatchDetailResult, LeagueClientSession> _remember;
        private CancellationTokenSource _cancellation;
        private int _generation;

        internal UnifiedWorkspaceHistoryDetailAdapter(Func<bool> canPublish, Action<string> publish,
            Action clearSelected, Action invalidateHistory,
            Action<string, LeagueEncounterMatchDetailResult, LeagueClientSession> remember)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _clearSelected = clearSelected ?? throw new ArgumentNullException(nameof(clearSelected));
            _invalidateHistory = invalidateHistory ?? throw new ArgumentNullException(nameof(invalidateHistory));
            _remember = remember ?? throw new ArgumentNullException(nameof(remember));
        }

        internal void StartLocal(LeaguePlayerModule module, LeagueUnifiedHistorySnapshot loaded,
            LeagueClientSession session, string raw)
        {
            if (!_canPublish() || string.IsNullOrEmpty(raw) || raw.Length > 20) return;
            long gameId;
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out gameId) ||
                gameId <= 0) { PostStatus(raw, "Unavailable"); return; }
            if (loaded == null || loaded.Profile == null || loaded.Page == null ||
                module == null || session == null ||
                !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession))
            { PostStatus(raw, "WrongAccount"); return; }
            var match = loaded.Page.Matches.FirstOrDefault(m => m.GameId == gameId);
            if (match == null) { PostStatus(raw, "Unavailable"); return; }
            Cancel();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _cancellation = source;
            ReadLocalAsync(module, loaded, session, match, raw, source, _generation);
        }

        private async void ReadLocalAsync(LeaguePlayerModule module, LeagueUnifiedHistorySnapshot loaded,
            LeagueClientSession session, LeaguePlayerMatchSummary match, string raw,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var detail = await module.ReadUnifiedMatchDetailAsync(
                    session, loaded.Profile.PuuId, match, source.Token);
                if (!Current(module, session, source, generation))
                {
                    if (Current(module, session, source, generation, false))
                    {
                        _invalidateHistory();
                        PostStatus(raw, "WrongAccount");
                    }
                    return;
                }
                PublishDetail(raw, detail, session);
            }
            catch (OperationCanceledException)
            {
                if (Current(module, session, source, generation, false)) PostStatus(raw, "Unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified history detail failed: " + exception.GetType().Name);
                if (Current(module, session, source, generation, false)) PostStatus(raw, "Unavailable");
            }
            finally { Finish(source); }
        }

        internal void StartRegional(LeaguePlayerModule module, LeagueRemotePlayerHistoryResult regional,
            LeagueClientSession session, LeaguePlayerProfile owner, string target, string raw)
        {
            if (!_canPublish() || string.IsNullOrEmpty(raw) || raw.Length > 20) return;
            long gameId;
            if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out gameId) ||
                gameId <= 0 || regional == null || regional.Page == null ||
                session == null || owner == null || !LeagueDynamicMessageService.SafePuuid(target))
            { PostStatus(raw, "Unavailable"); return; }
            if (module == null || !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession))
            { PostStatus(raw, "WrongAccount"); return; }
            var match = regional.Page.Matches.FirstOrDefault(m => m.GameId == gameId);
            if (match == null) { PostStatus(raw, "Unavailable"); return; }
            Cancel();
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            _cancellation = source;
            ReadRegionalAsync(module, match, session, owner.PuuId, target, raw, source, _generation);
        }

        private async void ReadRegionalAsync(LeaguePlayerModule module, LeaguePlayerMatchSummary match,
            LeagueClientSession session, string self, string target, string raw,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var detail = await module.ReadUnifiedRemoteDetailAsync(session, self, target,
                    match, source.Token);
                if (!Current(module, session, source, generation)) return;
                PublishDetail(raw, detail, session);
            }
            catch (OperationCanceledException)
            {
                if (Current(module, session, source, generation, false)) PostStatus(raw, "Unavailable");
            }
            catch (Exception exception)
            {
                AppLog.Info("Regional match detail skipped: " + exception.GetType().Name);
                if (Current(module, session, source, generation, false)) PostStatus(raw, "Unavailable");
            }
            finally { Finish(source); }
        }

        private void PublishDetail(string raw, LeagueEncounterMatchDetailResult detail,
            LeagueClientSession session)
        {
            if (detail == null || detail.Status != LeagueEncounterMatchDetailStatus.Ready || detail.Match == null)
            {
                PostStatus(raw, detail == null ? "Unavailable" : detail.Status.ToString());
                return;
            }
            _remember(raw, detail, session);
            if (_canPublish()) _publish(SerializeDetail(raw, detail));
        }

        private static string SerializeDetail(string raw, LeagueEncounterMatchDetailResult detail)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-detail",
                status = "Ready",
                id = raw,
                mode = detail.Match.GameMode,
                queue = detail.Match.QueueId,
                duration = detail.Match.GameDurationSeconds,
                source = detail.Match.DataSource.ToString(),
                teams = detail.Teams == null ? new object[0] :
                    detail.Teams.Take(12).Select(team => new
                    {
                        id = team.TeamId,
                        placement = team.SubteamPlacement,
                        kills = team.Kills,
                        deaths = team.Deaths,
                        assists = team.Assists,
                        damage = team.DamageToChampions,
                        towers = team.TowerKills,
                        dragons = team.DragonKills,
                        barons = team.BaronKills
                    }).ToArray(),
                players = detail.Participants == null ? new object[0] :
                    detail.Participants.Take(30).Select(player => new
                    {
                        name = player.PlayerName,
                        participantId = player.ParticipantId,
                        local = player.IsTarget,
                        championId = player.ChampionId,
                        champion = player.ChampionName,
                        team = player.TeamId,
                        position = player.Position,
                        kills = player.Kills,
                        deaths = player.Deaths,
                        assists = player.Assists,
                        damage = player.DamageAvailable ? (int?)player.DamageToChampions : null,
                        gold = player.GoldAvailable ? (int?)player.GoldEarned : null,
                        cs = player.CreepScoreAvailable ? (int?)player.CreepScore : null,
                        vision = player.VisionScore,
                        items = (player.ItemIds ?? new int[0]).Take(7).ToArray(),
                        spells = (player.SpellIds ?? new int[0]).Take(2).ToArray(),
                        perks = (player.PerkIds ?? new int[0]).Take(9).ToArray(),
                        augments = (player.AugmentIds ?? new int[0]).Take(6).ToArray()
                    }).ToArray()
            });
        }

        private bool Current(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return _canPublish() && generation == _generation &&
                ReferenceEquals(source, _cancellation) &&
                (!checkSession || LeagueEncounterReplayService.SessionMatches(
                    session, module.UnifiedHistorySession));
        }

        private void PostStatus(string raw, string status)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "history-advanced-detail", id = raw, status
                }));
        }

        private void Finish(CancellationTokenSource source)
        {
            if (ReferenceEquals(source, _cancellation)) _cancellation = null;
            source.Dispose();
        }

        internal void Cancel()
        {
            _clearSelected();
            _generation++;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var messages = 0;
            var clears = 0;
            var first = new UnifiedWorkspaceHistoryDetailAdapter(() => false,
                delegate { messages++; }, () => { clears++; }, () => { },
                (id, detail, session) => { });
            var second = new UnifiedWorkspaceHistoryDetailAdapter(() => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"history-advanced-detail\""))
                        throw new InvalidOperationException("Advanced history status contract changed.");
                    messages++;
                }, () => { clears++; }, () => { }, (id, detail, session) => { });
            first.StartLocal(null, null, null, "7");
            second.StartRegional(null, null, null, null, null, "7");
            first.Cancel();
            second.Cancel();
            second.Cancel();
            if (messages != 1 || clears != 3 || first._generation != 1 ||
                second._generation != 2)
                throw new InvalidOperationException("Detail adapter cancellation isolation failed.");
        }
    }
}
