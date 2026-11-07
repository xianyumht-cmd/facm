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
            if (!TryParse(valid, out parsed))
                throw new InvalidOperationException("Quick message settings rejected a valid JSON array payload.");
            if (parsed.IntervalMs != 400)
                throw new InvalidOperationException("Quick message interval changed during strict JSON parsing.");
            if (LeagueQuickMessagePresets.Parse(parsed.Presets)[0] != "准备好了")
                throw new InvalidOperationException("Quick message preset changed during native normalization.");
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
        private UnifiedWorkspaceQuickMessageAdapter _quickMessageAdapter;

        private UnifiedWorkspaceQuickMessageAdapter QuickMessageAdapter
        {
            get
            {
                if (_quickMessageAdapter == null)
                    _quickMessageAdapter = new UnifiedWorkspaceQuickMessageAdapter(
                        delegate { return CanPublishOnRoute("quick-messages"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate { return LeagueHubUiBridge.UnifiedEfficiency; });
                return _quickMessageAdapter;
            }
        }

        internal void OpenQuickMessagePage(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("quick-messages");
            _browser.Dock = System.Windows.Forms.DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncQuickMessageSettings();
        }

        internal void SyncQuickMessageSettings()
        {
            QuickMessageAdapter.Sync();
        }

        internal void SaveQuickMessageSettings(string raw)
        {
            QuickMessageAdapter.Save(raw);
        }

        internal void SendQuickMessage(int index)
        {
            QuickMessageAdapter.Send(index);
        }

        internal void CancelQuickMessageSend()
        {
            if (_quickMessageAdapter != null) _quickMessageAdapter.Cancel();
        }
    }
}
