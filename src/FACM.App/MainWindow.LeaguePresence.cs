using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private ILeaguePresenceService? _leaguePresence;
    private bool _leaguePresenceBusy;
    private TextBlock? _leaguePresenceTitle;
    private TextBlock? _leaguePresenceCurrent;
    private TextBlock? _leaguePresenceStatus;
    private Button? _leaguePresenceRefresh;
    private Grid? _leaguePresenceChoices;
    private readonly List<Button> _leaguePresenceButtons = [];

    private void InitializeLeaguePresenceSurface()
    {
        if (_leaguePresence is not null) return;
        if (Application.Current is not App app) return;
        try { _leaguePresence = app.CreateLeaguePresenceService(); }
        catch { return; }

        EnsureLeaguePresenceControls();
        Closed += OnLeaguePresenceClosed;
        if (IsLeagueWorkbenchSelected()) _ = RefreshLeaguePresenceAsync();
    }

    private void EnsureLeaguePresenceControls()
    {
        if (_leaguePresenceTitle is not null) return;
        if (LeagueAutomationSettingsStatus.Parent is not Border settingsStatusBorder ||
            settingsStatusBorder.Parent is not StackPanel automationStack)
            return;

        _leaguePresenceTitle = new TextBlock
        {
            Text = "在线状态",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        };

        var hint = new TextBlock
        {
            Text = "按既有方式修改 League 聊天在线状态。每次点击只发送一次 PUT，再读取两次确认；如果客户端覆盖，GGman 不会循环抢写。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };

        _leaguePresenceCurrent = new TextBlock
        {
            Text = "当前：等待读取",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leaguePresenceCurrent, "FACM.League.PresenceCurrent");

        _leaguePresenceRefresh = new Button
        {
            Content = "刷新在线状态",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetAutomationId(_leaguePresenceRefresh, "FACM.League.PresenceRefresh");
        _leaguePresenceRefresh.Click += OnLeaguePresenceRefreshClick;

        _leaguePresenceChoices = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        _leaguePresenceChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _leaguePresenceChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 3; row++)
            _leaguePresenceChoices.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddPresenceChoice("在线", LeaguePresenceMode.Online, 0, 0);
        AddPresenceChoice("离开", LeaguePresenceMode.Away, 0, 1);
        AddPresenceChoice("请勿打扰", LeaguePresenceMode.DoNotDisturb, 1, 0);
        AddPresenceChoice("手机在线", LeaguePresenceMode.Mobile, 1, 1);
        AddPresenceChoice("离线", LeaguePresenceMode.Offline, 2, 0);
        AddPresenceChoice("显示游戏中", LeaguePresenceMode.DisplayInGame, 2, 1);

        _leaguePresenceStatus = new TextBlock
        {
            Text = "在线状态控制已就绪。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leaguePresenceStatus, "FACM.League.PresenceStatus");

        var insertIndex = automationStack.Children.IndexOf(settingsStatusBorder);
        if (insertIndex < 0) insertIndex = automationStack.Children.Count;
        automationStack.Children.Insert(insertIndex++, _leaguePresenceTitle);
        automationStack.Children.Insert(insertIndex++, hint);
        automationStack.Children.Insert(insertIndex++, _leaguePresenceCurrent);
        automationStack.Children.Insert(insertIndex++, _leaguePresenceRefresh);
        automationStack.Children.Insert(insertIndex++, _leaguePresenceChoices);
        automationStack.Children.Insert(insertIndex, _leaguePresenceStatus);
    }

    private void AddPresenceChoice(string text, LeaguePresenceMode mode, int row, int column)
    {
        var grid = _leaguePresenceChoices;
        if (grid is null) return;
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = mode
        };
        AutomationProperties.SetAutomationId(button, "FACM.League.Presence." + mode);
        AutomationProperties.SetName(button, text);
        button.Click += OnLeaguePresenceChoiceClick;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        _leaguePresenceButtons.Add(button);
    }

    private async void OnLeaguePresenceRefreshClick(object sender, RoutedEventArgs args)
    {
        await RefreshLeaguePresenceAsync();
    }

    private async Task RefreshLeaguePresenceAsync()
    {
        var service = _leaguePresence;
        if (_closed || service is null || _leaguePresenceBusy) return;
        SetLeaguePresenceBusy(true);
        try
        {
            var snapshot = await service.ReadAsync();
            if (_closed) return;
            ApplyLeaguePresenceSnapshot(snapshot);
            if (_leaguePresenceStatus is not null)
                _leaguePresenceStatus.Text = snapshot.Connected ? "在线状态已刷新。" : "League 客户端未连接，在线状态暂不可用。";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_leaguePresenceStatus is not null) _leaguePresenceStatus.Text = "读取在线状态失败。";
        }
        finally
        {
            SetLeaguePresenceBusy(false);
        }
    }

    private async void OnLeaguePresenceChoiceClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: LeaguePresenceMode mode } || _closed || _leaguePresenceBusy) return;
        var service = _leaguePresence;
        if (service is null) return;

        SetLeaguePresenceBusy(true);
        try
        {
            if (_leaguePresenceStatus is not null) _leaguePresenceStatus.Text = "正在应用在线状态…";
            var result = await service.ApplyAsync(mode);
            if (_closed) return;
            if (result.Observed is not null) ApplyLeaguePresenceSnapshot(result.Observed);
            if (_leaguePresenceStatus is not null)
            {
                _leaguePresenceStatus.Text = result.Status switch
                {
                    "success" => "在线状态已应用并确认。",
                    "overridden" => "League 客户端覆盖了该状态；GGman 没有继续循环抢写。",
                    "unavailable" => "League 客户端未连接，无法修改在线状态。",
                    _ => "在线状态写入失败。"
                };
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_leaguePresenceStatus is not null) _leaguePresenceStatus.Text = "在线状态写入失败。";
        }
        finally
        {
            SetLeaguePresenceBusy(false);
        }
    }

    private void ApplyLeaguePresenceSnapshot(LeaguePresenceSnapshot snapshot)
    {
        if (_leaguePresenceCurrent is null) return;
        _leaguePresenceCurrent.Text = snapshot.Connected
            ? "当前：" + DisplayPresenceMode(snapshot)
            : "当前：不可用";
    }

    private static string DisplayPresenceMode(LeaguePresenceSnapshot snapshot)
    {
        if (string.Equals(snapshot.GameStatus, "inGame", StringComparison.OrdinalIgnoreCase)) return "显示游戏中";
        return (snapshot.Availability ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "chat" or "online" => "在线",
            "away" => "离开",
            "dnd" => "请勿打扰",
            "mobile" => "手机在线",
            "offline" => "离线",
            { Length: > 0 } value => value,
            _ => "未知"
        };
    }

    private void SetLeaguePresenceBusy(bool busy)
    {
        _leaguePresenceBusy = busy;
        if (_leaguePresenceRefresh is not null) _leaguePresenceRefresh.IsEnabled = !busy;
        foreach (var button in _leaguePresenceButtons) button.IsEnabled = !busy;
    }

    private void OnLeaguePresenceClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnLeaguePresenceClosed;
        if (_leaguePresenceRefresh is not null) _leaguePresenceRefresh.Click -= OnLeaguePresenceRefreshClick;
        foreach (var button in _leaguePresenceButtons) button.Click -= OnLeaguePresenceChoiceClick;
        _leaguePresenceButtons.Clear();
        _leaguePresence = null;
        _leaguePresenceTitle = null;
        _leaguePresenceCurrent = null;
        _leaguePresenceStatus = null;
        _leaguePresenceRefresh = null;
        _leaguePresenceChoices = null;
    }
}
