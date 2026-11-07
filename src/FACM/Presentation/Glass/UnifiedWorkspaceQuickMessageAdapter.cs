using System;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceQuickMessageAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueEfficiencyModule> _owner;
        private CancellationTokenSource _cancellation;
        private int _generation;

        internal UnifiedWorkspaceQuickMessageAdapter(Func<bool> canPublish,
            Action<string> publish, Func<LeagueEfficiencyModule> owner)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Sync()
        {
            if (!_canPublish()) return;
            var module = _owner();
            if (module == null) { PostResult("unavailable"); return; }
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "quick-message-settings",
                presets = LeagueQuickMessagePresets.Parse(module.QuickMessagePresetsJson),
                hotkeys = LeagueQuickMessageHotkeys.Parse(module.QuickMessageHotkeysJson),
                intervalMs = module.QuickMessageIntervalMs
            }));
        }

        internal void Save(string raw)
        {
            if (!_canPublish()) return;
            UnifiedQuickMessageChange change;
            if (!UnifiedWorkspaceQuickMessageContract.TryParse(raw, out change))
            { PostResult("invalid"); return; }
            var module = _owner();
            if (module == null) { PostResult("unavailable"); return; }
            try
            {
                string error;
                if (!module.TrySaveQuickMessageSettings(change.Presets, change.Hotkeys,
                    change.IntervalMs, out error))
                {
                    AppLog.Info("Quick message binding rejected by native hotkey owner.");
                    PostResult("hotkey-conflict"); return;
                }
                PostResult("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick message save failed: " + exception.GetType().Name);
                PostResult("failed");
            }
        }

        internal async void Send(int index)
        {
            if (!_canPublish() || index < 0 || index >= LeagueQuickMessagePresets.SlotCount ||
                _cancellation != null) return;
            var module = _owner();
            if (module == null) { PostResult("unavailable"); return; }
            var messages = LeagueQuickMessagePresets.Parse(module.QuickMessagePresetsJson);
            if (string.IsNullOrWhiteSpace(messages[index]))
            { PostResult("unsaved"); return; }
            var source = new CancellationTokenSource();
            _cancellation = source;
            var generation = _generation;
            PostResult("sending");
            try
            {
                var sent = await module.SendQuickMessageAsync(messages[index], source.Token);
                if (Current(module, source, generation))
                    PostResult(sent ? "sent" : "unavailable");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick message send skipped: " + exception.GetType().Name);
                if (Current(module, source, generation)) PostResult("failed");
            }
            finally
            {
                if (ReferenceEquals(_cancellation, source)) _cancellation = null;
                source.Dispose();
            }
        }

        private bool Current(LeagueEfficiencyModule module, CancellationTokenSource source, int generation)
        {
            return _canPublish() && !source.IsCancellationRequested &&
                generation == _generation && ReferenceEquals(source, _cancellation) &&
                ReferenceEquals(module, _owner());
        }

        private void PostResult(string status)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new
            { type = "quick-message-result", status }));
        }

        internal void Cancel()
        {
            _generation++;
            var source = _cancellation;
            _cancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal static void ValidateForSmokeTest()
        {
            var messages = 0;
            var hidden = new UnifiedWorkspaceQuickMessageAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden quick message published."); }, () => null);
            var visible = new UnifiedWorkspaceQuickMessageAdapter(() => true,
                delegate(string json)
                {
                    if (!json.Contains("\"type\":\"quick-message-result\""))
                        throw new InvalidOperationException("Quick message status contract changed.");
                    messages++;
                }, () => null);
            hidden.Sync();
            visible.Sync();
            visible.Send(0);
            hidden.Cancel();
            visible.Cancel();
            if (messages != 2 || hidden._generation != 1 || visible._generation != 1)
                throw new InvalidOperationException("Quick message adapter isolation failed.");
        }
    }
}
