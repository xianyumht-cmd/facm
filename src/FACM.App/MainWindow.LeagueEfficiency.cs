using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private ILeagueEfficiencyRuntime? _leagueEfficiencyRuntime;
    private Border? _leagueEfficiencyCard;
    private TextBox? _leagueExitGameHotkey;
    private TextBox? _leagueCloseLobbyHotkey;
    private Button? _leagueEfficiencySaveButton;
    private TextBlock? _leagueEfficiencyStatus;

    private void InitializeLeagueEfficiencySurface()
    {
        if (_leagueEfficiencyCard is not null) return;

        if (Application.Current is App app)
        {
            try { _leagueEfficiencyRuntime = app.GetLeagueEfficiencyRuntime(); }
            catch { }
        }

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "效率工具与全局快捷键",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "迁移自 FACM 3.5.15。空白表示禁用；裸字母/数字会被拒绝，F1-F12 可单独使用。注册失败时会恢复上一组快捷键，不会留下半套配置。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        content.Children.Add(BuildLeagueEfficiencyHotkeyRow(
            "结束游戏快捷键",
            "只结束 League of Legends 游戏进程，不结束大厅客户端。",
            "FACM.League.ExitGameHotkey",
            out _leagueExitGameHotkey));
        content.Children.Add(BuildLeagueEfficiencyHotkeyRow(
            "关闭大厅快捷键",
            "只结束 LeagueClient / LeagueClientUx / LeagueClientUxRender。",
            "FACM.League.CloseLobbyHotkey",
            out _leagueCloseLobbyHotkey));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        _leagueEfficiencySaveButton = new Button
        {
            Content = "保存快捷键",
            Style = (Style)Application.Current.Resources["FacmPrimaryButtonStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueEfficiencySaveButton, "FACM.League.SaveEfficiencyHotkeys");
        AutomationProperties.SetName(_leagueEfficiencySaveButton, "保存 League 效率快捷键");
        _leagueEfficiencySaveButton.Click += OnLeagueEfficiencySaveClicked;
        actions.Children.Add(_leagueEfficiencySaveButton);

        var clearButton = new Button { Content = "全部禁用" };
        AutomationProperties.SetAutomationId(clearButton, "FACM.League.ClearEfficiencyHotkeys");
        AutomationProperties.SetName(clearButton, "禁用全部 League 效率快捷键");
        clearButton.Click += OnLeagueEfficiencyClearClicked;
        actions.Children.Add(clearButton);
        content.Children.Add(actions);

        _leagueEfficiencyStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueEfficiencyStatus, "FACM.League.EfficiencyStatus");
        content.Children.Add(_leagueEfficiencyStatus);

        card.Child = content;
        _leagueEfficiencyCard = card;
        LeagueWorkbenchPanel.Children.Add(card);

        if (_leagueEfficiencyRuntime is not null)
            _leagueEfficiencyRuntime.StateChanged += OnLeagueEfficiencyStateChanged;
        SyncLeagueEfficiencySurface();
    }

    private StackPanel BuildLeagueEfficiencyHotkeyRow(
        string title,
        string hint,
        string automationId,
        out TextBox textBox)
    {
        var row = new StackPanel { Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        });
        row.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        textBox = new TextBox
        {
            PlaceholderText = "例如 Ctrl+F9；留空即禁用",
            MaxLength = 128,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(textBox, automationId);
        AutomationProperties.SetName(textBox, title);
        AutomationProperties.SetHelpText(textBox, "支持 Ctrl / Alt / Shift / Win 与一个主按键；留空禁用。");
        row.Children.Add(textBox);
        return row;
    }

    private async void OnLeagueEfficiencySaveClicked(object sender, RoutedEventArgs args)
    {
        var runtime = _leagueEfficiencyRuntime;
        if (_closed || runtime is null || runtime.State.IsBusy ||
            _leagueExitGameHotkey is null || _leagueCloseLobbyHotkey is null)
            return;

        try
        {
            _ = await runtime.UpdateBindingsAsync(
                _leagueExitGameHotkey.Text,
                _leagueCloseLobbyHotkey.Text);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Runtime state already records a stable failure reason. Keep the rest of the Workbench usable.
        }
        finally
        {
            SyncLeagueEfficiencySurface();
        }
    }

    private async void OnLeagueEfficiencyClearClicked(object sender, RoutedEventArgs args)
    {
        var runtime = _leagueEfficiencyRuntime;
        if (_closed || runtime is null || runtime.State.IsBusy) return;

        try
        {
            _ = await runtime.UpdateBindingsAsync(string.Empty, string.Empty);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            SyncLeagueEfficiencySurface();
        }
    }

    private void OnLeagueEfficiencyStateChanged(object? sender, LeagueEfficiencyRuntimeStateChangedEventArgs args)
    {
        if (_closed) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) SyncLeagueEfficiencySurface();
        });
    }

    private void SyncLeagueEfficiencySurface()
    {
        if (_leagueEfficiencyStatus is null) return;
        var runtime = _leagueEfficiencyRuntime;
        if (runtime is null)
        {
            _leagueEfficiencyStatus.Text = "全局快捷键当前不可用；LOL 工作台其它功能不受影响。";
            if (_leagueEfficiencySaveButton is not null) _leagueEfficiencySaveButton.IsEnabled = false;
            if (_leagueExitGameHotkey is not null) _leagueExitGameHotkey.IsEnabled = false;
            if (_leagueCloseLobbyHotkey is not null) _leagueCloseLobbyHotkey.IsEnabled = false;
            return;
        }

        var state = runtime.State;
        if (_leagueExitGameHotkey is not null && _leagueExitGameHotkey.FocusState != FocusState.Keyboard)
            _leagueExitGameHotkey.Text = state.ExitGameHotkey;
        if (_leagueCloseLobbyHotkey is not null && _leagueCloseLobbyHotkey.FocusState != FocusState.Keyboard)
            _leagueCloseLobbyHotkey.Text = state.CloseLobbyHotkey;

        if (_leagueEfficiencySaveButton is not null) _leagueEfficiencySaveButton.IsEnabled = !state.IsBusy;
        if (_leagueExitGameHotkey is not null) _leagueExitGameHotkey.IsEnabled = !state.IsBusy;
        if (_leagueCloseLobbyHotkey is not null) _leagueCloseLobbyHotkey.IsEnabled = !state.IsBusy;
        _leagueEfficiencyStatus.Text = FormatLeagueEfficiencyState(state);
    }

    private static string FormatLeagueEfficiencyState(LeagueEfficiencyRuntimeState state)
    {
        var status = state.Status switch
        {
            "initializing" => "正在初始化全局快捷键…",
            "saving" => "正在注册并保存快捷键…",
            "hotkey-invalid" => "快捷键格式无效",
            "hotkey-unavailable" => "快捷键被系统或其它程序占用",
            "running" => "正在执行效率操作…",
            "failed" => "效率工具操作失败",
            _ => "全局快捷键已就绪"
        };
        if (state.IsRecoveryReadOnly)
            status += " · 恢复模式：本次会话可用但不覆盖主设置";
        if (!string.IsNullOrWhiteSpace(state.Detail) &&
            !string.Equals(state.Detail, "registered", StringComparison.OrdinalIgnoreCase))
            status += " · " + state.Detail;
        return status;
    }

    private void DisposeLeagueEfficiencySurface()
    {
        if (_leagueEfficiencyRuntime is not null)
            _leagueEfficiencyRuntime.StateChanged -= OnLeagueEfficiencyStateChanged;
        _leagueEfficiencyRuntime = null;
        _leagueEfficiencyCard = null;
        _leagueExitGameHotkey = null;
        _leagueCloseLobbyHotkey = null;
        _leagueEfficiencySaveButton = null;
        _leagueEfficiencyStatus = null;
    }
}
