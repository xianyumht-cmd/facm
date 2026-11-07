using System;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceHistoryAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Func<bool> _isAdvanced;
        private readonly Action<string> _publish;
        private readonly Action _resetAdvanced;
        private readonly Action _syncAdvanced;
        private CancellationTokenSource _cancellation;
        private int _generation;
        internal LeagueUnifiedHistorySnapshot Snapshot { get; private set; }
        internal LeagueClientSession Session { get; private set; }

        internal UnifiedWorkspaceHistoryAdapter(Func<bool> canPublish, Func<bool> isAdvanced,
            Action<string> publish, Action resetAdvanced, Action syncAdvanced)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _isAdvanced = isAdvanced ?? throw new ArgumentNullException(nameof(isAdvanced));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _resetAdvanced = resetAdvanced ?? throw new ArgumentNullException(nameof(resetAdvanced));
            _syncAdvanced = syncAdvanced ?? throw new ArgumentNullException(nameof(syncAdvanced));
        }

        internal void Start(LeaguePlayerModule module, int count, bool force)
        {
            Cancel();
            if (module == null) { PostError(); return; }
            var source = new CancellationTokenSource();
            _cancellation = source;
            _resetAdvanced();
            ReadAsync(module, count, force, source, _generation);
        }

        private async void ReadAsync(LeaguePlayerModule module, int count, bool force,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedHistorySession;
                var result = await module.ReadUnifiedHistoryAsync(count, force, source.Token);
                if (!Current(module, session, source, generation))
                {
                    if (Current(module, null, source, generation, false)) PostError();
                    return;
                }
                if (result == null || result.Profile == null) { PostError(); return; }
                Snapshot = result;
                Session = session;
                if (_isAdvanced()) _syncAdvanced();
                _publish(SerializeHistory(result, count));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified recent history read skipped: " + exception.GetType().Name);
                if (Current(module, null, source, generation, false)) PostError();
            }
            finally
            {
                if (ReferenceEquals(_cancellation, source)) _cancellation = null;
                source.Dispose();
            }
        }

        private static string SerializeHistory(LeagueUnifiedHistorySnapshot result, int count)
        {
            var page = result.Page;
            return new JavaScriptSerializer().Serialize(new
            {
                type = "history-details",
                name = result.Profile.AccountName,
                level = result.Profile.SummonerLevel,
                count = page == null ? 0 : page.Matches.Count,
                hasMore = page != null && page.HasMore && count < 20,
                rows = page == null ? new object[0] : page.Matches.Take(20).Select(match => new
                {
                    gameId = match.GameId,
                    played = match.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                    champion = match.ChampionName,
                    championId = match.ChampionId,
                    mode = match.GameMode,
                    queueId = match.QueueId,
                    outcome = match.StatisticsAvailable ? (match.Win ? "win" : "loss") : "unknown",
                    kills = match.Kills,
                    deaths = match.Deaths,
                    assists = match.Assists,
                    duration = match.GameDurationSeconds
                }).ToArray()
            });
        }

        private bool Current(LeaguePlayerModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !source.IsCancellationRequested && generation == _generation &&
                ReferenceEquals(_cancellation, source) && _canPublish() &&
                (!checkSession || (session == null ? module.UnifiedHistorySession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedHistorySession)));
        }

        private void PostError()
        {
            if (!_canPublish()) return;
            _publish("{\"type\":\"history-failed\"}");
            if (_isAdvanced())
                _publish("{\"type\":\"history-advanced-list\",\"error\":true,\"rows\":[]}");
        }

        internal void Cancel()
        {
            _generation++;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal void ClearSnapshot()
        {
            Snapshot = null;
            Session = null;
        }

        internal bool HasAdvancedPage(LeaguePlayerModule module)
        {
            return module != null && Session != null && Snapshot != null &&
                Snapshot.Page != null && Snapshot.Page.Matches.Count >= 20 &&
                LeagueEncounterReplayService.SessionMatches(Session, module.UnifiedHistorySession);
        }

        internal void SyncAdvancedList(LeaguePlayerModule module)
        {
            if (!_canPublish() || !_isAdvanced()) return;
            var loaded = Snapshot;
            if (loaded == null || loaded.Profile == null || loaded.Page == null ||
                module == null || !LeagueEncounterReplayService.SessionMatches(
                    Session, module.UnifiedHistorySession))
            {
                _publish("{\"type\":\"history-advanced-list\",\"error\":true,\"rows\":[]}");
                return;
            }
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "history-advanced-list",
                name = loaded.Profile.AccountName,
                hasMore = loaded.Page.HasMore,
                rows = loaded.Page.Matches.Take(20).Select(m => new
                {
                    id = m.GameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    played = m.GameCreationLocal.ToString("yyyy-MM-dd HH:mm"),
                    champion = m.ChampionName,
                    championId = m.ChampionId,
                    queue = m.QueueId,
                    mode = m.GameMode,
                    outcome = m.StatisticsAvailable ? m.Win ? "win" : "loss" : "unknown",
                    kills = m.Kills,
                    deaths = m.Deaths,
                    assists = m.Assists
                }).ToArray()
            }));
        }

        internal static void ValidateForSmokeTest()
        {
            var firstPosts = 0;
            var secondPosts = 0;
            var first = new UnifiedWorkspaceHistoryAdapter(() => false, () => false,
                delegate { firstPosts++; }, () => { }, () => { });
            var second = new UnifiedWorkspaceHistoryAdapter(() => true, () => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"history-"))
                        throw new InvalidOperationException("History error contract changed.");
                    secondPosts++;
                }, () => { }, () => { });
            first.Start(null, 10, false);
            second.Start(null, 20, true);
            second.SyncAdvancedList(null);
            first.Cancel();
            second.ClearSnapshot();
            if (firstPosts != 0 || secondPosts != 3 ||
                second.Snapshot != null || second.Session != null || second.HasAdvancedPage(null))
                throw new InvalidOperationException("History adapters leaked state across owners.");
        }
    }
}
