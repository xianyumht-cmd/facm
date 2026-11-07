using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;
using FACM.Theming;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        internal void OpenDashboardPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Dashboard);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartDashboardRead();
        }

        internal void StartDashboardRead()
        {
            CancelDashboardRead();
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null) return;
            var cancellation = new CancellationTokenSource();
            _dashboardReadCancellation = cancellation;
            var generation = _dashboardReadGeneration;
            LoadDashboardAsync(module, cancellation, generation);
        }

        private async void LoadDashboardAsync(LeagueDashboardModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var snapshot = await module.ReadUnifiedDashboardAsync(cancellation.Token);
                if (IsDisposed || cancellation.IsCancellationRequested ||
                    generation != _dashboardReadGeneration ||
                    (_navigation.Current != LeagueHubNavigation.Dashboard &&
                     _navigation.Current != "dashboard-advanced") ||
                    _browser.CoreWebView2 == null) return;
                if (snapshot == null)
                {
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"dashboard-failed\"}");
                    return;
                }
                var payload = new JavaScriptSerializer().Serialize(new
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
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified League dashboard read skipped: " + exception.GetType().Name);
                if (!IsDisposed && generation == _dashboardReadGeneration &&
                    _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"dashboard-failed\"}");
            }
            finally
            {
                if (ReferenceEquals(_dashboardReadCancellation, cancellation))
                    _dashboardReadCancellation = null;
                cancellation.Dispose();
            }
        }

        internal void CancelDashboardRead()
        {
            _dashboardReadGeneration++;
            var previous = _dashboardReadCancellation;
            _dashboardReadCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }




    }
}
