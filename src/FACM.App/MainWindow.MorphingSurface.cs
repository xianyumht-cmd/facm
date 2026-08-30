using System.Globalization;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FACM.App;

public sealed partial class MainWindow
{
    private string _champSelectCandidateSignature = string.Empty;

    private void ApplyMorphingChampSelectState()
    {
        if (!_morphingSurfaceEnabled || ChampSelectCandidatesPanel is null) return;

        var live = _leagueWorkbench.Live;
        if (live.State == LeagueWorkbenchDataState.Unavailable)
        {
            ChampSelectStatus.Text = _text.Get(UiTextKeys.ChampSelectNoData);
            ChampSelectAction.Text = _text.Get(UiTextKeys.ChampSelectUnavailableAction);
            RebuildChampSelectCandidates(Array.Empty<int>());
            return;
        }

        var timer = live.TimerMillisecondsLeft > 0
            ? " · " + (live.TimerMillisecondsLeft / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "s"
            : string.Empty;
        ChampSelectStatus.Text = _text.Get(UiTextKeys.LeagueStateChampSelect) + timer;

        var action = string.IsNullOrWhiteSpace(live.LocalActionType)
            ? _text.Get(UiTextKeys.ChampSelectWaitingAction)
            : live.LocalActionType.Trim();
        if (live.LocalActionChampionId > 0)
            action += " · #" + live.LocalActionChampionId.ToString(CultureInfo.InvariantCulture);
        ChampSelectAction.Text = action;

        var candidates = live.BenchEnabled
            ? live.BenchChampionIds.Where(id => id > 0).Distinct().ToArray()
            : Array.Empty<int>();
        RebuildChampSelectCandidates(candidates);
    }

    private void RebuildChampSelectCandidates(IEnumerable<int> championIds)
    {
        var ids = championIds.Where(id => id > 0).Distinct().ToArray();
        var signature = string.Join(',', ids);
        if (string.Equals(signature, _champSelectCandidateSignature, StringComparison.Ordinal)) return;

        _champSelectCandidateSignature = signature;
        ChampSelectCandidatesPanel.Children.Clear();
        foreach (var championId in ids)
        {
            var button = new Button
            {
                Content = "#" + championId.ToString(CultureInfo.InvariantCulture),
                Tag = championId,
                Width = 48,
                Height = 36,
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
            };
            AutomationProperties.SetAutomationId(button, "FACM.Surface.ChampSelect." + championId);
            AutomationProperties.SetName(button, _text.Get(UiTextKeys.ChampSelectSwapName) + " " + championId);
            AutomationProperties.SetHelpText(button, _text.Get(UiTextKeys.ChampSelectSwapHelp));
            button.Click += OnChampSelectCandidateClick;
            ChampSelectCandidatesPanel.Children.Add(button);
        }
    }

    private async void OnChampSelectCandidateClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: int championId }) return;
        try
        {
            await SwapLeagueBenchChampionAsync(championId);
            ApplyMorphingChampSelectState();
        }
        catch
        {
            ChampSelectAction.Text = _text.Get(UiTextKeys.ChampSelectSwapFailed) + " #" +
                championId.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void PlayMorphingSurfaceTransition()
    {
        if (!_morphingSurfaceEnabled || _surfaceStateMachine.Mode == FacmSurfaceMode.HiddenInGame) return;

        SurfaceRoot.Opacity = 0.84;
        SurfaceTransitionTransform.TranslateY = 5;
        try
        {
            var storyboard = new Storyboard();
            var opacity = new DoubleAnimation
            {
                From = 0.84,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacity, SurfaceRoot);
            Storyboard.SetTargetProperty(opacity, "Opacity");

            var translation = new DoubleAnimation
            {
                From = 5,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translation, SurfaceTransitionTransform);
            Storyboard.SetTargetProperty(translation, "TranslateY");
            storyboard.Children.Add(opacity);
            storyboard.Children.Add(translation);
            storyboard.Begin();
        }
        catch
        {
            SurfaceRoot.Opacity = 1;
            SurfaceTransitionTransform.TranslateY = 0;
        }
    }
}
