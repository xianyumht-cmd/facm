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
        internal void OpenQuickTools(bool pushHistory)
        {
            if (pushHistory) _navigation.Open(LeagueHubNavigation.Efficiency);
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncQuickSettings();
        }

        internal void ApplyQuickSetting(string request)
        {
            if (!string.Equals(_navigation.Current, LeagueHubNavigation.Efficiency, StringComparison.Ordinal)) return;
            var divider = request.LastIndexOf(':');
            if (divider < 0) return;
            var key = request.Substring(0, divider);
            var value = request.Substring(divider + 1);
            if (value != "true" && value != "false") return;
            var enabled = value == "true";
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                if (key == "autoSearch" || key == "autoAccept")
                    module.UpdateMatchmakingSettings(
                        key == "autoSearch" ? enabled : module.AutoMatchmakingEnabled,
                        key == "autoAccept" ? enabled : module.AutoAcceptEnabled,
                        module.AutoMatchmakingMinPartySize, module.AutoMatchmakingStartDelayMs,
                        module.AutoAcceptDelayMs, module.AutoMatchmakingStopPolicy,
                        module.AutoMatchmakingStopAfterMs, module.AutoMatchmakingWaitForInvitees,
                        module.AutoMatchmakingRoomNotices);
                else if (key == "autoReconnect") module.UpdateReconnectSetting(enabled);
                else if (key == "autoHonor" || key == "autoReturn")
                    module.UpdatePostGameSettings(key == "autoHonor" ? enabled : module.AutoHonorEnabled,
                        key == "autoReturn" ? enabled : module.AutoReturnLobbyEnabled,
                        module.AutoHonorStrategy);
                else return;
                SyncQuickSettings();
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified workspace quick setting update failed", exception);
                if (_ready && _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"tool-error\"}");
            }
        }

        internal void SyncQuickSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "quick-tools",
                values = new
                {
                    autoSearch = module.AutoMatchmakingEnabled,
                    autoAccept = module.AutoAcceptEnabled,
                    autoReconnect = module.AutoReconnectEnabled,
                    autoHonor = module.AutoHonorEnabled,
                    autoReturn = module.AutoReturnLobbyEnabled
                }
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        internal void OpenClientStatus(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("client-status");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncGameflowState();
        }

        internal void OpenAppearance(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("appearance");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncThemeOptions();
            SyncUnifiedPetOptions();
        }

        internal void ApplyTheme(string id)
        {
            if (!string.Equals(_navigation.Current, "appearance", StringComparison.Ordinal)) return;
            var candidate = ThemeCatalog.All.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
            if (candidate == null) return;
            var before = _settings.ThemeId;
            try
            {
                _settings.ThemeId = candidate.Id;
                _settings.Save();
                FacmThemeRuntime.SetCurrent(candidate.Id);
                SyncThemeOptions(true);
                AppLog.Info("Unified workspace skin applied: " + candidate.Id);
            }
            catch (Exception exception)
            {
                _settings.ThemeId = before;
                AppLog.Error("Unified workspace theme update failed", exception);
                if (_ready && _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"theme-error\"}");
            }
        }

        private void SyncThemeOptions(bool applied = false)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "themes",
                selected = _settings.ThemeId,
                applied = applied,
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
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

    }
}
