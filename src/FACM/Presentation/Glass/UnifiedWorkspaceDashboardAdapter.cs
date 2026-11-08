using System;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Per-workspace UI request owner. Native gameflow and LCU state stay with LeagueDashboardModule.
    internal sealed class UnifiedWorkspaceDashboardAdapter
    {
        private readonly Func<bool> _canPublishDashboard;
        private readonly Func<bool> _canPublishRespawn;
        private readonly Action<string> _publish;
        private CancellationTokenSource _dashboardCancellation;
        private CancellationTokenSource _respawnCancellation;
        private int _dashboardGeneration;
        private int _respawnGeneration;

        internal UnifiedWorkspaceDashboardAdapter(Func<bool> canPublishDashboard,
            Func<bool> canPublishRespawn, Action<string> publish)
        {
            _canPublishDashboard = canPublishDashboard ?? throw new ArgumentNullException(nameof(canPublishDashboard));
            _canPublishRespawn = canPublishRespawn ?? throw new ArgumentNullException(nameof(canPublishRespawn));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void StartDashboard(LeagueDashboardModule module)
        {
            CancelDashboard();
            if (module == null) return;
            var source = new CancellationTokenSource();
            _dashboardCancellation = source;
            ReadDashboardAsync(module, source, _dashboardGeneration);
        }

        private async void ReadDashboardAsync(LeagueDashboardModule module,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedPresenceSession;
                var snapshot = await module.ReadUnifiedDashboardAsync(source.Token);
                if (!DashboardCurrent(source, generation)) return;
                if (session == null ? module.UnifiedPresenceSession != null :
                    !LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession))
                {
                    _publish("{\"type\":\"dashboard-failed\"}");
                    return;
                }
                if (snapshot == null)
                {
                    _publish("{\"type\":\"dashboard-failed\"}");
                    return;
                }
                _publish(SerializeDashboard(snapshot));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified League dashboard read skipped: " + exception.GetType().Name);
                if (DashboardCurrent(source, generation))
                    _publish("{\"type\":\"dashboard-failed\"}");
            }
            finally
            {
                if (ReferenceEquals(_dashboardCancellation, source)) _dashboardCancellation = null;
                source.Dispose();
            }
        }

        private bool DashboardCurrent(CancellationTokenSource source, int generation)
        {
            return !source.IsCancellationRequested && generation == _dashboardGeneration &&
                ReferenceEquals(_dashboardCancellation, source) && _canPublishDashboard();
        }

        private static string SerializeDashboard(LeagueDashboardSnapshot snapshot)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "dashboard-details",
                connected = snapshot.Connected,
                clientProcess = snapshot.ClientProcessDetected,
                phase = snapshot.Phase,
                account = snapshot.AccountName,
                server = !string.IsNullOrWhiteSpace(snapshot.PlatformName)
                    ? snapshot.PlatformName : snapshot.PlatformId,
                level = snapshot.SummonerLevel,
                budget = snapshot.BudgetName,
                gameProcess = snapshot.GameProcessDetected,
                platformId = snapshot.PlatformId,
                platformName = snapshot.PlatformName,
                profileIconId = snapshot.ProfileIconId,
                activity = snapshot.Activity.ToString(),
                observedAt = snapshot.UpdatedAtUtc == DateTime.MinValue ? null
                    : snapshot.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                ranked = snapshot.IsRankedQueue
            });
        }

        internal void CancelDashboard()
        {
            _dashboardGeneration++;
            var previous = _dashboardCancellation;
            _dashboardCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        internal void StartRespawn(LeagueDashboardModule module)
        {
            CancelRespawn();
            if (module == null) { PublishRespawn("unavailable", 0); return; }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _respawnCancellation = source;
            ReadRespawnAsync(module, source, _respawnGeneration);
        }

        private async void ReadRespawnAsync(LeagueDashboardModule module,
            CancellationTokenSource source, int generation)
        {
            try
            {
                if (module.CurrentGameflowState == null ||
                    !module.CurrentGameflowState.Connected ||
                    !string.Equals(module.CurrentGameflowState.Phase, "InProgress",
                        StringComparison.OrdinalIgnoreCase))
                {
                    PublishRespawn("unavailable", 0); return;
                }
                var session = module.UnifiedPresenceSession;
                if (session == null)
                {
                    PublishRespawn("unavailable", 0); return;
                }
                var result = await module.ReadUnifiedRespawnAsync(source.Token);
                if (!RespawnCurrent(module, session, source, generation))
                {
                    if (RespawnCurrent(module, null, source, generation, false))
                        PublishRespawn("session-changed", 0);
                    return;
                }
                PublishRespawn(result == null ? "unavailable" :
                    result.IsDead ? "dead" : "alive",
                    result == null ? 0 : result.TimeLeftSeconds);
            }
            catch (OperationCanceledException)
            {
                if (RespawnCurrent(module, null, source, generation, false))
                    PublishRespawn("unavailable", 0);
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified respawn read skipped: " + exception.GetType().Name);
                if (RespawnCurrent(module, null, source, generation, false))
                    PublishRespawn("failed", 0);
            }
            finally
            {
                if (ReferenceEquals(_respawnCancellation, source)) _respawnCancellation = null;
                source.Dispose();
            }
        }

        private bool RespawnCurrent(LeagueDashboardModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return generation == _respawnGeneration &&
                ReferenceEquals(_respawnCancellation, source) && _canPublishRespawn() &&
                (!checkSession || (session == null ? module.UnifiedPresenceSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession)));
        }

        private void PublishRespawn(string status, double seconds)
        {
            if (!_canPublishRespawn()) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "advanced-respawn", status, seconds
            }));
        }

        internal void CancelRespawn()
        {
            _respawnGeneration++;
            var previous = _respawnCancellation;
            _respawnCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var snapshot = new LeagueDashboardSnapshot
            {
                GameName = "ShardFixture", TagLine = "1234", PlatformId = "HN1",
                PlatformName = "Tencent CN", SummonerLevel = 42,
                Connected = true, IsRankedQueue = true
            };
            var json = SerializeDashboard(snapshot);
            if (!json.Contains("\"type\":\"dashboard-details\"") ||
                !json.Contains("\"account\":\"ShardFixture#1234\"") ||
                !json.Contains("\"server\":\"Tencent CN\"") ||
                !json.Contains("\"level\":42") ||
                !json.Contains("\"ranked\":true"))
                throw new InvalidOperationException("Dashboard shard JSON contract changed.");
            var firstPosts = 0;
            var secondPosts = 0;
            var first = new UnifiedWorkspaceDashboardAdapter(() => false, () => false,
                delegate { firstPosts++; });
            var second = new UnifiedWorkspaceDashboardAdapter(() => true, () => true,
                delegate(string message)
                {
                    if (!message.Contains("\"type\":\"advanced-respawn\""))
                        throw new InvalidOperationException("Advanced respawn contract changed.");
                    secondPosts++;
                });
            first.StartRespawn(null);
            second.StartRespawn(null);
            first.CancelDashboard();
            second.CancelRespawn();
            if (firstPosts != 0 || secondPosts != 1)
                throw new InvalidOperationException("Dashboard request adapters leaked state across owners.");
        }
    }
}
