using System.ComponentModel;
using FACM.App.ViewModels;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private bool _leagueAutomationUiApplying;
    private string _leagueAutomationStatusTextKey = UiTextKeys.LeagueAutomationSettingsReady;
    private LeaguePostGameAutomationSettingsViewModel? _postGameAutomationSettings;
    private ToggleSwitch? _leagueAutoHonorToggle;
    private TextBlock? _leagueAutoHonorHint;
    private ToggleSwitch? _leagueAutoReturnLobbyToggle;
    private TextBlock? _leagueAutoReturnLobbyHint;
    private TextBlock? _leaguePostGameStatus;

    private void InitializeLeagueAutomationSurface()
    {
        LeagueAutoMatchmakingToggle.Header = _text.Get(UiTextKeys.LeagueAutoMatchmaking);
        LeagueAutoMatchmakingHint.Text = _text.Get(UiTextKeys.LeagueAutoMatchmakingHint);
        LeagueAutoAcceptToggle.Header = _text.Get(UiTextKeys.LeagueAutoAccept);
        LeagueAutoAcceptHint.Text = _text.Get(UiTextKeys.LeagueAutoAcceptHint);

        AutomationProperties.SetName(LeagueAutoMatchmakingToggle, _text.Get(UiTextKeys.LeagueAutoMatchmaking));
        AutomationProperties.SetHelpText(LeagueAutoMatchmakingToggle, _text.Get(UiTextKeys.LeagueAutoMatchmakingHint));
        AutomationProperties.SetName(LeagueAutoAcceptToggle, _text.Get(UiTextKeys.LeagueAutoAccept));
        AutomationProperties.SetHelpText(LeagueAutoAcceptToggle, _text.Get(UiTextKeys.LeagueAutoAcceptHint));

        if (_postGameAutomationSettings is null && Application.Current is App app)
        {
            try { ConfigureLeaguePostGameAutomation(app.CreateLeaguePostGameAutomationSettingsViewModel()); }
            catch { }
        }

        ApplyLeagueAutomationSettingsSurface();
    }

    private void ConfigureLeaguePostGameAutomation(LeaguePostGameAutomationSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (_postGameAutomationSettings is not null)
        {
            viewModel.Dispose();
            return;
        }

        _postGameAutomationSettings = viewModel;
        _postGameAutomationSettings.PropertyChanged += OnLeaguePostGameAutomationPropertyChanged;
        Closed += OnLeaguePostGameUiClosed;
        EnsureLeaguePostGameControls();
        ApplyLeagueAutomationSettingsSurface();
    }

    private void EnsureLeaguePostGameControls()
    {
        if (_leagueAutoHonorToggle is not null) return;
        if (LeagueAutomationSettingsStatus.Parent is not Border settingsStatusBorder ||
            settingsStatusBorder.Parent is not StackPanel automationStack)
            return;

        _leagueAutoHonorToggle = new ToggleSwitch { Header = "自动点赞队友" };
        AutomationProperties.SetAutomationId(_leagueAutoHonorToggle, "FACM.League.AutoHonor");
        AutomationProperties.SetName(_leagueAutoHonorToggle, "自动点赞队友");
        AutomationProperties.SetHelpText(
            _leagueAutoHonorToggle,
            "赛后有可用荣誉票时，从可点赞队友中选择一名；提交后会再次读取确认。" );
        _leagueAutoHonorToggle.Toggled += OnLeagueAutoHonorToggled;

        _leagueAutoHonorHint = new TextBlock
        {
            Text = "沿用 3.5 的一次赛后 cycle、排除自己/机器人/已点赞队友，以及 V2 → legacy fallback。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };

        _leagueAutoReturnLobbyToggle = new ToggleSwitch { Header = "自动返回大厅" };
        AutomationProperties.SetAutomationId(_leagueAutoReturnLobbyToggle, "FACM.League.AutoReturnLobby");
        AutomationProperties.SetName(_leagueAutoReturnLobbyToggle, "自动返回大厅");
        AutomationProperties.SetHelpText(
            _leagueAutoReturnLobbyToggle,
            "赛后按当前阶段的受控延时返回大厅；继续复用唯一 League gameflow heartbeat。" );
        _leagueAutoReturnLobbyToggle.Toggled += OnLeagueAutoReturnLobbyToggled;

        _leagueAutoReturnLobbyHint = new TextBlock
        {
            Text = "不会创建第二套 phase 轮询；关闭主界面后，已启用的赛后自动化仍由 FACM 进程继续托管。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };

        _leaguePostGameStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leaguePostGameStatus, "FACM.League.PostGameAutomationStatus");

        var insertIndex = automationStack.Children.IndexOf(settingsStatusBorder);
        if (insertIndex < 0) insertIndex = automationStack.Children.Count;
        automationStack.Children.Insert(insertIndex++, _leagueAutoHonorToggle);
        automationStack.Children.Insert(insertIndex++, _leagueAutoHonorHint);
        automationStack.Children.Insert(insertIndex++, _leagueAutoReturnLobbyToggle);
        automationStack.Children.Insert(insertIndex++, _leagueAutoReturnLobbyHint);
        automationStack.Children.Insert(insertIndex, _leaguePostGameStatus);
    }

    private void ApplyLeagueAutomationSettingsSurface()
    {
        if (_closed) return;

        _leagueAutomationUiApplying = true;
        try
        {
            LeagueAutoMatchmakingToggle.IsOn = _leagueWorkbench.AutoMatchmakingEnabled;
            LeagueAutoAcceptToggle.IsOn = _leagueWorkbench.AutoAcceptEnabled;
            var enabled = _leagueWorkbench.HasMatchmakingAutomation && !_leagueWorkbench.IsAutomationSettingsBusy;
            LeagueAutoMatchmakingToggle.IsEnabled = enabled;
            LeagueAutoAcceptToggle.IsEnabled = enabled;
            LeagueAutomationSettingsStatus.Text = _text.Get(_leagueAutomationStatusTextKey);

            var postGame = _postGameAutomationSettings;
            if (_leagueAutoHonorToggle is not null)
            {
                _leagueAutoHonorToggle.IsOn = postGame?.AutoHonorEnabled ?? false;
                _leagueAutoHonorToggle.IsEnabled = postGame is not null && !postGame.IsBusy;
            }
            if (_leagueAutoReturnLobbyToggle is not null)
            {
                _leagueAutoReturnLobbyToggle.IsOn = postGame?.AutoReturnLobbyEnabled ?? false;
                _leagueAutoReturnLobbyToggle.IsEnabled = postGame is not null && !postGame.IsBusy;
            }
            if (_leaguePostGameStatus is not null)
                _leaguePostGameStatus.Text = BuildLeaguePostGameStatus(postGame);
        }
        finally
        {
            _leagueAutomationUiApplying = false;
        }
    }

    private async void OnLeagueAutoMatchmakingToggled(object sender, RoutedEventArgs args)
    {
        if (_leagueAutomationUiApplying || _closed || _leagueWorkbench.IsAutomationSettingsBusy) return;
        var requested = LeagueAutoMatchmakingToggle.IsOn;
        var saved = false;
        try
        {
            saved = await _leagueWorkbench.SetAutoMatchmakingEnabledAsync(requested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _leagueAutomationStatusTextKey = saved
                ? UiTextKeys.LeagueAutomationSettingsSaved
                : UiTextKeys.LeagueAutomationSettingsFailed;
            ApplyLeagueAutomationSettingsSurface();
        }
    }

    private async void OnLeagueAutoAcceptToggled(object sender, RoutedEventArgs args)
    {
        if (_leagueAutomationUiApplying || _closed || _leagueWorkbench.IsAutomationSettingsBusy) return;
        var requested = LeagueAutoAcceptToggle.IsOn;
        var saved = false;
        try
        {
            saved = await _leagueWorkbench.SetAutoAcceptEnabledAsync(requested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _leagueAutomationStatusTextKey = saved
                ? UiTextKeys.LeagueAutomationSettingsSaved
                : UiTextKeys.LeagueAutomationSettingsFailed;
            ApplyLeagueAutomationSettingsSurface();
        }
    }

    private async void OnLeagueAutoHonorToggled(object sender, RoutedEventArgs args)
    {
        var postGame = _postGameAutomationSettings;
        if (_leagueAutomationUiApplying || _closed || postGame is null || postGame.IsBusy ||
            _leagueAutoHonorToggle is null)
            return;

        try { _ = await postGame.SetAutoHonorEnabledAsync(_leagueAutoHonorToggle.IsOn); }
        catch (OperationCanceledException) { }
        finally { ApplyLeagueAutomationSettingsSurface(); }
    }

    private async void OnLeagueAutoReturnLobbyToggled(object sender, RoutedEventArgs args)
    {
        var postGame = _postGameAutomationSettings;
        if (_leagueAutomationUiApplying || _closed || postGame is null || postGame.IsBusy ||
            _leagueAutoReturnLobbyToggle is null)
            return;

        try { _ = await postGame.SetAutoReturnLobbyEnabledAsync(_leagueAutoReturnLobbyToggle.IsOn); }
        catch (OperationCanceledException) { }
        finally { ApplyLeagueAutomationSettingsSurface(); }
    }

    private void OnLeaguePostGameAutomationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) ApplyLeagueAutomationSettingsSurface();
        });
    }

    private static string BuildLeaguePostGameStatus(LeaguePostGameAutomationSettingsViewModel? viewModel)
    {
        if (viewModel is null) return "赛后自动化暂不可用。";
        if (viewModel.IsBusy) return "正在保存赛后自动化设置…";
        if (viewModel.RecoveryReadOnly) return "设置处于恢复只读模式，本次开关未覆盖损坏的主设置文件。";

        var status = viewModel.LastHonorStatus;
        if (status is null) return "赛后自动化已就绪。";
        var state = status.State switch
        {
            "success" => "点赞已确认",
            "skipped" => "本局未执行点赞",
            "unknown" => "点赞已提交但未能权威确认",
            "failed" => "点赞失败",
            _ => status.State
        };
        var target = string.IsNullOrWhiteSpace(status.TargetPuuidSuffix)
            ? string.Empty
            : " · 目标 " + status.TargetPuuidSuffix;
        return state + " · " + status.Route + " · " + status.Detail + target;
    }

    private void OnLeaguePostGameUiClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnLeaguePostGameUiClosed;
        if (_postGameAutomationSettings is not null)
        {
            _postGameAutomationSettings.PropertyChanged -= OnLeaguePostGameAutomationPropertyChanged;
            _postGameAutomationSettings.Dispose();
            _postGameAutomationSettings = null;
        }

        if (LeagueAutomationSettingsStatus.Parent is Border settingsStatusBorder &&
            settingsStatusBorder.Parent is StackPanel automationStack)
        {
            if (_leagueAutoHonorToggle is not null) automationStack.Children.Remove(_leagueAutoHonorToggle);
            if (_leagueAutoHonorHint is not null) automationStack.Children.Remove(_leagueAutoHonorHint);
            if (_leagueAutoReturnLobbyToggle is not null) automationStack.Children.Remove(_leagueAutoReturnLobbyToggle);
            if (_leagueAutoReturnLobbyHint is not null) automationStack.Children.Remove(_leagueAutoReturnLobbyHint);
            if (_leaguePostGameStatus is not null) automationStack.Children.Remove(_leaguePostGameStatus);
        }

        if (_leagueAutoHonorToggle is not null) _leagueAutoHonorToggle.Toggled -= OnLeagueAutoHonorToggled;
        if (_leagueAutoReturnLobbyToggle is not null) _leagueAutoReturnLobbyToggle.Toggled -= OnLeagueAutoReturnLobbyToggled;
        _leagueAutoHonorToggle = null;
        _leagueAutoHonorHint = null;
        _leagueAutoReturnLobbyToggle = null;
        _leagueAutoReturnLobbyHint = null;
        _leaguePostGameStatus = null;
    }
}
