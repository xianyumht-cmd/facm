using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Streams;

namespace FACM.App;

public sealed partial class MainWindow
{
    private string _champSelectRequestedSignature = string.Empty;
    private string _champSelectRenderedSignature = string.Empty;
    private CancellationTokenSource? _champSelectIdentityCts;
    private bool _champSelectHasCandidates;

    private sealed record ChampSelectCandidateVisual(
        int ChampionId,
        string Name,
        BitmapImage? Icon);

    private void ApplyMorphingChampSelectState()
    {
        _ = RefreshMorphingChampSelectStateAsync();
    }

    private async Task RefreshMorphingChampSelectStateAsync()
    {
        if (!_morphingSurfaceEnabled || ChampSelectCandidatesPanel is null) return;

        var live = _leagueWorkbench.Live;
        if (live.State == LeagueWorkbenchDataState.Unavailable)
        {
            ChampSelectStatus.Text = _text.Get(UiTextKeys.ChampSelectNoData);
            ChampSelectAction.Text = _text.Get(UiTextKeys.ChampSelectUnavailableAction);
            _champSelectRequestedSignature = string.Empty;
            RebuildChampSelectCandidates(Array.Empty<ChampSelectCandidateVisual>());
            return;
        }

        _leagueBenchQuickPick?.SetSwapRoute(live.BenchSwapRoute);

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
        var signature = string.Join(',', candidates);
        if (string.Equals(signature, _champSelectRequestedSignature, StringComparison.Ordinal)) return;

        _champSelectRequestedSignature = signature;
        _champSelectIdentityCts?.Cancel();
        _champSelectIdentityCts?.Dispose();
        _champSelectIdentityCts = null;

        if (candidates.Length == 0)
        {
            RebuildChampSelectCandidates(Array.Empty<ChampSelectCandidateVisual>());
            return;
        }

        var service = _leagueBenchQuickPick;
        if (service is null)
        {
            RebuildChampSelectCandidates(CreateFallbackCandidates(candidates));
            return;
        }

        _champSelectIdentityCts = new CancellationTokenSource();
        var requestCts = _champSelectIdentityCts;

        try
        {
            var identities = await service.LoadChampionIdentitiesAsync(candidates, requestCts.Token);
            var visuals = new List<ChampSelectCandidateVisual>(candidates.Length);
            foreach (var championId in candidates)
            {
                requestCts.Token.ThrowIfCancellationRequested();
                identities.TryGetValue(championId, out var identity);
                var icon = await TryLoadChampionIconAsync(service, championId, requestCts.Token);
                visuals.Add(new ChampSelectCandidateVisual(
                    championId,
                    identity?.Name ?? string.Empty,
                    icon));
            }

            if (_closed || requestCts.IsCancellationRequested ||
                _surfaceStateMachine.Mode != FacmSurfaceMode.ChampSelectStrip ||
                !string.Equals(signature, _champSelectRequestedSignature, StringComparison.Ordinal))
                return;

            RebuildChampSelectCandidates(visuals);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
        }
        catch
        {
            if (!_closed && string.Equals(signature, _champSelectRequestedSignature, StringComparison.Ordinal))
                RebuildChampSelectCandidates(CreateFallbackCandidates(candidates));
        }
        finally
        {
            if (ReferenceEquals(_champSelectIdentityCts, requestCts))
            {
                _champSelectIdentityCts.Dispose();
                _champSelectIdentityCts = null;
            }
        }
    }

    private void RebuildChampSelectCandidates(IEnumerable<ChampSelectCandidateVisual> candidates)
    {
        var visuals = candidates.Where(item => item.ChampionId > 0).ToArray();
        var signature = string.Join(',', visuals.Select(item =>
            item.ChampionId.ToString(CultureInfo.InvariantCulture) + ":" + item.Name + ":" + (item.Icon is null ? "0" : "1")));
        _champSelectHasCandidates = visuals.Length > 0;
        if (string.Equals(signature, _champSelectRenderedSignature, StringComparison.Ordinal))
        {
            if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
                ApplySurfaceGeometry(FacmSurfaceMode.ChampSelectStrip);
            return;
        }

        _champSelectRenderedSignature = signature;
        ChampSelectCandidatesPanel.Children.Clear();
        var ordinal = 0;
        foreach (var visual in visuals)
        {
            ordinal++;
            var name = string.IsNullOrWhiteSpace(visual.Name) ? "候选英雄 " + ordinal : visual.Name;
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (visual.Icon is not null)
            {
                content.Children.Add(new Image
                {
                    Source = visual.Icon,
                    Width = 28,
                    Height = 28,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                });
            }
            else
            {
                content.Children.Add(new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(6),
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FacmAccentBrush"],
                    Child = new TextBlock
                    {
                        Text = name[..Math.Min(1, name.Length)],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FacmAccentTextBrush"]
                    }
                });
            }
            content.Children.Add(new TextBlock
            {
                Text = name,
                MaxWidth = 74,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var button = new Button
            {
                Content = content,
                Tag = visual.ChampionId,
                Width = 94,
                Height = 42,
                Padding = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
            };
            AutomationProperties.SetAutomationId(button, "FACM.Surface.ChampSelect." + visual.ChampionId);
            AutomationProperties.SetName(button, _text.Get(UiTextKeys.ChampSelectSwapName) + " " + name);
            AutomationProperties.SetHelpText(button, _text.Get(UiTextKeys.ChampSelectSwapHelp));
            button.Click += OnChampSelectCandidateClick;
            ChampSelectCandidatesPanel.Children.Add(button);
        }

        if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
            ApplySurfaceGeometry(FacmSurfaceMode.ChampSelectStrip);
    }

    private static IEnumerable<ChampSelectCandidateVisual> CreateFallbackCandidates(IEnumerable<int> championIds) =>
        championIds.Where(id => id > 0)
            .Distinct()
            .Select((id, index) => new ChampSelectCandidateVisual(id, "候选英雄 " + (index + 1), null));

    private static async Task<BitmapImage?> TryLoadChampionIconAsync(
        ILeagueBenchQuickPickService service,
        int championId,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await service.LoadChampionIconAsync(championId, cancellationToken);
            if (bytes is null || bytes.Length == 0) return null;
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = 32, DecodePixelHeight = 32 };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
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
