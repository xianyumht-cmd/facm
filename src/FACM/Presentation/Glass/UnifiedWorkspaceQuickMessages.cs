using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedQuickMessageChange
    {
        internal string Presets, Hotkeys;
        internal int IntervalMs;
    }

    internal static class UnifiedWorkspaceQuickMessageContract
    {
        internal static bool TryParse(string json, out UnifiedQuickMessageChange result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4000) return false;
            try
            {
                var fields = new JavaScriptSerializer { MaxJsonLength = 4000 }
                    .Deserialize<Dictionary<string, object>>(json);
                if (fields == null || fields.Count != 3) return false;
                object rawPresets, rawHotkeys, rawInterval;
                if (!fields.TryGetValue("presets", out rawPresets) ||
                    !fields.TryGetValue("hotkeys", out rawHotkeys) ||
                    !fields.TryGetValue("intervalMs", out rawInterval)) return false;
                var presets = rawPresets as IList;
                var hotkeys = rawHotkeys as IList;
                if (presets == null || presets.Count != LeagueQuickMessagePresets.SlotCount ||
                    hotkeys == null || hotkeys.Count != LeagueQuickMessageHotkeys.Count ||
                    rawInterval is bool || rawInterval is string || rawInterval == null) return false;
                int interval;
                if (!int.TryParse(Convert.ToString(rawInterval, CultureInfo.InvariantCulture),
                    NumberStyles.None, CultureInfo.InvariantCulture, out interval) ||
                    interval < 0 || interval > 5000) return false;
                var normalizedPresets = new string[presets.Count];
                for (var i = 0; i < presets.Count; i++)
                {
                    var original = presets[i] as string;
                    if (original == null || original.Length > 600) return false;
                    var normalized = LeagueQuickMessagePresets.NormalizeMessage(original);
                    if (!string.IsNullOrWhiteSpace(original) && normalized.Length == 0) return false;
                    normalizedPresets[i] = normalized;
                }
                var normalizedHotkeys = new string[hotkeys.Count];
                for (var i = 0; i < hotkeys.Count; i++)
                {
                    var original = hotkeys[i] as string;
                    if (original == null || original.Length > 60) return false;
                    LeagueHotkeyBinding binding;
                    string error;
                    if (!LeagueHotkeyBinding.TryParse(original, out binding, out error)) return false;
                    normalizedHotkeys[i] = binding.ToString();
                }
                result = new UnifiedQuickMessageChange
                {
                    Presets = LeagueQuickMessagePresets.Save(normalizedPresets),
                    Hotkeys = LeagueQuickMessageHotkeys.Save(normalizedHotkeys),
                    IntervalMs = interval
                };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        internal static void ValidateForSmokeTest()
        {
            UnifiedQuickMessageChange parsed;
            var valid = "{\"presets\":[\"准备好了\",\"\",\"\",\"\"],\"hotkeys\":[\"Ctrl+Shift+1\",\"\",\"\",\"\",\"\"],\"intervalMs\":400}";
            if (!TryParse(valid, out parsed) || parsed.IntervalMs != 400 ||
                LeagueQuickMessagePresets.Parse(parsed.Presets)[0] != "准备好了")
                throw new InvalidOperationException("Quick message settings rejected valid native JSON.");
            if (TryParse(valid.Replace("\"Ctrl+Shift+1\"", "\"Ctrl+Shift+1\",\"Ctrl+Shift+1\""), out parsed))
                throw new InvalidOperationException("Quick message settings accepted duplicate hotkey slots.");
            if (TryParse(valid.Replace("\"intervalMs\":400", "\"intervalMs\":6000"), out parsed) ||
                TryParse(valid.Replace("\"intervalMs\":400", "\"intervalMs\":\"400\""), out parsed))
                throw new InvalidOperationException("Quick message settings accepted an invalid interval.");
            if (TryParse(valid.Replace("\"准备好了\"", "\"a\\nb\\nc\\nd\\ne\""), out parsed) ||
                TryParse(valid.Replace("\"presets\"", "\"other\""), out parsed))
                throw new InvalidOperationException("Quick message settings accepted unsafe presets or unknown fields.");
        }
    }

    internal sealed partial class UnifiedWorkspaceForm
    {
        private CancellationTokenSource _quickMessageCancellation;
        private int _quickMessageGeneration;

        private void OpenQuickMessagePage(bool pushHistory)
        {
            if (!TryCloseCurrentHub()) return;
            if (pushHistory) _navigation.Open("quick-messages");
            _content.Visible = false;
            _browser.Dock = System.Windows.Forms.DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncQuickMessageSettings();
        }

        private void SyncQuickMessageSettings()
        {
            if (!_ready || _closing || IsDisposed || _browser.CoreWebView2 == null ||
                _navigation.Current != "quick-messages") return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) { PostQuickMessageResult("unavailable"); return; }
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "quick-message-settings",
                presets = LeagueQuickMessagePresets.Parse(module.QuickMessagePresetsJson),
                hotkeys = LeagueQuickMessageHotkeys.Parse(module.QuickMessageHotkeysJson),
                intervalMs = module.QuickMessageIntervalMs
            }));
        }

        private void SaveQuickMessageSettings(string raw)
        {
            if (_navigation.Current != "quick-messages" || _closing) return;
            UnifiedQuickMessageChange change;
            if (!UnifiedWorkspaceQuickMessageContract.TryParse(raw, out change))
            {
                PostQuickMessageResult("invalid"); return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) { PostQuickMessageResult("unavailable"); return; }
            try
            {
                string error;
                if (!module.TrySaveQuickMessageSettings(change.Presets, change.Hotkeys,
                    change.IntervalMs, out error))
                {
                    AppLog.Info("Quick message binding rejected by native hotkey owner.");
                    PostQuickMessageResult("hotkey-conflict"); return;
                }
                PostQuickMessageResult("saved");
            }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick message save failed: " + exception.GetType().Name);
                PostQuickMessageResult("failed");
            }
        }

        private async void SendQuickMessage(int index)
        {
            if (_navigation.Current != "quick-messages" || _closing || index < 0 ||
                index >= LeagueQuickMessagePresets.SlotCount || _quickMessageCancellation != null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) { PostQuickMessageResult("unavailable"); return; }
            var messages = LeagueQuickMessagePresets.Parse(module.QuickMessagePresetsJson);
            if (string.IsNullOrWhiteSpace(messages[index]))
            {
                PostQuickMessageResult("unsaved"); return;
            }
            var token = new CancellationTokenSource();
            _quickMessageCancellation = token;
            var generation = _quickMessageGeneration;
            PostQuickMessageResult("sending");
            try
            {
                var sent = await module.SendQuickMessageAsync(messages[index], token.Token);
                if (generation == _quickMessageGeneration && _navigation.Current == "quick-messages" &&
                    !token.IsCancellationRequested)
                    PostQuickMessageResult(sent ? "sent" : "unavailable");
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified quick message send skipped: " + exception.GetType().Name);
                if (generation == _quickMessageGeneration && !token.IsCancellationRequested)
                    PostQuickMessageResult("failed");
            }
            finally
            {
                if (ReferenceEquals(_quickMessageCancellation, token)) _quickMessageCancellation = null;
                token.Dispose();
            }
        }

        private void CancelQuickMessageSend()
        {
            _quickMessageGeneration++;
            var source = _quickMessageCancellation;
            _quickMessageCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        private void PostQuickMessageResult(string status)
        {
            if (!_ready || IsDisposed || _closing || _browser.CoreWebView2 == null ||
                _navigation.Current != "quick-messages") return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "quick-message-result", status
            }));
        }
    }
}
