using System;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.Services;
using FACM.Theming;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceSkinAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Func<bool> _canApply;
        private readonly Action<string> _publish;
        private readonly AppSettings _settings;

        internal UnifiedWorkspaceSkinAdapter(Func<bool> canPublish, Func<bool> canApply,
            Action<string> publish, AppSettings settings)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _canApply = canApply ?? throw new ArgumentNullException(nameof(canApply));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void Apply(string id)
        {
            if (!_canApply()) return;
            var candidate = FindTheme(id);
            if (candidate == null) return;
            var before = _settings.ThemeId;
            try
            {
                _settings.ThemeId = candidate.Id;
                _settings.Save();
                FacmThemeRuntime.SetCurrent(candidate.Id);
                Sync(true);
                AppLog.Info("Unified workspace skin applied: " + candidate.Id);
            }
            catch (Exception exception)
            {
                _settings.ThemeId = before;
                try
                {
                    _settings.Save();
                    if (FacmThemeRuntime.Current.Id != before)
                        FacmThemeRuntime.SetCurrent(before);
                }
                catch (Exception rollback)
                { AppLog.Info("Unified workspace skin rollback failed: " + rollback.GetType().Name); }
                AppLog.Error("Unified workspace theme update failed", exception);
                if (_canPublish()) _publish("{\"type\":\"theme-error\"}");
            }
        }

        internal void Sync(bool applied = false)
        {
            // The initial skin contract is needed on the home route as well as the picker.
            if (_canPublish()) _publish(Serialize(_settings.ThemeId, applied));
        }

        private static ThemeDefinition FindTheme(string id)
        {
            return ThemeCatalog.All.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
        }

        private static string Serialize(string selected, bool applied)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                type = "themes",
                selected,
                applied,
                items = ThemeCatalog.All.Select(theme => new
                {
                    id = theme.Id, name = theme.Name, description = theme.Description,
                    accent = ColorTranslator.ToHtml(theme.Accent),
                    background = ColorTranslator.ToHtml(theme.Background),
                    surface = ColorTranslator.ToHtml(theme.Surface),
                    surfaceRaised = ColorTranslator.ToHtml(theme.SurfaceSecondary),
                    text = ColorTranslator.ToHtml(theme.TextPrimary),
                    muted = ColorTranslator.ToHtml(theme.TextMuted),
                    border = ColorTranslator.ToHtml(theme.Border),
                    light = theme.IsLight
                }).ToArray()
            });
        }

        internal static void ValidateForSmokeTest()
        {
            if (FindTheme("obsidian-gold") == null || FindTheme("cloud-light") == null ||
                FindTheme("unknown") != null || FindTheme("OBSIDIAN-GOLD") != null ||
                FindTheme(null) != null)
                throw new InvalidOperationException("Skin selection contract changed.");
            var json = Serialize("cloud-light", true);
            if (!json.Contains("\"type\":\"themes\"") ||
                !json.Contains("\"selected\":\"cloud-light\"") ||
                !json.Contains("\"applied\":true") ||
                !json.Contains("\"light\":true") ||
                !json.Contains("\"surfaceRaised\""))
                throw new InvalidOperationException("Skin palette projection contract changed.");
            var sent = 0;
            var local = new AppSettings();
            var adapter = new UnifiedWorkspaceSkinAdapter(() => true, () => false,
                delegate(string payload)
                {
                    if (!payload.Contains("\"type\":\"themes\""))
                        throw new InvalidOperationException("Home skin hydration changed.");
                    sent++;
                }, local);
            adapter.Sync();
            adapter.Apply("obsidian-gold");
            if (sent != 1 || local.ThemeId != ThemeCatalog.DefaultThemeId)
                throw new InvalidOperationException("Skin adapter applied outside the appearance route.");
        }
    }
}
