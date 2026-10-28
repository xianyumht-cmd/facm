using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FACM.League
{
    internal sealed class LeagueHistoryHydrationResult
    {
        internal List<LeaguePlayerMatchSummary> Matches;
        internal readonly HashSet<string> Attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal int Updated;
        internal bool AccountChanged, TimedOut;
    }

    internal sealed partial class LeaguePlayerDataService
    {
        internal const int MaximumAnalysisHydrationCount = 20;

        internal static string HistoryDetailKey(LeaguePlayerMatchSummary match)
        {
            return (match.SgpServerId ?? "") + ":" + match.DataSource + ":" +
                match.GameId.ToString(CultureInfo.InvariantCulture);
        }

        internal static bool NeedsAnalysisDetail(LeaguePlayerMatchSummary match)
        {
            if (match == null || match.GameId <= 0 ||
                (!string.IsNullOrEmpty(match.GameType) && match.GameType != "MATCHED_GAME") ||
                match.WinResult == "remake" || match.WinResult == "abort") return false;
            return !match.ParticipantResolved || !match.StatisticsAvailable ||
                !match.RosterIdentitiesComplete || LeagueHistoryAkariScore.Calculate(new[] { match }) == null;
        }

        internal async Task<LeagueHistoryHydrationResult> HydrateAnalysisAsync(
            string selfPuuid, string targetPuuid, LeagueClientSession expectedSession,
            IList<LeaguePlayerMatchSummary> matches, ISet<string> alreadyAttempted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new LeagueHistoryHydrationResult { Matches = new List<LeaguePlayerMatchSummary>() };
            if (!IsSafePuuid(selfPuuid) || !IsSafePuuid(targetPuuid) || matches == null || matches.Count > 1000)
                return result;
            var original = matches.Select(CloneMatch).ToList();
            result.Matches = original.Select(CloneMatch).ToList();
            var target = new LeaguePlayerProfile { PuuId = targetPuuid };
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(24));
                try
                {
                    for (var i = 0; i < result.Matches.Count; i++)
                    {
                        var current = result.Matches[i];
                        if (!NeedsAnalysisDetail(current) ||
                            (!string.IsNullOrEmpty(current.PuuId) && current.PuuId != targetPuuid) ||
                            (current.DataSource != LeaguePlayerHistorySource.Lcu && current.DataSource != LeaguePlayerHistorySource.Sgp)) continue;
                        var key = HistoryDetailKey(current);
                        if ((alreadyAttempted != null && alreadyAttempted.Contains(key)) ||
                            result.Attempted.Contains(key)) continue;
                        if (result.Attempted.Count >= MaximumAnalysisHydrationCount) break;
                        // A regional row must retain its explicit source and server; never probe LCU for it.
                        if ((current.DataSource != LeaguePlayerHistorySource.Sgp &&
                            !string.IsNullOrEmpty(current.SgpServerId)) ||
                            (current.DataSource == LeaguePlayerHistorySource.Sgp &&
                            string.IsNullOrEmpty(current.SgpServerId))) continue;
                        try
                        {
                            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                            {
                                attempt.CancelAfter(TimeSpan.FromSeconds(4));
                                await _requestGate.WaitAsync(attempt.Token).ConfigureAwait(false);
                                try
                                {
                                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, attempt.Token).ConfigureAwait(false))
                                    { result.AccountChanged = true; break; }
                                    result.Attempted.Add(key);
                                    var game = await LoadGameAsync(target, current.GameId, attempt.Token,
                                        current.SgpServerId, current.DataSource == LeaguePlayerHistorySource.Sgp,
                                        current.DataSource == LeaguePlayerHistorySource.Sgp).ConfigureAwait(false);
                                    if (!await VerifyDetailsOwnerAsync(selfPuuid, expectedSession, attempt.Token).ConfigureAwait(false))
                                    { result.AccountChanged = true; break; }
                                    var hydrated = ParseMatch(game, target);
                                    if (!CanReplaceAnalysisMatch(current, hydrated, targetPuuid)) continue;
                                    hydrated.PreferredSource = current.PreferredSource;
                                    hydrated.ChampionName = current.ChampionId == hydrated.ChampionId
                                        ? current.ChampionName : hydrated.ChampionName;
                                    result.Matches[i] = hydrated;
                                    result.Updated++;
                                }
                                finally { _requestGate.Release(); }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            token.ThrowIfCancellationRequested();
                            if (deadline.IsCancellationRequested) throw;
                            result.TimedOut = true;
                        }
                    }
                    if (!result.AccountChanged)
                    {
                        await _requestGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                        try
                        {
                            result.AccountChanged = !await VerifyDetailsOwnerAsync(
                                selfPuuid, expectedSession, deadline.Token).ConfigureAwait(false);
                        }
                        finally { _requestGate.Release(); }
                    }
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    result.TimedOut = true;
                    result.Matches = original;
                    result.Updated = 0;
                }
            }
            if (!SessionStillCurrent(expectedSession)) result.AccountChanged = true;
            if (result.AccountChanged) { result.Matches.Clear(); result.Updated = 0; }
            return result;
        }

        internal static bool CanReplaceAnalysisMatch(LeaguePlayerMatchSummary old,
            LeaguePlayerMatchSummary value, string target)
        {
            if (value == null || value.GameId != old.GameId || !value.ParticipantResolved ||
                !value.StatisticsAvailable || value.PuuId != target ||
                value.DataSource != old.DataSource ||
                !string.Equals(value.SgpServerId ?? "", old.SgpServerId ?? "", StringComparison.OrdinalIgnoreCase) ||
                (old.ChampionId > 0 && value.ChampionId != old.ChampionId) ||
                (old.GameDurationSeconds > 0 && value.GameDurationSeconds <= 0) ||
                (old.GameCreationEpochMs > 0 && value.GameCreationEpochMs != old.GameCreationEpochMs) ||
                (old.QueueId > 0 && value.QueueId != old.QueueId) ||
                (!string.IsNullOrEmpty(old.GameMode) && value.GameMode != old.GameMode) ||
                (!string.IsNullOrEmpty(old.GameType) && value.GameType != old.GameType) ||
                (old.RosterIdentitiesComplete && !value.RosterIdentitiesComplete)) return false;
            var before = AnalysisValues(old);
            var after = AnalysisValues(value);
            for (var i = 0; i < before.Length; i++)
                if (before[i].HasValue && !after[i].HasValue) return false;
            return LeagueHistoryAkariScore.Calculate(new[] { old }) == null ||
                LeagueHistoryAkariScore.Calculate(new[] { value }) != null;
        }

        private static double?[] AnalysisValues(LeaguePlayerMatchSummary m)
        {
            return new[] { m.SoloKills, m.VisionScore, m.ChampionDamageShare, m.DamageTakenShare,
                m.GoldShare, m.KillParticipation, m.DamageGoldEfficiency, m.GoldEarned, m.GoldSpent,
                m.DamageDealtToChampions, m.DamageTaken, m.TotalHeal, m.TimeCCingOthers };
        }
    }
}
