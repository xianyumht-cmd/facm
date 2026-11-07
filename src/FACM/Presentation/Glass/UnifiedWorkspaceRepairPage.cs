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
        private CancellationTokenSource _repairCancellation;
        private bool _repairBusy;

        internal void OpenRepairPage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Repair);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncRepairState();
        }

        internal void SyncRepairState()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var service = LeagueHubUiBridge.UnifiedRepair;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-state",
                available = service != null,
                auto = service != null && service.AutoRepairEnabled
            }));
        }

        internal void ApplyRepairAction(string action)
        {
            if (_navigation.Current != LeagueHubNavigation.Repair || _repairBusy) return;
            var service = LeagueHubUiBridge.UnifiedRepair;
            if (service == null) { PostRepairResult("unavailable", null); return; }
            if (action == "auto")
            {
                try
                {
                    var updated = service.SetAutoRepairEnabled(!service.AutoRepairEnabled);
                    PostRepairResult(updated != null && updated.Success ? "success" : "failed",
                        updated == null ? null : updated.Message);
                    SyncRepairState();
                }
                catch (Exception exception)
                {
                    AppLog.Info("Unified auto-repair switch failed: " + exception.GetType().Name);
                    PostRepairResult("failed", null);
                }
                return;
            }
            if (action != "window" && action != "settlement" && action != "restart" && action != "exit")
                return;
            var cancellation = new CancellationTokenSource();
            _repairCancellation = cancellation;
            _repairBusy = true;
            RunRepairActionAsync(action, service, cancellation);
        }

        private async void RunRepairActionAsync(string action,
            LeagueGameRepairService service, CancellationTokenSource cancellation)
        {
            try
            {
                if (cancellation.IsCancellationRequested) return;
                if (action == "exit")
                {
                    var efficiency = LeagueHubUiBridge.UnifiedEfficiency;
                    var result = efficiency == null ? null : await efficiency.RunExitGameAsync();
                    if (IsCurrentRepairOperation(cancellation))
                        PostRepairResult(result == null ? "failed" : result.Status, null);
                }
                else
                {
                    LeagueGameRepairResult result;
                    if (action == "window") result = await service.RepairWindowAsync(cancellation.Token);
                    else if (action == "settlement") result = await service.SkipSettlementAsync(cancellation.Token);
                    else result = await service.RestartClientUxAsync(cancellation.Token);
                    if (IsCurrentRepairOperation(cancellation))
                        PostRepairResult(result != null && result.Success ? "success" : "failed",
                            result == null ? null : result.Message);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified repair action failed: " + exception.GetType().Name);
                if (IsCurrentRepairOperation(cancellation)) PostRepairResult("failed", null);
            }
            finally
            {
                if (ReferenceEquals(_repairCancellation, cancellation))
                {
                    _repairCancellation = null;
                    _repairBusy = false;
                }
                cancellation.Dispose();
            }
        }

        private bool IsCurrentRepairOperation(CancellationTokenSource cancellation)
        {
            return !IsDisposed && !_closing && !cancellation.IsCancellationRequested &&
                ReferenceEquals(_repairCancellation, cancellation) &&
                _navigation.Current == LeagueHubNavigation.Repair &&
                _ready && _browser.CoreWebView2 != null;
        }

        internal void CancelRepairOperation()
        {
            _repairBusy = false;
            var cancellation = _repairCancellation;
            _repairCancellation = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }

        private void PostRepairResult(string status, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-result", status = status, message = message
            }));
        }

    }
}
