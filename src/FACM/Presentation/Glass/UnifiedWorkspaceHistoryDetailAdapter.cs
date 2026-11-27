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

        private static double? NonNegative(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) &&
                value.Value >= 0 ? value : null;
        }

        private static double? Share(double? value, bool denominatorKnown)
        {
            var valid = NonNegative(value);
            return denominatorKnown && valid.HasValue && valid.Value <= 1 ? valid : null;
        }

        internal static object ProjectReview(LeagueEncounterMatchDetailResult detail)
        {
            var match = detail == null ? null : detail.Match;
            if (match == null || !match.ParticipantResolved || match.ParticipantId <= 0 ||
                detail.Participants == null) return null;
            var targets = detail.Participants.Where(p => p != null && p.IsTarget).ToArray();
            if (targets.Length != 1 || targets[0].ParticipantId != match.ParticipantId) return null;
            var target = targets[0];
            var outcome = LeaguePlayerHistoryAnalysis.Outcome(match);
            var validStats = match.StatisticsAvailable && outcome != LeagueHistoryOutcome.Remake &&
                outcome != LeagueHistoryOutcome.Abort && match.Kills >= 0 &&
                match.Deaths >= 0 && match.Assists >= 0;
            var cherry = LeagueCherryTeams.IsCherry(match.GameMode);
            var classic = string.Equals(match.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase);
            var aram = string.Equals(match.GameMode, "ARAM", StringComparison.OrdinalIgnoreCase);
            var standard = (classic && match.MapId == 11) || (aram && match.MapId == 12);
            var groupId = LeagueCherryTeams.GroupId(match, cherry);
            var members = (match.ParticipantRoster ?? new LeaguePlayerMatchSummary[0])
                .Where(p => p != null && groupId > 0 && LeagueCherryTeams.GroupId(p, cherry) == groupId)
                .ToArray();
            var complete = validStats && standard && groupId > 0 && members.Length == 5 &&
                members.All(p => p.ParticipantId > 0 && p.StatisticsAvailable && p.Kills >= 0) &&
                members.Select(p => p.ParticipantId).Distinct().Count() == 5 &&
                members.Count(p => p.ParticipantId == match.ParticipantId) == 1;
            var teams = detail.Teams == null ? new LeagueMatchTeamScore[0] :
                detail.Teams.Where(t => t != null && t.TeamId == groupId && t.IsCherry == cherry).ToArray();
            var team = complete && teams.Length == 1 && teams[0].PlayerCount == 5 ? teams[0] : null;
            var fullTaken = team != null && members.All(p => NonNegative(p.DamageTaken).HasValue) &&
                members.Sum(p => p.DamageTaken.Value) > 0;
            var duration = match.GameDurationSeconds;
            return new
            {
                champion = target.ChampionName,
                championId = target.ChampionId,
                outcome = outcome.ToString().ToLowerInvariant(),
                placement = cherry ? LeagueCherryTeams.ValidPlacement(match.SubteamPlacement) : null,
                kills = validStats ? (int?)match.Kills : null,
                deaths = validStats ? (int?)match.Deaths : null,
                assists = validStats ? (int?)match.Assists : null,
                kda = validStats ? (double?)((match.Kills + (double)match.Assists) / Math.Max(1, match.Deaths)) : null,
                damage = validStats ? NonNegative(match.DamageDealtToChampions) : null,
                gold = validStats ? NonNegative(match.GoldEarned) : null,
                taken = validStats ? NonNegative(match.DamageTaken) : null,
                csPerMinute = validStats && classic && match.MapId == 11 && duration > 0 &&
                    target.CreepScoreAvailable && target.CreepScore >= 0
                    ? (double?)target.CreepScore * 60d / duration : null,
                vision = validStats && classic && match.MapId == 11 ? NonNegative(match.VisionScore) : null,
                damageShare = Share(match.ChampionDamageShare, team != null && team.DamageToChampions > 0),
                goldShare = Share(match.GoldShare, team != null && team.GoldEarned > 0),
                takenShare = Share(match.DamageTakenShare, fullTaken),
                participation = Share(match.KillParticipation, team != null && team.Kills > 0),
                towers = team == null ? null : team.TowerKills,
                dragons = team == null || !classic ? null : team.DragonKills,
                barons = team == null || !classic ? null : team.BaronKills,
                heralds = team == null || !classic ? null : team.RiftHeraldKills
            };
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
                review = ProjectReview(detail),
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
            var summary = new LeaguePlayerMatchSummary
            {
                ParticipantId = 1, ParticipantResolved = true, StatisticsAvailable = true,
                GameMode = "CLASSIC", MapId = 11, TeamId = 100, WinResult = "win",
                Kills = 4, Deaths = 2, Assists = 8, GameDurationSeconds = 1200,
                DamageDealtToChampions = 15000, ChampionDamageShare = 0.3,
                GoldShare = 0.2, KillParticipation = 0.6, VisionScore = null,
                ParticipantRoster = Enumerable.Range(1, 5).Select(id => new LeaguePlayerMatchSummary
                { ParticipantId = id, TeamId = 100, StatisticsAvailable = true }).ToArray()
            };
            var reviewDetail = new LeagueEncounterMatchDetailResult
            {
                Match = summary,
                Participants = new System.Collections.Generic.List<LeaguePlayerMatchParticipant>
                {
                    new LeaguePlayerMatchParticipant { ParticipantId = 1, IsTarget = true,
                        ChampionId = 22, CreepScoreAvailable = true, CreepScore = 140 }
                },
                Teams = new System.Collections.Generic.List<LeagueMatchTeamScore>
                {
                    new LeagueMatchTeamScore { TeamId = 100, PlayerCount = 5, Kills = 20,
                        DamageToChampions = 50000, GoldEarned = 50000, DragonKills = 2 }
                }
            };
            var serializer = new JavaScriptSerializer();
            var review = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject(
                serializer.Serialize(ProjectReview(reviewDetail)));
            if (Convert.ToDouble(review["kda"]) != 6 || Convert.ToDouble(review["csPerMinute"]) != 7 ||
                Convert.ToDouble(review["damageShare"]) != 0.3 || Convert.ToInt32(review["dragons"]) != 2 ||
                review["vision"] != null || review["takenShare"] != null)
                throw new InvalidOperationException("Review lost confirmed metrics or invented missing values.");
            summary.ParticipantRoster = summary.ParticipantRoster.Take(4).ToArray();
            review = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject(
                serializer.Serialize(ProjectReview(reviewDetail)));
            if (review["damageShare"] != null || review["participation"] != null)
                throw new InvalidOperationException("Incomplete team produced contribution ratios.");
            summary.GameMode = "CHERRY";
            summary.SubteamPlacement = 2;
            review = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject(
                serializer.Serialize(ProjectReview(reviewDetail)));
            if (Convert.ToInt32(review["placement"]) != 2 || review["csPerMinute"] != null ||
                review["dragons"] != null || review["vision"] != null)
                throw new InvalidOperationException("Arena review exposed incompatible metrics.");
            reviewDetail.Participants[0].ParticipantId = 2;
            if (ProjectReview(reviewDetail) != null || ProjectReview(null) != null)
                throw new InvalidOperationException("Unverified target produced a review.");
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
