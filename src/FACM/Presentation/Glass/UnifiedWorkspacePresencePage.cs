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
        internal void OpenPresencePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Presence);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            if (_ready) StartPresenceRead();
        }

        internal void StartPresenceRead()
        {
            if (_presenceBusy) return;
            CancelPresenceOperation();
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null) { PostPresenceSnapshot(null); return; }
            var cancellation = new CancellationTokenSource();
            _presenceCancellation = cancellation;
            _presenceBusy = true;
            ReadPresenceAsync(module, cancellation, _presenceGeneration);
        }

        private async void ReadPresenceAsync(LeagueDashboardModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var session = module.UnifiedPresenceSession;
                LeaguePresenceSnapshot snapshot = null;
                if (session != null)
                    snapshot = await module.CreateUnifiedPresenceService().ReadAsync(cancellation.Token);
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                _presenceSession = session;
                _presenceSnapshot = snapshot;
                PostPresenceSnapshot(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence read skipped: " + exception.GetType().Name);
                if (PresenceRequestIsCurrent(module, null, cancellation, generation, false))
                    PostPresenceSnapshot(null);
            }
            finally { FinishPresenceOperation(cancellation); }
        }

        private bool PresenceRequestIsCurrent(LeagueDashboardModule module, LeagueClientSession session,
            CancellationTokenSource cancellation, int generation, bool checkSession = true)
        {
            return !IsDisposed && !_closing && !cancellation.IsCancellationRequested &&
                generation == _presenceGeneration &&
                ReferenceEquals(_presenceCancellation, cancellation) &&
                _navigation.Current == LeagueHubNavigation.Presence &&
                _ready && _browser.CoreWebView2 != null &&
                (!checkSession || (session == null ? module.UnifiedPresenceSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession)));
        }

        internal void ApplyPresenceChange(string kind, string request)
        {
            if (_presenceBusy || _navigation.Current != LeagueHubNavigation.Presence) return;
            var module = LeagueHubUiBridge.UnifiedDashboard;
            if (module == null || _presenceSession == null || _presenceSnapshot == null ||
                !_presenceSnapshot.Connected ||
                !LeagueEncounterReplayService.SessionMatches(_presenceSession, module.UnifiedPresenceSession))
            {
                PostPresenceResult("unavailable");
                return;
            }
            LeaguePresenceMode mode = LeaguePresenceMode.Online;
            string signature = null, queue = null, tier = null, division = null;
            try
            {
                if (kind == "mode")
                {
                    if (!new[] { "Online", "Away", "DoNotDisturb", "Mobile", "Offline", "DisplayInGame" }
                        .Contains(request) || !Enum.TryParse(request, out mode))
                    { PostPresenceResult("invalid"); return; }
                }
                else
                {
                    if (string.IsNullOrEmpty(request) || request.Length > 4096)
                    { PostPresenceResult("invalid"); return; }
                    var fields = new JavaScriptSerializer().DeserializeObject(request) as IDictionary<string, object>;
                    if (fields == null) { PostPresenceResult("invalid"); return; }
                    object value;
                    if (kind == "signature")
                    {
                        if (fields.Count != 1 || !fields.TryGetValue("message", out value) ||
                            !(value is string) || ((string)value).Length > LeaguePresenceService.MaximumStatusMessageLength)
                        { PostPresenceResult("invalid"); return; }
                        signature = (string)value;
                    }
                    else if (kind == "rank")
                    {
                        if (fields.Count != 3 || !fields.TryGetValue("queue", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        queue = (string)value;
                        if (!fields.TryGetValue("tier", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        tier = (string)value;
                        if (!fields.TryGetValue("division", out value) || !(value is string))
                        { PostPresenceResult("invalid"); return; }
                        division = (string)value;
                        string nq, nt, nd;
                        if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(queue, tier, division,
                            out nq, out nt, out nd))
                        { PostPresenceResult("invalid"); return; }
                        queue = nq; tier = nt; division = nd;
                    }
                    else return;
                }
            }
            catch (ArgumentException) { PostPresenceResult("invalid"); return; }
            catch (InvalidOperationException) { PostPresenceResult("invalid"); return; }

            var cancellation = new CancellationTokenSource();
            _presenceCancellation = cancellation;
            _presenceBusy = true;
            WritePresenceAsync(module, kind, mode, signature, queue, tier, division,
                _presenceSnapshot, _presenceSession, cancellation, _presenceGeneration);
        }

        private async void WritePresenceAsync(LeagueDashboardModule module, string kind,
            LeaguePresenceMode mode, string signature, string queue, string tier, string division,
            LeaguePresenceSnapshot expected, LeagueClientSession session,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                var service = module.CreateUnifiedPresenceService();
                string result;
                if (kind == "mode")
                    result = (await service.ApplyIfAvailabilityUnchangedAsync(mode,
                        expected.Availability, cancellation.Token)).Status;
                else if (kind == "signature")
                    result = (await service.ApplyStatusMessageIfUnchangedAsync(signature,
                        expected.StatusMessage, cancellation.Token)).Status;
                else
                    result = (await service.ApplyRankedStatusIfUnchangedAsync(queue, tier, division,
                        expected, cancellation.Token)).Status;
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                var snapshot = await service.ReadAsync(cancellation.Token);
                if (!PresenceRequestIsCurrent(module, session, cancellation, generation)) return;
                _presenceSnapshot = snapshot;
                PostPresenceSnapshot(snapshot);
                PostPresenceResult(result ?? "write-failed");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence write skipped: " + exception.GetType().Name);
                if (PresenceRequestIsCurrent(module, session, cancellation, generation))
                    PostPresenceResult("write-failed");
            }
            finally { FinishPresenceOperation(cancellation); }
        }

        private void FinishPresenceOperation(CancellationTokenSource cancellation)
        {
            if (ReferenceEquals(_presenceCancellation, cancellation))
            {
                _presenceBusy = false;
                _presenceCancellation = null;
            }
            cancellation.Dispose();
        }

        internal void CancelPresenceOperation()
        {
            _presenceGeneration++;
            _presenceSession = null;
            _presenceSnapshot = null;
            _presenceBusy = false;
            var cancellation = _presenceCancellation;
            _presenceCancellation = null;
            if (cancellation != null && !cancellation.IsCancellationRequested) cancellation.Cancel();
        }

        private void PostPresenceSnapshot(LeaguePresenceSnapshot snapshot)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "presence-state",
                connected = snapshot != null && snapshot.Connected,
                name = snapshot == null ? null : snapshot.DisplayName,
                availability = snapshot == null ? null : snapshot.Availability,
                gameStatus = snapshot == null ? null : snapshot.GameStatus,
                message = snapshot == null ? null : snapshot.StatusMessage,
                queue = snapshot == null ? null : snapshot.RankedLeagueQueue,
                tier = snapshot == null ? null : snapshot.RankedLeagueTier,
                division = snapshot == null ? null : snapshot.RankedLeagueDivision
            }));
        }

        private void PostPresenceResult(string result)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "presence-result", status = result
            }));
        }


    }
}
