using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private int _benchStripCandidateCount;
    private string _benchLastCandidateSignature = string.Empty;
    private readonly LeagueBenchContextDismissal _benchContextDismissal = new();
    private readonly Dictionary<int, string> _champSelectCandidateNames = [];

    private sealed record ChampSelectCandidateVisual(LeagueBenchCandidate Candidate, BitmapImage? Icon);

    private void BeginBenchContext()
    {
        CancelChampSelectIdentityLoad();
        _benchContextDismissal.BeginNewContext();
        _benchLastCandidateSignature = string.Empty;
        _benchStripCandidateCount = 0;
        _champSelectRequestedSignature = string.Empty;
        _champSelectRenderedSignature = string.Empty;
        _champSelectCandidateNames.Clear();
    }

    private void DismissBenchStripForCurrentContext() => _benchContextDismissal.DismissCurrentContext();

    private void ResetBenchContext()
    {
        _benchLastCandidateSignature = string.Empty;
        _benchStripCandidateCount = 0;
        _champSelectRequestedSignature = string.Empty;
        _champSelectRenderedSignature = string.Empty;
        _champSelectCandidateNames.Clear();
        CancelChampSelectIdentityLoad();
    }

    private void CancelChampSelectIdentityLoad()
    {
        _champSelectIdentityCts?.Cancel();
        _champSelectIdentityCts?.Dispose();
        _champSelectIdentityCts = null;
    }

    private void ApplyMorphingChampSelectState() => _ = RefreshMorphingChampSelectStateAsync();

    private async Task RefreshMorphingChampSelectStateAsync()
    {
        if (!_morphingSurfaceEnabled || ChampSelectCandidatesPanel is null) return;

        var live = _leagueWorkbench.Live;
        var candidates = live.BenchEnabled
            ? live.BenchChampionIds.Where(id => id > 0).Distinct().ToArray()
            : Array.Empty<int>();
        var signature = string.Join(',', candidates);
        if (!string.Equals(signature, _benchLastCandidateSignature, StringComparison.Ordinal))
        {
            _benchLastCandidateSignature = signature;
            if (!string.IsNullOrEmpty(signature))
                _benchContextDismissal.ResetForMaterialCandidateChange();
        }

        var eligible = LeagueBenchSwapStripPolicy.IsEligible(live) && candidates.Length > 0;
        if (!eligible)
        {
            if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip &&
                !_surfaceStateMachine.IsModalScopeActive)
            {
                ShowMorphingSurface(FacmSurfaceMode.Orb, "bench-candidates-unavailable", false, live.Phase);
            }
            return;
        }

        _benchStripCandidateCount = candidates.Length;
        if (_surfaceStateMachine.IsModalScopeActive ||
            !_benchContextDismissal.CanAutoShow(true))
            return;

        if (_surfaceStateMachine.Mode != FacmSurfaceMode.ChampSelectStrip)
        {
            ShowMorphingSurface(FacmSurfaceMode.ChampSelectStrip, "bench-candidates-available", false, live.Phase);
            return;
        }

        ChampSelectStatus.Text = _text.Get(UiTextKeys.LeagueStateChampSelect);
        if (!_leagueBenchSwapping)
            ChampSelectAction.Text = "点击头像即可交换英雄";

        if (string.Equals(signature, _champSelectRequestedSignature, StringComparison.Ordinal))
        {
            SetChampSelectCandidateButtonsEnabled(!_leagueBenchSwapping);
            return;
        }

        _champSelectRequestedSignature = signature;
        CancelChampSelectIdentityLoad();
        var fallback = LeagueBenchCandidatePresentation.Create(candidates)
            .Select(candidate => new ChampSelectCandidateVisual(candidate, null))
            .ToArray();
        RebuildChampSelectCandidates(fallback);

        var service = _leagueBenchQuickPick;
        if (service is null) return;

        _champSelectIdentityCts = new CancellationTokenSource();
        var requestCts = _champSelectIdentityCts;
        try
        {
            var identities = await service.LoadChampionIdentitiesAsync(candidates, requestCts.Token);
            var visuals = new List<ChampSelectCandidateVisual>(candidates.Length);
            foreach (var candidate in LeagueBenchCandidatePresentation.Create(candidates, identities))
            {
                requestCts.Token.ThrowIfCancellationRequested();
                visuals.Add(new ChampSelectCandidateVisual(
                    candidate,
                    await TryLoadChampionIconAsync(service, candidate.ChampionId, requestCts.Token)));
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
                RebuildChampSelectCandidates(fallback);
        }
        finally
        {
            if (ReferenceEquals(_champSelectIdentityCts, requestCts))
            {
                requestCts.Dispose();
                _champSelectIdentityCts = null;
            }
        }
    }

    private void RebuildChampSelectCandidates(IEnumerable<ChampSelectCandidateVisual> candidates)
    {
        var visuals = candidates
            .Where(item => item.Candidate.ChampionId > 0 && item.Candidate.IsActionable)
            .ToArray();
        var signature = string.Join(',', visuals.Select(item =>
            item.Candidate.ChampionId.ToString(CultureInfo.InvariantCulture) + ":" +
            item.Candidate.DisplayName + ":" + (item.Icon is null ? "0" : "1")));
        _champSelectHasCandidates = visuals.Length > 0;
        _benchStripCandidateCount = visuals.Length > 0 ? visuals.Length : _benchStripCandidateCount;
        if (string.Equals(signature, _champSelectRenderedSignature, StringComparison.Ordinal))
        {
            if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
                EnsureCurrentSurfacePresentation("bench-strip-candidates-unchanged");
            return;
        }

        _champSelectRenderedSignature = signature;
        _champSelectCandidateNames.Clear();
        ChampSelectCandidatesPanel.Children.Clear();
        foreach (var visual in visuals)
        {
            _champSelectCandidateNames[visual.Candidate.ChampionId] = visual.Candidate.DisplayName;
            FrameworkElement portrait = visual.Icon is null
                ? new Border
                {
                    Width = LeagueBenchSwapStripPolicy.PortraitTileDip,
                    Height = LeagueBenchSwapStripPolicy.PortraitTileDip,
                    CornerRadius = new CornerRadius(7),
                    Background = (Brush)Application.Current.Resources["FacmAccentBrush"],
                    Child = new TextBlock
                    {
                        Text = "?",
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["FacmAccentTextBrush"]
                    }
                }
                : new Image
                {
                    Source = visual.Icon,
                    Width = LeagueBenchSwapStripPolicy.PortraitTileDip,
                    Height = LeagueBenchSwapStripPolicy.PortraitTileDip,
                    Stretch = Stretch.UniformToFill
                };
            var button = new Button
            {
                Content = portrait,
                Tag = visual.Candidate.ChampionId,
                Width = 50,
                Height = 50,
                Padding = new Thickness(2),
                IsTabStop = true,
                IsEnabled = !_leagueBenchSwapping,
                Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
            };
            AutomationProperties.SetAutomationId(button, "FACM.Surface.BenchSwap." + visual.Candidate.ChampionId);
            AutomationProperties.SetName(button, visual.Candidate.AccessibleName);
            AutomationProperties.SetHelpText(button, visual.Candidate.DisplayName + " · Click to swap once");
            ToolTipService.SetToolTip(button, visual.Candidate.DisplayName + " · 点击交换一次");
            button.Click += OnChampSelectCandidateClick;
            ChampSelectCandidatesPanel.Children.Add(button);
        }

        if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
            EnsureCurrentSurfacePresentation("bench-strip-candidates-changed");
    }

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
            var image = new BitmapImage { DecodePixelWidth = 44, DecodePixelHeight = 44 };
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

    private void SetChampSelectCandidateButtonsEnabled(bool enabled)
    {
        if (ChampSelectCandidatesPanel is null) return;
        foreach (var child in ChampSelectCandidatesPanel.Children)
            if (child is Button button) button.IsEnabled = enabled;
    }

    private string ResolveChampSelectCandidateName(int championId) =>
        _champSelectCandidateNames.TryGetValue(championId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "Unknown champion";

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
            ChampSelectAction.Text = "交换失败 · " + ResolveChampSelectCandidateName(championId);
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
