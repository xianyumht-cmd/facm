using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using FACM.Core.Desktop;
using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FACM.App;

public sealed partial class MainWindow
{
    private ILeagueBenchQuickPickService? _leagueBenchQuickPick;
    private Border? _leagueBenchCard;
    private TextBlock? _leagueBenchStateText;
    private TextBlock? _leagueBenchStatusText;
    private StackPanel? _leagueBenchButtons;
    private CancellationTokenSource? _leagueBenchIdentityCts;
    private bool _leagueBenchSwapping;
    private bool _ownsLeagueBenchQuickPick;
    private string _leagueBenchRequestedSignature = string.Empty;
    private string _leagueBenchRenderedSignature = string.Empty;

    private sealed record BenchCandidateVisual(LeagueBenchCandidate Candidate, BitmapImage? Icon);

    private void InitializeLeagueBenchQuickPickSurface()
    {
        if (_leagueBenchCard is not null) return;
        if (_leagueBenchQuickPick is null)
        {
            if (Application.Current is not App app) return;
            try
            {
                _leagueBenchQuickPick = app.CreateLeagueBenchQuickPickService();
                _ownsLeagueBenchQuickPick = true;
            }
            catch { return; }
        }

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "随机模式英雄台快捷换人",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "迁移自 FACM 3.5.15。仅在客户端实际提供 Bench 且存在可操作候选时显示头像；每次点击最多发送一次交换请求，成功必须经过只读回验确认。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        _leagueBenchStateText = new TextBlock
        {
            Text = "正在等待随机模式英雄台…",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueBenchStateText, "FACM.League.BenchState");
        content.Children.Add(_leagueBenchStateText);

        _leagueBenchButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var scroller = new ScrollViewer
        {
            Content = _leagueBenchButtons,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        content.Children.Add(scroller);

        _leagueBenchStatusText = new TextBlock
        {
            Text = "等待可交换英雄。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueBenchStatusText, "FACM.League.BenchStatus");
        content.Children.Add(_leagueBenchStatusText);

        card.Child = content;
        _leagueBenchCard = card;
        LeagueWorkbenchPanel.Children.Add(card);
    }

    /// <summary>
    /// Renders the same Workbench.Live Bench state used by the Morphing strip. This presenter does
    /// not start a second Bench polling loop; refresh ownership stays in LeagueWorkbenchViewModel.
    /// </summary>
    private void ApplyLeagueBenchFromLive()
    {
        if (_leagueBenchStateText is null || _leagueBenchStatusText is null) return;

        var live = _leagueWorkbench.Live;
        _leagueBenchQuickPick?.SetSwapRoute(live.BenchSwapRoute);
        if (live.State == LeagueWorkbenchDataState.Unavailable ||
            !string.Equals(live.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
        {
            ApplyLeagueBenchUnavailable("当前没有可用的选人会话。", "等待随机模式英雄台。");
            return;
        }

        if (!live.BenchEnabled)
        {
            ApplyLeagueBenchUnavailable("当前选人模式未启用英雄台。", "此模式无需 Bench quick-pick。");
            return;
        }

        var ids = live.BenchChampionIds.Where(id => id > 0).Distinct().ToArray();
        _leagueBenchStateText.Text = ids.Length == 0
            ? "英雄台已启用，正在等待可交换英雄。"
            : $"英雄台已启用 · 当前 {ids.Length} 个候选";
        if (!_leagueBenchSwapping)
            _leagueBenchStatusText.Text = ids.Length == 0
                ? "等待候选刷新。"
                : "点击英雄头像即可尝试交换；每次点击只写一次。";

        var signature = string.Join(',', ids);
        if (string.Equals(signature, _leagueBenchRequestedSignature, StringComparison.Ordinal))
        {
            SetLeagueBenchButtonsEnabled(!_leagueBenchSwapping);
            return;
        }

        _leagueBenchRequestedSignature = signature;
        CancelLeagueBenchIdentityLoad();
        if (ids.Length == 0)
        {
            RebuildLeagueBenchButtons(Array.Empty<BenchCandidateVisual>());
            return;
        }

        var candidates = LeagueBenchCandidatePresentation.Create(ids);
        RebuildLeagueBenchButtons(candidates.Select(candidate => new BenchCandidateVisual(candidate, null)).ToArray());
        var service = _leagueBenchQuickPick;
        if (service is null) return;

        _leagueBenchIdentityCts = new CancellationTokenSource();
        var requestCts = _leagueBenchIdentityCts;
        _ = LoadLeagueBenchCandidatesAsync(ids, service, requestCts);
    }

    private void ApplyLeagueBenchUnavailable(string state, string status)
    {
        _leagueBenchStateText!.Text = state;
        _leagueBenchStatusText!.Text = status;
        _leagueBenchRequestedSignature = string.Empty;
        CancelLeagueBenchIdentityLoad();
        RebuildLeagueBenchButtons(Array.Empty<BenchCandidateVisual>());
    }

    private async Task LoadLeagueBenchCandidatesAsync(
        IReadOnlyCollection<int> ids,
        ILeagueBenchQuickPickService service,
        CancellationTokenSource requestCts)
    {
        try
        {
            var identities = await service.LoadChampionIdentitiesAsync(ids, requestCts.Token);
            var visuals = new List<BenchCandidateVisual>(ids.Count);
            foreach (var candidate in LeagueBenchCandidatePresentation.Create(ids, identities))
            {
                requestCts.Token.ThrowIfCancellationRequested();
                visuals.Add(new BenchCandidateVisual(
                    candidate,
                    await TryLoadLeagueBenchIconAsync(service, candidate.ChampionId, requestCts.Token)));
            }

            if (_closed || requestCts.IsCancellationRequested ||
                !string.Equals(string.Join(',', ids), _leagueBenchRequestedSignature, StringComparison.Ordinal))
                return;
            RebuildLeagueBenchButtons(visuals);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
        }
        catch
        {
            // The identity model already supplies a safe unknown-champion fallback.
        }
        finally
        {
            if (ReferenceEquals(_leagueBenchIdentityCts, requestCts))
            {
                requestCts.Dispose();
                _leagueBenchIdentityCts = null;
            }
        }
    }

    private static async Task<BitmapImage?> TryLoadLeagueBenchIconAsync(
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

    private void RebuildLeagueBenchButtons(IReadOnlyList<BenchCandidateVisual> visuals)
    {
        var panel = _leagueBenchButtons;
        if (panel is null) return;

        var signature = string.Join(',', visuals.Select(item =>
            item.Candidate.ChampionId.ToString(CultureInfo.InvariantCulture) + ":" +
            item.Candidate.DisplayName + ":" + (item.Icon is null ? "0" : "1")));
        if (string.Equals(signature, _leagueBenchRenderedSignature, StringComparison.Ordinal))
        {
            SetLeagueBenchButtonsEnabled(!_leagueBenchSwapping);
            return;
        }

        _leagueBenchRenderedSignature = signature;
        panel.Children.Clear();
        foreach (var visual in visuals)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(visual.Icon is null
                ? new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)Application.Current.Resources["FacmAccentBrush"],
                    Child = new TextBlock
                    {
                        Text = "?",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["FacmAccentTextBrush"]
                    }
                }
                : new Image
                {
                    Source = visual.Icon,
                    Width = 38,
                    Height = 38,
                    Stretch = Stretch.UniformToFill
                });
            content.Children.Add(new TextBlock
            {
                Text = visual.Candidate.DisplayName,
                MaxWidth = 120,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var button = new Button
            {
                Content = content,
                Tag = visual.Candidate.ChampionId,
                MinWidth = 132,
                Height = 48,
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !_leagueBenchSwapping,
                Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
            };
            AutomationProperties.SetAutomationId(button, "FACM.League.Bench." + visual.Candidate.ChampionId);
            AutomationProperties.SetName(button, visual.Candidate.AccessibleName);
            AutomationProperties.SetHelpText(button, "Manual one-shot Bench swap.");
            ToolTipService.SetToolTip(button, visual.Candidate.DisplayName + " · Click to swap");
            button.Click += OnLeagueBenchChampionClicked;
            panel.Children.Add(button);
        }
    }

    private async void OnLeagueBenchChampionClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: int championId }) return;
        try { await SwapLeagueBenchChampionAsync(championId); }
        catch
        {
            if (!_closed && _leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = "Bench swap failed.";
        }
    }

    private async Task SwapLeagueBenchChampionAsync(int championId)
    {
        var service = _leagueBenchQuickPick;
        if (_closed || service is null || _leagueBenchSwapping || championId <= 0) return;

        _leagueBenchSwapping = true;
        SetBenchSwapButtonsEnabled(false);
        var candidateName = ResolveBenchCandidateName(championId);
        SetBenchSwapStatus("正在交换英雄 " + candidateName + "…", "Swapping " + candidateName + "…");

        try
        {
            var result = await service.TrySwapAsync(championId);
            if (_closed) return;
            var success = result.Success;
            var status = FormatLeagueBenchSwapResult(result, candidateName);
            SetBenchSwapStatus(status, success ? "已切换 · " + candidateName : status);
            SetBenchSwapFeedback(championId, candidateName, success);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_closed) SetBenchSwapStatus("交换失败 · " + candidateName, "Swap failed · " + candidateName);
        }
        finally
        {
            _leagueBenchSwapping = false;
            SetBenchSwapButtonsEnabled(true);
            await RefreshBenchAuthoritativeStateAsync();
        }
    }

    private async Task RefreshBenchAuthoritativeStateAsync()
    {
        if (_closed || !_leagueWorkbench.HasRealDataSource ||
            !string.Equals(_leagueWorkbench.Live.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            // One explicit post-click reconciliation through the existing Workbench owner. This is
            // not a new timer or polling loop; the resulting Live notification redraws both views.
            await _leagueWorkbench.RefreshAsync();
            if (_leagueBenchRuntime is not null)
                await _leagueBenchRuntime.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
        }
    }

    private string ResolveBenchCandidateName(int championId)
    {
        if (_surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
            return ResolveChampSelectCandidateName(championId);
        if (_leagueBenchButtons is not null)
        {
            foreach (var child in _leagueBenchButtons.Children)
            {
                if (child is Button { Tag: int id, Content: StackPanel content } && id == championId)
                {
                    var label = content.Children.OfType<TextBlock>().FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(label?.Text)) return label.Text;
                }
            }
        }
        return "Unknown champion";
    }

    private static string FormatLeagueBenchSwapResult(LeagueBenchSwapResult result, string name)
    {
        return result.Status switch
        {
            LeagueBenchSwapStatus.Success => "交换成功 · " + name,
            LeagueBenchSwapStatus.TargetUnavailable => "目标已不可用 · " + name,
            LeagueBenchSwapStatus.BenchDisabled => "当前模式未启用英雄台。",
            LeagueBenchSwapStatus.SessionUnavailable => "选人会话已结束。",
            LeagueBenchSwapStatus.VerificationFailed => "回读未确认交换 · " + name,
            _ when result.StatusCode > 0 => "交换被客户端拒绝 · HTTP " + result.StatusCode,
            _ => "交换被客户端拒绝 · " + name
        };
    }

    private void SetBenchSwapStatus(string detail, string stripStatus)
    {
        if (_leagueBenchStatusText is not null) _leagueBenchStatusText.Text = detail;
        if (ChampSelectAction is not null && _surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
            ChampSelectAction.Text = stripStatus;
    }

    private void SetBenchSwapFeedback(int championId, string name, bool success)
    {
        if (ChampSelectAction is not null && _surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip)
            ChampSelectAction.Text = success ? "已切换 · " + name : "交换失败 · " + name;
    }

    private void SetBenchSwapButtonsEnabled(bool enabled)
    {
        SetLeagueBenchButtonsEnabled(enabled);
        SetChampSelectCandidateButtonsEnabled(enabled);
    }

    private void SetLeagueBenchButtonsEnabled(bool enabled)
    {
        if (_leagueBenchButtons is null) return;
        foreach (var child in _leagueBenchButtons.Children)
            if (child is Button button) button.IsEnabled = enabled;
    }

    private void CancelLeagueBenchIdentityLoad()
    {
        _leagueBenchIdentityCts?.Cancel();
        _leagueBenchIdentityCts?.Dispose();
        _leagueBenchIdentityCts = null;
    }

    private void DisposeLeagueBenchQuickPickSurface()
    {
        CancelLeagueBenchIdentityLoad();
        if (_ownsLeagueBenchQuickPick && _leagueBenchQuickPick is IDisposable disposable)
            disposable.Dispose();
        _leagueBenchQuickPick = null;
        _ownsLeagueBenchQuickPick = false;
        _leagueBenchCard = null;
        _leagueBenchStateText = null;
        _leagueBenchStatusText = null;
        _leagueBenchButtons = null;
        _leagueBenchRequestedSignature = string.Empty;
        _leagueBenchRenderedSignature = string.Empty;
        _leagueBenchSwapping = false;
    }
}
