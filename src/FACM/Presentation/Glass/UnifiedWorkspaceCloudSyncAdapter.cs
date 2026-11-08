using System;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Online;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    // Owns the per-window cloud view and its event subscriptions, not the cloud transport.
    internal sealed class UnifiedWorkspaceCloudSyncAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Action<Action> _onUi;
        private readonly AppSettings _settings;
        private readonly UiTextCatalog _ui;
        private GgmanAutoSyncService _observer;
        private bool _hooked;
        private bool _busy;
        private int _generation;

        internal UnifiedWorkspaceCloudSyncAdapter(Func<bool> canPublish, Action<string> publish,
            Action<Action> onUi, AppSettings settings, UiTextCatalog ui)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        internal void Hook()
        {
            if (!_hooked)
            {
                _hooked = true;
                GgmanAccountSession.Changed += HandleChanged;
            }
            RefreshObserver();
        }

        private void RefreshObserver()
        {
            var current = GgmanAutoSyncService.Current;
            if (ReferenceEquals(current, _observer)) return;
            if (_observer != null) _observer.StateChanged -= HandleChanged;
            _observer = current;
            if (_hooked && _observer != null) _observer.StateChanged += HandleChanged;
        }

        private void HandleChanged(object sender, EventArgs args)
        {
            _onUi(delegate { if (_hooked) { RefreshObserver(); Sync(); } });
        }

        internal void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            _generation++;
            GgmanAccountSession.Changed -= HandleChanged;
            if (_observer != null) _observer.StateChanged -= HandleChanged;
            _observer = null;
        }

        internal void Leave() { _generation++; }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            var status = service == null ? _ui.Get(UiTextKeys.AutoSyncWaiting) : _ui.Get(service.StatusKey);
            if (service != null && !string.IsNullOrWhiteSpace(service.ConflictCategory))
            {
                var category = service.ConflictCategory == "app" ? UiTextKeys.UnifiedSyncSettings :
                    service.ConflictCategory == "text" ? UiTextKeys.UnifiedSyncText : UiTextKeys.UnifiedSyncEsc;
                status = string.Format(status, _ui.Get(category));
            }
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "cloud-state", localOnly = _settings.LocalOnlyMode,
                loggedIn = account != null, account = account == null ? null : account.Email,
                autoSync = _settings.AutoConfigSyncEnabled,
                autoGameRestore = _settings.GameAutoRestoreEnabled,
                status, busy = _busy || (service != null && service.IsBusy),
                available = service != null,
                conflict = service == null ? null : service.ConflictCategory,
                conflictHasRemote = service != null && service.ConflictHasRemote
            }));
        }

        internal void Toggle(string request)
        {
            if (!_canPublish() || _busy) return;
            if (_settings.LocalOnlyMode) { PostResult("local-only"); return; }
            var parts = (request ?? string.Empty).Split(':');
            if (parts.Length != 2 || (parts[1] != "true" && parts[1] != "false"))
            { PostResult("invalid"); return; }
            var service = GgmanAutoSyncService.Current;
            if (service == null || service.IsBusy) { PostResult("busy"); return; }
            try
            {
                if (parts[0] == "config") service.SetEnabled(parts[1] == "true");
                else if (parts[0] == "game") service.SetGameAutoRestoreEnabled(parts[1] == "true");
                else { PostResult("invalid"); return; }
                Sync();
                PostResult("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cloud preference update failed: " + exception.GetType().Name);
                PostResult("failed");
                Sync();
            }
        }

        internal async void PublishGameProfile()
        {
            if (!_canPublish() || _busy) return;
            if (_settings.LocalOnlyMode) { PostResult("local-only"); return; }
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            if (account == null || service == null || service.IsBusy)
            { PostResult("unavailable"); return; }
            _busy = true;
            var generation = _generation;
            Sync();
            try
            {
                var version = await service.PublishGameProfileAsync();
                if (!GgmanAccountSession.IsCurrent(account))
                { PostResultIfCurrent(generation, "context-changed"); return; }
                PostResultIfCurrent(generation, "published", version);
            }
            catch (OperationCanceledException) { PostResultIfCurrent(generation, "context-changed"); }
            catch (Exception exception)
            {
                AppLog.Info("Unified game profile upload failed: " + exception.GetType().Name);
                PostResultIfCurrent(generation, "failed");
            }
            finally { _busy = false; Sync(); }
        }

        internal async void ResolveConflict()
        {
            if (!_canPublish() || _busy) return;
            if (_settings.LocalOnlyMode) { PostResult("local-only"); return; }
            var service = GgmanAutoSyncService.Current;
            var account = GgmanAccountSession.Current;
            if (service == null || account == null || service.IsBusy ||
                string.IsNullOrEmpty(service.ConflictCategory))
            { PostResult("unavailable"); return; }
            if (service.ConflictCategory == "text" && UiTextEditorPanel.HasPendingVisibleChanges)
            { PostResult("unsaved"); return; }
            _busy = true;
            var generation = _generation;
            Sync();
            try
            {
                await service.ResolveConflictAsync(true);
                if (!GgmanAccountSession.IsCurrent(account))
                { PostResultIfCurrent(generation, "context-changed"); return; }
                PostResultIfCurrent(generation,
                    string.IsNullOrEmpty(service.ConflictCategory) ? "resolved" : "failed");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified cloud conflict resolution failed: " + exception.GetType().Name);
                PostResultIfCurrent(generation, "failed");
            }
            finally { _busy = false; Sync(); }
        }

        private void PostResultIfCurrent(int generation, string status, long version = 0)
        {
            if (generation == _generation) PostResult(status, version);
        }

        private void PostResult(string status, long version = 0)
        {
            if (_canPublish())
                _publish(new JavaScriptSerializer().Serialize(new { type = "cloud-result", status, version }));
        }

        internal static void ValidateForSmokeTest()
        {
            var posts = 0;
            var adapter = new UnifiedWorkspaceCloudSyncAdapter(() => false,
                delegate { posts++; }, delegate(Action work) { work(); },
                new AppSettings(), UiTextCatalog.Load());
            adapter.Toggle("config:true");
            adapter.PublishGameProfile();
            adapter.ResolveConflict();
            adapter.Leave();
            if (posts != 0 || adapter._generation != 1)
                throw new InvalidOperationException("Cloud adapter route and generation isolation changed.");
        }
    }
}
