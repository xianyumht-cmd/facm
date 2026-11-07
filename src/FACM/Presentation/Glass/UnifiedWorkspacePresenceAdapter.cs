using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspacePresenceAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private CancellationTokenSource _cancellation;
        private int _generation;
        private bool _busy;
        private LeaguePresenceSnapshot _snapshot;
        private LeagueClientSession _session;

        internal UnifiedWorkspacePresenceAdapter(Func<bool> canPublish, Action<string> publish)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        }

        internal void Start(LeagueDashboardModule module)
        {
            if (_busy) return;
            Cancel();
            if (module == null) { PostSnapshot(null); return; }
            var source = new CancellationTokenSource();
            _cancellation = source;
            _busy = true;
            ReadAsync(module, source, _generation);
        }

        private async void ReadAsync(LeagueDashboardModule module,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var session = module.UnifiedPresenceSession;
                LeaguePresenceSnapshot snapshot = null;
                if (session != null)
                    snapshot = await module.CreateUnifiedPresenceService().ReadAsync(source.Token);
                if (!Current(module, session, source, generation)) return;
                _session = session;
                _snapshot = snapshot;
                PostSnapshot(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence read skipped: " + exception.GetType().Name);
                if (Current(module, null, source, generation, false)) PostSnapshot(null);
            }
            finally { Finish(source); }
        }

        private bool Current(LeagueDashboardModule module, LeagueClientSession session,
            CancellationTokenSource source, int generation, bool checkSession = true)
        {
            return !source.IsCancellationRequested && generation == _generation &&
                ReferenceEquals(_cancellation, source) && _canPublish() &&
                (!checkSession || (session == null ? module.UnifiedPresenceSession == null :
                    LeagueEncounterReplayService.SessionMatches(session, module.UnifiedPresenceSession)));
        }

        internal void Apply(LeagueDashboardModule module, string kind, string request)
        {
            if (_busy || !_canPublish()) return;
            if (module == null || _session == null || _snapshot == null ||
                !_snapshot.Connected ||
                !LeagueEncounterReplayService.SessionMatches(_session, module.UnifiedPresenceSession))
            {
                PostResult("unavailable");
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
                    { PostResult("invalid"); return; }
                }
                else
                {
                    if (string.IsNullOrEmpty(request) || request.Length > 4096)
                    { PostResult("invalid"); return; }
                    var fields = new JavaScriptSerializer().DeserializeObject(request) as IDictionary<string, object>;
                    if (fields == null) { PostResult("invalid"); return; }
                    object value;
                    if (kind == "signature")
                    {
                        if (fields.Count != 1 || !fields.TryGetValue("message", out value) ||
                            !(value is string) || ((string)value).Length > LeaguePresenceService.MaximumStatusMessageLength)
                        { PostResult("invalid"); return; }
                        signature = (string)value;
                    }
                    else if (kind == "rank")
                    {
                        if (fields.Count != 3 || !fields.TryGetValue("queue", out value) || !(value is string))
                        { PostResult("invalid"); return; }
                        queue = (string)value;
                        if (!fields.TryGetValue("tier", out value) || !(value is string))
                        { PostResult("invalid"); return; }
                        tier = (string)value;
                        if (!fields.TryGetValue("division", out value) || !(value is string))
                        { PostResult("invalid"); return; }
                        division = (string)value;
                        string nq, nt, nd;
                        if (!LeaguePresenceService.TryNormalizeRankedStatusForSmokeTest(queue, tier, division,
                            out nq, out nt, out nd))
                        { PostResult("invalid"); return; }
                        queue = nq; tier = nt; division = nd;
                    }
                    else return;
                }
            }
            catch (ArgumentException) { PostResult("invalid"); return; }
            catch (InvalidOperationException) { PostResult("invalid"); return; }

            var source = new CancellationTokenSource();
            _cancellation = source;
            _busy = true;
            WriteAsync(module, kind, mode, signature, queue, tier, division,
                _snapshot, _session, source, _generation);
        }

        private async void WriteAsync(LeagueDashboardModule module, string kind,
            LeaguePresenceMode mode, string signature, string queue, string tier, string division,
            LeaguePresenceSnapshot expected, LeagueClientSession session,
            CancellationTokenSource source, int generation)
        {
            try
            {
                if (!Current(module, session, source, generation)) return;
                var service = module.CreateUnifiedPresenceService();
                string result;
                if (kind == "mode")
                    result = (await service.ApplyIfAvailabilityUnchangedAsync(mode,
                        expected.Availability, source.Token)).Status;
                else if (kind == "signature")
                    result = (await service.ApplyStatusMessageIfUnchangedAsync(signature,
                        expected.StatusMessage, source.Token)).Status;
                else
                    result = (await service.ApplyRankedStatusIfUnchangedAsync(queue, tier, division,
                        expected, source.Token)).Status;
                if (!Current(module, session, source, generation)) return;
                var snapshot = await service.ReadAsync(source.Token);
                if (!Current(module, session, source, generation)) return;
                _snapshot = snapshot;
                PostSnapshot(snapshot);
                PostResult(result ?? "write-failed");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified presence write skipped: " + exception.GetType().Name);
                if (Current(module, session, source, generation)) PostResult("write-failed");
            }
            finally { Finish(source); }
        }

        private void Finish(CancellationTokenSource source)
        {
            if (ReferenceEquals(_cancellation, source))
            {
                _busy = false;
                _cancellation = null;
            }
            source.Dispose();
        }

        internal void Cancel()
        {
            _generation++;
            _session = null;
            _snapshot = null;
            _busy = false;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private static string SerializeSnapshot(LeaguePresenceSnapshot snapshot)
        {
            return new JavaScriptSerializer().Serialize(new
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
            });
        }

        private void PostSnapshot(LeaguePresenceSnapshot snapshot)
        {
            if (_canPublish()) _publish(SerializeSnapshot(snapshot));
        }

        private void PostResult(string result)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new { type = "presence-result", status = result }));
        }

        internal static void ValidateForSmokeTest()
        {
            var json = SerializeSnapshot(null);
            if (!json.Contains("\"type\":\"presence-state\"") ||
                !json.Contains("\"connected\":false"))
                throw new InvalidOperationException("Presence snapshot contract changed.");
            var hiddenPosts = 0;
            var visiblePosts = 0;
            var hidden = new UnifiedWorkspacePresenceAdapter(() => false, delegate { hiddenPosts++; });
            var visible = new UnifiedWorkspacePresenceAdapter(() => true, delegate { visiblePosts++; });
            hidden.Start(null);
            visible.Start(null);
            hidden.Cancel();
            if (hiddenPosts != 0 || visiblePosts != 1)
                throw new InvalidOperationException("Presence adapters leaked state across owners.");
        }
    }
}
