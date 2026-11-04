using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FACM.Services;

namespace FACM.League
{
    internal static class LeagueInGameMessageSmokeTest
    {
        internal static void Validate()
        {
            Require(LeagueNativeGameMessageInput.HasNativeInputLayoutForSmokeTest(),
                "Native INPUT size/alignment must match Win32 on the current architecture.");
            ValidateSettings();
            ValidateSendingAsync().GetAwaiter().GetResult();
        }

        private static void ValidateSettings()
        {
            var settings = new AppSettings();
            Require(settings.LeagueQuickMessageIntervalMs == 500 &&
                LeagueQuickMessageHotkeys.Parse(settings.LeagueQuickMessageHotkeys).Length == 5,
                "Quick-message hotkeys and interval defaults changed.");
            settings.LeagueQuickMessageHotkeys = LeagueQuickMessageHotkeys.Save(new[]
                { "F7", "Ctrl+F8", "", "", "F10" });
            settings.LeagueQuickMessageIntervalMs = 1200;
            var restored = AppSettings.ParseLines(settings.BuildLinesForSmokeTest());
            Require(restored.LeagueQuickMessageIntervalMs == 1200 &&
                LeagueQuickMessageHotkeys.Parse(restored.LeagueQuickMessageHotkeys)[1] == "Ctrl+F8",
                "Quick-message hotkeys or interval failed INI roundtrip.");
            var cloud = CloudSettingsSnapshot.Capture(settings);
            var target = new AppSettings();
            cloud.ApplyTo(target);
            Require(target.LeagueQuickMessageIntervalMs == 1200 &&
                LeagueQuickMessageHotkeys.Parse(target.LeagueQuickMessageHotkeys)[4] == "F10",
                "Quick-message hotkeys or interval failed cloud roundtrip.");
            GgmanPortableSettingsStore.Validate(new GgmanPortableSettingsProfile { Settings = cloud });
            Require(LeagueQuickMessageHotkeys.Parse("[\"F7\",\"F7\",\"\",\"\",\"\"]")[0] == null,
                "Duplicate quick-message shortcuts should be disabled.");
            Require(LeagueQuickMessageHotkeys.Parse("[\"A\",\"\",\"\",\"\",\"\"]")[0] == null,
                "Bare typing keys should not become global shortcuts.");
        }

        private static async Task ValidateSendingAsync()
        {
            var phase = new LeagueDashboardPhaseState { Connected = true, Phase = "InProgress" };
            var input = new Input();
            using (var sender = new LeagueInGameMessageSender(input, () => phase))
            {
                Require(await sender.SendAsync("hello\nworld", 0).ConfigureAwait(false) &&
                    string.Join("|", input.Actions) == "enter|hello|enter|enter|world|enter",
                    "User-triggered in-game messages must dispatch one Enter/text/Enter group per line.");
                input.Actions.Clear();
                input.Foreground = false;
                Require(!await sender.SendAsync("blocked", 0).ConfigureAwait(false) && input.Actions.Count == 0,
                    "Foreground loss must block native typing.");
                input.Foreground = true;
                phase.Phase = "Lobby";
                Require(!await sender.SendAsync("blocked", 0).ConfigureAwait(false) && input.Actions.Count == 0,
                    "Non-game phases must not issue native input.");
                phase.Phase = "InProgress";
                input.AfterEnter = () => sender.Cancel();
                Require(!await sender.SendAsync("cancelled", 0).ConfigureAwait(false) &&
                    string.Join("|", input.Actions) == "enter",
                    "Cancellation after opening chat must suppress text and closing Enter.");
                input.Actions.Clear();
                using (var cancellation = new CancellationTokenSource())
                {
                    input.AfterEnter = () => cancellation.Cancel();
                    Require(!await sender.SendAsync("external cancellation", 0, cancellation.Token).ConfigureAwait(false) &&
                        string.Join("|", input.Actions) == "enter", "Parent request cancellation did not stop native input.");
                }
                input.AfterEnter = null;
                input.Actions.Clear();
                input.AfterText = () => phase.Phase = "EndOfGame";
                Require(!await sender.SendAsync("phase changed", 0).ConfigureAwait(false) &&
                    string.Join("|", input.Actions) == "enter|phase changed",
                    "Gameflow departure must stop the remaining native input.");
            }
        }

        private static void Require(bool passed, string detail)
        {
            if (!passed) throw new InvalidOperationException(detail);
        }

        private sealed class Input : ILeagueGameMessageInput
        {
            internal readonly List<string> Actions = new List<string>();
            internal bool Foreground = true;
            internal Action AfterEnter;
            internal Action AfterText;
            public bool IsGameForeground() { return Foreground; }
            public bool AreModifiersReleased() { return true; }
            public bool TryPressEnter()
            {
                if (!Foreground) return false;
                Actions.Add("enter");
                AfterEnter?.Invoke();
                return true;
            }
            public bool TrySendText(string text, CancellationToken token)
            {
                if (!Foreground || token.IsCancellationRequested) return false;
                Actions.Add(text);
                AfterText?.Invoke();
                return true;
            }
        }
    }
}
