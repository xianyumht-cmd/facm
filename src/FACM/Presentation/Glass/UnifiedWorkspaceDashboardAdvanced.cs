using System;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _advancedRespawnCancellation;
        private int _advancedRespawnGeneration;

        private void OpenAdvancedDashboardPage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("dashboard-advanced");
            _content.Visible = false;
            _browser.Dock = System.Windows.Forms.DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartDashboardRead();
        }

        private void StartAdvancedRespawnRead()
        {
            if (_navigation.Current != "dashboard-advanced" || _closing) return;
            CancelAdvancedRespawnRead();
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null) { PostAdvancedRespawn("unavailable", 0); return; }
            var source = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _advancedRespawnCancellation = source;
            var generation = _advancedRespawnGeneration;
            ReadAdvancedRespawnAsync(module, source, generation);
        }

        private async void ReadAdvancedRespawnAsync(LeagueDashboardModule module,
            CancellationTokenSource source, int generation)
        {
            try
            {
                if (module.CurrentGameflowState == null ||
                    !module.CurrentGameflowState.Connected ||
                    !string.Equals(module.CurrentGameflowState.Phase, "InProgress",
                        StringComparison.OrdinalIgnoreCase))
                {
                    PostAdvancedRespawn("unavailable", 0); return;
                }
                var accountSession = module.UnifiedPresenceSession;
                if (accountSession == null)
                {
                    PostAdvancedRespawn("unavailable", 0); return;
                }
                var result = await module.ReadUnifiedRespawnAsync(source.Token);
                if (!RespawnIsCurrent(module, accountSession, source, generation))
                {
                    if (RespawnIsCurrent(module, null, source, generation, false))
                        PostAdvancedRespawn("session-changed", 0);
                    return;
                }
                PostAdvancedRespawn(result == null ? "unavailable" :
                    result.IsDead ? "dead" : "alive",
                    result == null ? 0 : result.TimeLeftSeconds);
            }
            catch (OperationCanceledException)
            {
                if (RespawnIsCurrent(module, null, source, generation, false))
                    PostAdvancedRespawn("unavailable", 0);
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified respawn read skipped: " + exception.GetType().Name);
                if (RespawnIsCurrent(module, null, source, generation, false))
                    PostAdvancedRespawn("failed", 0);
            }
            finally
            {
                if (ReferenceEquals(_advancedRespawnCancellation, source))
                    _advancedRespawnCancellation = null;
                source.Dispose();
            }
        }

        private bool RespawnIsCurrent(LeagueDashboardModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && _ready && _browser.CoreWebView2 != null &&
                _navigation.Current == "dashboard-advanced" &&
                generation == _advancedRespawnGeneration &&
                ReferenceEquals(source, _advancedRespawnCancellation) &&
                (!checkSession || (session == null ? module.UnifiedPresenceSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession)));
        }

        private void PostAdvancedRespawn(string status, double seconds)
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "dashboard-advanced") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "advanced-respawn", status, seconds
            }));
        }

        private void CancelAdvancedRespawnRead()
        {
            _advancedRespawnGeneration++;
            var source = _advancedRespawnCancellation;
            _advancedRespawnCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }
    }
}
