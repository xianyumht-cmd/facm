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
        private CancellationTokenSource _championCatalogCancellation;
        private int _championCatalogGeneration;

        internal void OpenPostgameSettings(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("postgame-settings");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncPostgameSettings();
        }

        internal void SyncPostgameSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var textKeys = new[]
            {
                LeagueEfficiencyUiTextKeys.HonorPreferParty,
                LeagueEfficiencyUiTextKeys.HonorOnlyParty,
                LeagueEfficiencyUiTextKeys.HonorAnyAlly,
                LeagueEfficiencyUiTextKeys.HonorOptOut,
                LeagueEfficiencyUiTextKeys.HonorIncludingOpponent
            };
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "postgame-settings",
                autoHonor = module.AutoHonorEnabled, autoReturn = module.AutoReturnLobbyEnabled,
                strategy = module.AutoHonorStrategy,
                strategies = LeagueHonorStrategy.Choices.Select((value, index) => new
                {
                    value, label = LeagueEfficiencyText.Get(_ui, textKeys[Math.Min(index, textKeys.Length - 1)])
                }).ToArray()
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

        internal void ApplyPostgameSettings(string json)
        {
            if (!string.Equals(_navigation.Current, "postgame-settings", StringComparison.Ordinal)) return;
            UnifiedPostgameChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParsePostgame(json, out input))
            {
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed));
                return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                module.UpdatePostGameSettings(input.AutoHonor, input.AutoReturn, input.Strategy);
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.PostGameSaved));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified postgame save failed", exception);
                SendPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed));
            }
        }

        private void SendPostgameResult(string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            { type = "postgame-result", message }));
        }

        internal void OpenAutoSelection(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("auto-selection");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncAutoSelection();
            StartAutoSelectionCatalog();
        }

        internal void StartAutoSelectionCatalog()
        {
            CancelChampionCatalog();
            if (!_ready || IsDisposed) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var cancellation = new CancellationTokenSource();
            _championCatalogCancellation = cancellation;
            var generation = _championCatalogGeneration;
            LoadAutoSelectionCatalogAsync(module, cancellation, generation);
        }

        private async void LoadAutoSelectionCatalogAsync(LeagueEfficiencyModule module,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var champions = await module.LoadChampionsAsync(cancellation.Token);
                if (IsDisposed || cancellation.IsCancellationRequested ||
                    generation != _championCatalogGeneration ||
                    !string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal) ||
                    _browser.CoreWebView2 == null) return;
                var payload = new JavaScriptSerializer().Serialize(new
                {
                    type = "auto-selection-catalog",
                    champions = (champions ?? new LeagueProfileChampionOption[0])
                        .Where(item => item != null && item.Id > 0 && item.Id <= 10000)
                        .Select(item => new { id = item.Id, name = item.Name }).ToArray()
                });
                _browser.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified auto-select champion directory unavailable: " + exception.GetType().Name);
            }
            finally
            {
                if (ReferenceEquals(_championCatalogCancellation, cancellation))
                    _championCatalogCancellation = null;
                cancellation.Dispose();
            }
        }

        internal void CancelChampionCatalog()
        {
            _championCatalogGeneration++;
            var previous = _championCatalogCancellation;
            _championCatalogCancellation = null;
            if (previous != null && !previous.IsCancellationRequested) previous.Cancel();
        }

        internal void ApplyAutoSelection(string json)
        {
            if (!string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal)) return;
            UnifiedAutoSelectionChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParseAutoSelection(json, out input))
            {
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionInvalid));
                return;
            }
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try
            {
                module.UpdateAutoSelectSettings(input.AutoPick, input.PickId, input.AutoBan, input.BanId);
                SendSelectionResult(true, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionSaved));
            }
            catch (ArgumentException)
            {
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionMissingChampion));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified auto-select settings save failed", exception);
                SendSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionFailed));
            }
        }

        internal void ApplySelectionPause(string value)
        {
            if (!string.Equals(_navigation.Current, "auto-selection", StringComparison.Ordinal) ||
                (value != "true" && value != "false")) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            try { module.UpdateAutoSelectTemporaryPause(value == "true"); SyncAutoSelection(); }
            catch (Exception exception) { AppLog.Error("Unified auto-select pause failed", exception); }
        }

        private void SendSelectionResult(bool ok, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                { type = "selection-result", ok, message }));
        }

        internal void SyncAutoSelection()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "auto-selection",
                autoPick = module.AutoPickEnabled, autoBan = module.AutoBanEnabled,
                pickId = module.AutoPickChampionId, banId = module.AutoBanChampionId,
                paused = module.AutoSelectTemporarilyDisabled,
                champions = new object[0]
            }));
        }

        internal void OpenMatchmakingSettings(bool pushHistory)
        {
            if (pushHistory) _navigation.Open("matchmaking-settings");
            _browser.Dock = DockStyle.Fill;
            ResizeForRoute(true);
            SyncWebNavigation();
            SyncMatchmakingSettings();
        }

        internal void ApplyMatchmakingSettings(string request)
        {
            if (!string.Equals(_navigation.Current, "matchmaking-settings", StringComparison.Ordinal)) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            UnifiedMatchmakingChange setting;
            if (!UnifiedWorkspaceSettingsContract.TryParseMatchmaking(request, out setting))
            {
                PostMatchmakingResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingInvalid));
                return;
            }
            try
            {
                module.UpdateMatchmakingSettings(setting.AutoSearch, setting.AutoAccept,
                    setting.MinParty, setting.QueueDelayMs, setting.AcceptDelayMs,
                    setting.StopPolicy, setting.StopAfterMs,
                    setting.WaitForInvitees, setting.RoomNotices);
                SyncMatchmakingSettings();
                PostMatchmakingResult(true, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingSaved));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified matchmaking settings save failed", exception);
                PostMatchmakingResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingFailed));
            }
        }

        private void PostMatchmakingResult(bool ok, string message)
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
                { type = "matchmaking-result", ok, message }));
        }

        internal void SyncMatchmakingSettings()
        {
            if (!_ready || IsDisposed || _browser.CoreWebView2 == null) return;
            var module = LeagueHubUiBridge.UnifiedEfficiency;
            if (module == null) return;
            var payload = new JavaScriptSerializer().Serialize(new
            {
                type = "matchmaking-settings",
                autoSearch = module.AutoMatchmakingEnabled,
                autoAccept = module.AutoAcceptEnabled,
                minParty = module.AutoMatchmakingMinPartySize,
                queueDelayMs = module.AutoMatchmakingStartDelayMs,
                acceptDelayMs = module.AutoAcceptDelayMs,
                stopPolicy = module.AutoMatchmakingStopPolicy,
                stopAfterMs = module.AutoMatchmakingStopAfterMs,
                waitForInvitees = module.AutoMatchmakingWaitForInvitees,
                roomNotices = module.AutoMatchmakingRoomNotices,
                policies = new[]
                {
                    new { value = LeagueMatchmakingStopPolicyCodec.Never, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopNever) },
                    new { value = LeagueMatchmakingStopPolicyCodec.Fixed, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopFixed) },
                    new { value = LeagueMatchmakingStopPolicyCodec.Estimated, label = UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingStopEstimated) }
                }
            });
            _browser.CoreWebView2.PostWebMessageAsJson(payload);
        }

    }
}
