using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using FACM.Core.Desktop;

namespace FACM.App;

public sealed partial class MainWindow
{
    private ILeagueBenchQuickPickService? _leagueBenchQuickPick;
    private Border? _leagueBenchCard;
    private TextBlock? _leagueBenchStateText;
    private TextBlock? _leagueBenchStatusText;
    private StackPanel? _leagueBenchButtons;
    private CancellationTokenSource? _leagueBenchLoopCts;
    private bool _leagueBenchRefreshing;
    private bool _leagueBenchSwapping;
    private bool _leagueBenchActive;
    private string _leagueBenchSignature = string.Empty;

    private void InitializeLeagueBenchQuickPickSurface()
    {
        if (_leagueBenchCard is not null) return;
        if (Application.Current is not App app) return;

        try { _leagueBenchQuickPick = app.CreateLeagueBenchQuickPickService(); }
        catch { return; }

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
            Text = "迁移自 FACM 3.5.15。仅在客户端实际提供 Bench 时显示候选；每次点击最多发送一次交换请求，成功必须经过只读回验确认，不会自动重试写入。",
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

        _leagueBenchLoopCts = new CancellationTokenSource();
        _ = RunLeagueBenchLoopAsync(_leagueBenchLoopCts.Token);
    }

    private async Task RunLeagueBenchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_closed)
            {
                var hidden = !IsLeagueWorkbenchSelected();
                var inGame = IsLeagueBenchInGame();
                var morphingChampSelect = _morphingSurfaceEnabled &&
                                          _surfaceStateMachine.Mode == FacmSurfaceMode.ChampSelectStrip;
                if (!hidden && !inGame && !morphingChampSelect && !_leagueBenchSwapping)
                    await RefreshLeagueBenchOnceAsync(cancellationToken);

                var delay = LeagueBenchQuickPickPolling.ResolveDelay(_leagueBenchActive, inGame, hidden);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (!_closed && _leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = "英雄台后台刷新已停止；LOL 工作台其它功能不受影响。";
        }
    }

    private bool IsLeagueBenchInGame()
    {
        var phase = _leagueWorkbench.Live.Phase ?? string.Empty;
        return phase.Equals("InProgress", StringComparison.OrdinalIgnoreCase) ||
               phase.Equals("Reconnect", StringComparison.OrdinalIgnoreCase) ||
               phase.Equals("GameStart", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshLeagueBenchOnceAsync(CancellationToken cancellationToken = default)
    {
        var service = _leagueBenchQuickPick;
        if (_closed || service is null || _leagueBenchRefreshing || _leagueBenchSwapping) return;

        _leagueBenchRefreshing = true;
        try
        {
            var state = await service.RefreshAsync(cancellationToken);
            if (_closed || cancellationToken.IsCancellationRequested) return;
            ApplyLeagueBenchState(state);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_closed && _leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = "英雄台状态读取失败；LOL 工作台其它功能不受影响。";
        }
        finally
        {
            _leagueBenchRefreshing = false;
        }
    }

    private void ApplyLeagueBenchState(LeagueBenchQuickPickState state)
    {
        if (_leagueBenchStateText is null || _leagueBenchStatusText is null) return;

        if (!state.SessionAvailable)
        {
            _leagueBenchActive = false;
            _leagueBenchStateText.Text = "当前没有可用的选人会话。";
            _leagueBenchStatusText.Text = "等待随机模式英雄台。";
            RebuildLeagueBenchButtons(Array.Empty<int>());
            return;
        }

        if (!state.BenchEnabled)
        {
            _leagueBenchActive = false;
            _leagueBenchStateText.Text = "当前选人模式未启用英雄台。";
            _leagueBenchStatusText.Text = "此模式无需 Bench quick-pick。";
            RebuildLeagueBenchButtons(Array.Empty<int>());
            return;
        }

        _leagueBenchActive = true;
        var ids = state.ChampionIds.Where(value => value > 0).Distinct().ToArray();
        _leagueBenchStateText.Text = ids.Length == 0
            ? "英雄台已启用，正在等待可交换英雄。"
            : $"英雄台已启用 · 当前 {ids.Length} 个候选";
        if (!_leagueBenchSwapping)
            _leagueBenchStatusText.Text = ids.Length == 0
                ? "等待候选刷新。"
                : "点击英雄即可尝试交换；每次点击只写一次。";
        RebuildLeagueBenchButtons(ids);
    }

    private void RebuildLeagueBenchButtons(IEnumerable<int> championIds)
    {
        var panel = _leagueBenchButtons;
        if (panel is null) return;

        var ids = championIds.Where(value => value > 0).Distinct().ToArray();
        var signature = string.Join(',', ids);
        if (string.Equals(signature, _leagueBenchSignature, StringComparison.Ordinal))
        {
            SetLeagueBenchButtonsEnabled(!_leagueBenchSwapping);
            return;
        }
        _leagueBenchSignature = signature;
        panel.Children.Clear();

        foreach (var championId in ids)
        {
            var button = new Button
            {
                Content = "#" + championId,
                Tag = championId,
                MinWidth = 64,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsEnabled = !_leagueBenchSwapping
            };
            AutomationProperties.SetAutomationId(button, "FACM.League.Bench." + championId);
            AutomationProperties.SetName(button, "交换英雄 " + championId);
            AutomationProperties.SetHelpText(button, "手动交换英雄台候选；单次点击最多发送一次写请求。");
            button.Click += OnLeagueBenchChampionClicked;
            panel.Children.Add(button);
        }
    }

    private async void OnLeagueBenchChampionClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: int championId }) return;
        try
        {
            await SwapLeagueBenchChampionAsync(championId);
        }
        catch
        {
            if (!_closed && _leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = $"英雄 #{championId} 交换失败。";
        }
    }

    private async Task SwapLeagueBenchChampionAsync(int championId)
    {
        var service = _leagueBenchQuickPick;
        if (_closed || service is null || _leagueBenchSwapping || championId <= 0) return;

        _leagueBenchSwapping = true;
        SetLeagueBenchButtonsEnabled(false);
        if (_leagueBenchStatusText is not null)
            _leagueBenchStatusText.Text = $"正在交换英雄 #{championId}…";

        try
        {
            var result = await service.TrySwapAsync(championId);
            if (_closed) return;
            if (_leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = FormatLeagueBenchSwapResult(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!_closed && _leagueBenchStatusText is not null)
                _leagueBenchStatusText.Text = $"英雄 #{championId} 交换失败。";
        }
        finally
        {
            _leagueBenchSwapping = false;
            SetLeagueBenchButtonsEnabled(true);
            await RefreshLeagueBenchOnceAsync();
        }
    }

    private static string FormatLeagueBenchSwapResult(LeagueBenchSwapResult result)
    {
        var suffix = " #" + result.ChampionId;
        if (result.ElapsedMilliseconds > 0) suffix += " · " + result.ElapsedMilliseconds + " ms";
        return result.Status switch
        {
            LeagueBenchSwapStatus.Success => "交换成功" + suffix,
            LeagueBenchSwapStatus.TargetUnavailable => "目标已不在英雄台" + suffix,
            LeagueBenchSwapStatus.BenchDisabled => "当前模式未启用英雄台。",
            LeagueBenchSwapStatus.SessionUnavailable => "选人会话已结束。",
            LeagueBenchSwapStatus.VerificationFailed => "客户端已接受请求，但回读未确认交换完成" + suffix,
            _ when result.StatusCode > 0 => "交换被客户端拒绝" + suffix + " · HTTP " + result.StatusCode,
            _ => "交换被客户端拒绝" + suffix
        };
    }

    private void SetLeagueBenchButtonsEnabled(bool enabled)
    {
        if (_leagueBenchButtons is null) return;
        foreach (var child in _leagueBenchButtons.Children)
            if (child is Button button) button.IsEnabled = enabled;
    }

    private void DisposeLeagueBenchQuickPickSurface()
    {
        _leagueBenchLoopCts?.Cancel();
        _leagueBenchLoopCts?.Dispose();
        _leagueBenchLoopCts = null;
        if (_leagueBenchQuickPick is IDisposable disposable) disposable.Dispose();
        _leagueBenchQuickPick = null;
        _leagueBenchCard = null;
        _leagueBenchStateText = null;
        _leagueBenchStatusText = null;
        _leagueBenchButtons = null;
        _leagueBenchSignature = string.Empty;
        _leagueBenchActive = false;
        _leagueBenchRefreshing = false;
        _leagueBenchSwapping = false;
    }
}
