using System;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.AppHost.Modules;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Per-window repair action owner. The native repair and exit services retain their operations.
    internal sealed class UnifiedWorkspaceRepairAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueGameRepairService> _service;
        private readonly Func<LeagueEfficiencyModule> _efficiency;
        private CancellationTokenSource _repairCancellation;
        private LeagueGameRepairService _repairServiceOwner;
        private bool _repairBusy;

        internal UnifiedWorkspaceRepairAdapter(Func<bool> canPublish, Action<string> publish,
            Func<LeagueGameRepairService> service, Func<LeagueEfficiencyModule> efficiency)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _efficiency = efficiency ?? throw new ArgumentNullException(nameof(efficiency));
        }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var service = _service();
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-state",
                available = service != null,
                auto = service != null && service.AutoRepairEnabled
            }));
        }

        internal void Apply(string action)
        {
            if (!_canPublish() || _repairBusy) return;
            var service = _service();
            if (service == null) { PostRepairResult("unavailable", null); return; }
            if (action == "auto")
            {
                try
                {
                    var updated = service.SetAutoRepairEnabled(!service.AutoRepairEnabled);
                    PostRepairResult(updated != null && updated.Success ? "success" : "failed",
                        updated == null ? null : updated.Message);
                    Sync();
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
            _repairServiceOwner = service;
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
                    var efficiency = _efficiency();
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
                    _repairServiceOwner = null;
                    _repairBusy = false;
                }
                cancellation.Dispose();
            }
        }

        private bool IsCurrentRepairOperation(CancellationTokenSource cancellation)
        {
            return _canPublish() && !cancellation.IsCancellationRequested &&
                ReferenceEquals(_repairCancellation, cancellation) &&
                ReferenceEquals(_service(), _repairServiceOwner);
        }

        internal void Cancel()
        {
            _repairBusy = false;
            _repairServiceOwner = null;
            var cancellation = _repairCancellation;
            _repairCancellation = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }

        private void PostRepairResult(string status, string message)
        {
            if (!_canPublish()) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "repair-result", status = status, message = message
            }));
        }

        internal static void ValidateForSmokeTest()
        {
            var posted = 0;
            var adapter = new UnifiedWorkspaceRepairAdapter(() => true,
                delegate(string json) { if (json.Contains("repair-state")) posted++; },
                () => null, () => null);
            adapter.Sync();
            adapter.Cancel();
            var inactive = new UnifiedWorkspaceRepairAdapter(() => false,
                delegate { throw new InvalidOperationException("Inactive repair adapter published."); },
                () => null, () => null);
            inactive.Sync();
            if (posted != 1 || adapter._repairBusy || inactive._repairBusy)
                throw new InvalidOperationException("Repair adapter route isolation changed.");
        }

    }
}
