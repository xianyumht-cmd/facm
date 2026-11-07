using System;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using FACM.AppHost.Modules;
using FACM.League;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspaceAutomationAdapter
    {
        private readonly Func<string, bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<LeagueEfficiencyModule> _owner;
        private readonly UiTextCatalog _ui;
        private CancellationTokenSource _championCancellation;
        private int _championGeneration;

        internal UnifiedWorkspaceAutomationAdapter(Func<string, bool> canPublish,
            Action<string> publish, Func<LeagueEfficiencyModule> owner, UiTextCatalog ui)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        internal void SyncPostgame()
        {
            if (!_canPublish("postgame-settings")) return;
            var module = _owner();
            if (module == null) return;
            var keys = new[]
            {
                LeagueEfficiencyUiTextKeys.HonorPreferParty,
                LeagueEfficiencyUiTextKeys.HonorOnlyParty,
                LeagueEfficiencyUiTextKeys.HonorAnyAlly,
                LeagueEfficiencyUiTextKeys.HonorOptOut,
                LeagueEfficiencyUiTextKeys.HonorIncludingOpponent
            };
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "postgame-settings",
                autoHonor = module.AutoHonorEnabled, autoReturn = module.AutoReturnLobbyEnabled,
                strategy = module.AutoHonorStrategy,
                strategies = LeagueHonorStrategy.Choices.Select((value, index) => new
                {
                    value,
                    label = LeagueEfficiencyText.Get(_ui, keys[Math.Min(index, keys.Length - 1)])
                }).ToArray()
            }));
        }

        internal void ApplyPostgame(string json)
        {
            if (!_canPublish("postgame-settings")) return;
            UnifiedPostgameChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParsePostgame(json, out input))
            { PostPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed)); return; }
            var module = _owner();
            if (module == null) return;
            try
            {
                module.UpdatePostGameSettings(input.AutoHonor, input.AutoReturn, input.Strategy);
                PostPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.PostGameSaved));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified postgame save failed", exception);
                PostPostgameResult(LeagueEfficiencyText.Get(_ui, LeagueEfficiencyUiTextKeys.SaveFailed));
            }
        }

        private void PostPostgameResult(string message)
        {
            if (_canPublish("postgame-settings"))
                _publish(new JavaScriptSerializer().Serialize(new { type = "postgame-result", message }));
        }

        internal void SyncAutoSelection()
        {
            if (!_canPublish("auto-selection")) return;
            var module = _owner();
            if (module == null) return;
            _publish(new JavaScriptSerializer().Serialize(new
            {
                type = "auto-selection",
                autoPick = module.AutoPickEnabled, autoBan = module.AutoBanEnabled,
                pickId = module.AutoPickChampionId, banId = module.AutoBanChampionId,
                paused = module.AutoSelectTemporarilyDisabled,
                champions = new object[0]
            }));
        }

        internal void StartChampionCatalog()
        {
            CancelChampionCatalog();
            if (!_canPublish("auto-selection")) return;
            var module = _owner();
            if (module == null) return;
            var source = new CancellationTokenSource();
            _championCancellation = source;
            ReadChampionCatalogAsync(module, source, _championGeneration);
        }

        private async void ReadChampionCatalogAsync(LeagueEfficiencyModule module,
            CancellationTokenSource source, int generation)
        {
            try
            {
                var champions = await module.LoadChampionsAsync(source.Token);
                if (!ChampionCurrent(module, source, generation)) return;
                _publish(new JavaScriptSerializer().Serialize(new
                {
                    type = "auto-selection-catalog",
                    champions = (champions ?? new LeagueProfileChampionOption[0])
                        .Where(item => item != null && item.Id > 0 && item.Id <= 10000)
                        .Select(item => new { id = item.Id, name = item.Name }).ToArray()
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                AppLog.Info("Unified auto-select champion directory unavailable: " + exception.GetType().Name);
            }
            finally
            {
                if (ReferenceEquals(_championCancellation, source)) _championCancellation = null;
                source.Dispose();
            }
        }

        private bool ChampionCurrent(LeagueEfficiencyModule module, CancellationTokenSource source,
            int generation)
        {
            return _canPublish("auto-selection") && !source.IsCancellationRequested &&
                generation == _championGeneration && ReferenceEquals(source, _championCancellation) &&
                ReferenceEquals(module, _owner());
        }

        internal void CancelChampionCatalog()
        {
            _championGeneration++;
            var source = _championCancellation;
            _championCancellation = null;
            if (source != null && !source.IsCancellationRequested) source.Cancel();
        }

        internal void ApplyAutoSelection(string json)
        {
            if (!_canPublish("auto-selection")) return;
            UnifiedAutoSelectionChange input;
            if (!UnifiedWorkspaceSettingsContract.TryParseAutoSelection(json, out input))
            { PostSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionInvalid)); return; }
            var module = _owner();
            if (module == null) return;
            try
            {
                module.UpdateAutoSelectSettings(input.AutoPick, input.PickId, input.AutoBan, input.BanId);
                PostSelectionResult(true, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionSaved));
            }
            catch (ArgumentException)
            {
                PostSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionMissingChampion));
            }
            catch (Exception exception)
            {
                AppLog.Error("Unified auto-select settings save failed", exception);
                PostSelectionResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedSelectionFailed));
            }
        }

        internal void ApplySelectionPause(string value)
        {
            if (!_canPublish("auto-selection") || (value != "true" && value != "false")) return;
            var module = _owner();
            if (module == null) return;
            try { module.UpdateAutoSelectTemporaryPause(value == "true"); SyncAutoSelection(); }
            catch (Exception exception) { AppLog.Error("Unified auto-select pause failed", exception); }
        }

        private void PostSelectionResult(bool ok, string message)
        {
            if (_canPublish("auto-selection"))
                _publish(new JavaScriptSerializer().Serialize(new { type = "selection-result", ok, message }));
        }

        internal void SyncMatchmaking()
        {
            if (!_canPublish("matchmaking-settings")) return;
            var module = _owner();
            if (module == null) return;
            _publish(new JavaScriptSerializer().Serialize(new
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
            }));
        }

        internal void ApplyMatchmaking(string request)
        {
            if (!_canPublish("matchmaking-settings")) return;
            var module = _owner();
            if (module == null) return;
            UnifiedMatchmakingChange setting;
            if (!UnifiedWorkspaceSettingsContract.TryParseMatchmaking(request, out setting))
            { PostMatchmakingResult(false, UiTextRuntime.Text(UiTextKeys.UnifiedMatchmakingInvalid)); return; }
            try
            {
                module.UpdateMatchmakingSettings(setting.AutoSearch, setting.AutoAccept,
                    setting.MinParty, setting.QueueDelayMs, setting.AcceptDelayMs,
                    setting.StopPolicy, setting.StopAfterMs,
                    setting.WaitForInvitees, setting.RoomNotices);
                SyncMatchmaking();
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
            if (_canPublish("matchmaking-settings"))
                _publish(new JavaScriptSerializer().Serialize(new { type = "matchmaking-result", ok, message }));
        }

        internal static void ValidateForSmokeTest()
        {
            var posts = 0;
            var adapter = new UnifiedWorkspaceAutomationAdapter(
                delegate(string route) { return route == "auto-selection"; },
                delegate { posts++; }, () => null, UiTextCatalog.Load());
            adapter.SyncPostgame();
            adapter.SyncMatchmaking();
            adapter.StartChampionCatalog();
            adapter.CancelChampionCatalog();
            if (posts != 0 || adapter._championGeneration != 2)
                throw new InvalidOperationException("Automation adapter route or cancel isolation failed.");
        }
    }
}
