using System.Globalization;
using System.Text;
using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private Border? _leagueProductActionsCard;
    private TextBlock? _leagueAdvisorStatus;
    private Button? _leagueAdvisorRefreshButton;
    private Button? _leagueItemSetApplyButton;

    private void InitializeLeagueWorkbenchProductActions()
    {
        if (_leagueProductActionsCard is not null) return;

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "OP.GG 推荐与装备集",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "只在你打开 LOL 工作台或手动刷新时读取推荐；进入游戏后只使用已有缓存。应用装备集前会再次确认。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        _leagueAdvisorStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        content.Children.Add(_leagueAdvisorStatus);

        _leagueAdvisorRefreshButton = new Button
        {
            Content = "刷新 OP.GG 推荐",
            Style = (Style)Application.Current.Resources["FacmPrimaryButtonStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueAdvisorRefreshButton, "FACM.League.RefreshBuildAdvisor");
        AutomationProperties.SetName(_leagueAdvisorRefreshButton, "刷新 OP.GG 推荐");
        _leagueAdvisorRefreshButton.Click += OnLeagueAdvisorRefreshClicked;

        _leagueItemSetApplyButton = new Button
        {
            Content = "应用推荐装备集"
        };
        AutomationProperties.SetAutomationId(_leagueItemSetApplyButton, "FACM.League.ApplyItemSet");
        AutomationProperties.SetName(_leagueItemSetApplyButton, "应用推荐装备集");
        AutomationProperties.SetHelpText(_leagueItemSetApplyButton, "先生成预览，确认后才写入 League 推荐装备集目录。");
        _leagueItemSetApplyButton.Click += OnLeagueItemSetApplyClicked;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        actions.Children.Add(_leagueAdvisorRefreshButton);
        actions.Children.Add(_leagueItemSetApplyButton);
        content.Children.Add(actions);

        card.Child = content;
        _leagueProductActionsCard = card;
        LeagueWorkbenchPanel.Children.Add(card);
        SyncLeagueWorkbenchProductActions();
    }

    private void SyncLeagueWorkbenchProductActions()
    {
        if (_leagueAdvisorStatus is null ||
            _leagueAdvisorRefreshButton is null ||
            _leagueItemSetApplyButton is null)
            return;

        _leagueAdvisorStatus.Text = BuildAdvisorSummary(
            _leagueWorkbench.Advisor,
            _leagueWorkbench.IsAdvisorRefreshing,
            _leagueWorkbench.ItemSetStatus);

        var serviceReady = _leagueWorkbench.HasProductServices;
        _leagueAdvisorRefreshButton.IsEnabled =
            serviceReady && !_leagueWorkbench.IsAdvisorRefreshing && !_leagueWorkbench.IsItemSetBusy;
        _leagueItemSetApplyButton.IsEnabled =
            serviceReady && _leagueWorkbench.CanPrepareItemSet && !_leagueWorkbench.IsAdvisorRefreshing;
    }

    private async void OnLeagueAdvisorRefreshClicked(object sender, RoutedEventArgs args)
    {
        if (_closed || !_leagueWorkbench.HasProductServices || _leagueWorkbench.IsAdvisorRefreshing) return;
        try
        {
            await _leagueWorkbench.RefreshBuildAdvisorAsync(force: true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SyncLeagueWorkbenchProductActions();
        }
    }

    private async void OnLeagueItemSetApplyClicked(object sender, RoutedEventArgs args)
    {
        if (_closed || !_leagueWorkbench.CanPrepareItemSet) return;

        LeagueItemSetPlan? plan;
        try
        {
            plan = await _leagueWorkbench.PrepareItemSetAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            SyncLeagueWorkbenchProductActions();
        }
        if (plan is null || _closed) return;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "应用推荐装备集？",
            Content = BuildItemSetConfirmation(plan),
            PrimaryButtonText = "应用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return;

        LeagueItemSetApplyResult? result;
        try
        {
            result = await _leagueWorkbench.ApplyItemSetAsync(plan);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            SyncLeagueWorkbenchProductActions();
        }
        if (result is null || _closed) return;

        var resultDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = result.Succeeded ? "装备集已应用" : "装备集未应用",
            Content = BuildItemSetResult(result),
            CloseButtonText = "确定"
        };
        await resultDialog.ShowAsync();
    }

    private static string BuildAdvisorSummary(
        LeagueBuildAdvisorSnapshot advisor,
        bool refreshing,
        string itemSetStatus)
    {
        if (refreshing) return "正在读取 OP.GG 推荐…";

        var stateText = advisor.State switch
        {
            LeagueBuildAdvisorState.WaitingChampion => "等待你在选人阶段确定英雄。",
            LeagueBuildAdvisorState.UnsupportedMode => "当前模式暂不支持 OP.GG 推荐。",
            LeagueBuildAdvisorState.WaitingChampSelect => "推荐会在选人阶段可用。",
            LeagueBuildAdvisorState.InGameCache => "已进入游戏，当前只显示选人阶段留下的缓存推荐。",
            LeagueBuildAdvisorState.InGameNoCache => "已进入游戏，但本局没有可用的推荐缓存；不会在游戏中请求 OP.GG。",
            LeagueBuildAdvisorState.ProviderUnavailable => "OP.GG 当前不可用，可稍后手动刷新。",
            LeagueBuildAdvisorState.Timeout => "OP.GG 请求超时，可稍后重试。",
            LeagueBuildAdvisorState.Unavailable => "League 客户端未连接，或推荐数据暂不可用。",
            LeagueBuildAdvisorState.Ready => string.Empty,
            _ => "推荐数据暂不可用。"
        };
        if (advisor.State != LeagueBuildAdvisorState.Ready && advisor.State != LeagueBuildAdvisorState.InGameCache)
            return stateText;

        var builder = new StringBuilder();
        builder.Append(advisor.FromCache ? "推荐：缓存" : "推荐：最新")
            .Append(" · ")
            .Append(string.IsNullOrWhiteSpace(advisor.ChampionName)
                ? "英雄 " + advisor.ChampionId.ToString(CultureInfo.InvariantCulture)
                : advisor.ChampionName)
            .Append(" · ")
            .Append(Fallback(advisor.Mode, "未知模式"));
        if (!string.IsNullOrWhiteSpace(advisor.Position) && !string.Equals(advisor.Position, "none", StringComparison.OrdinalIgnoreCase))
            builder.Append(" / ").Append(advisor.Position);
        if (!string.IsNullOrWhiteSpace(advisor.Version)) builder.Append(" · ").Append(advisor.Version);

        if (advisor.Recommendation is { } recommendation)
        {
            if (!string.IsNullOrWhiteSpace(recommendation.Tier))
                builder.Append(" · ").Append(recommendation.Tier);
            if (recommendation.WinRate is { } winRate)
            {
                var percent = winRate <= 1d ? winRate * 100d : winRate;
                builder.Append(" · 胜率 ").Append(percent.ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
            }

            foreach (var row in recommendation.Rows.Take(5))
            {
                builder.AppendLine();
                builder.Append(FormatAdvisorCategory(row.Category)).Append("：").Append(row.Recommendation);
                if (!string.IsNullOrWhiteSpace(row.Evidence)) builder.Append(" · ").Append(row.Evidence);
            }
        }

        if (!string.IsNullOrWhiteSpace(itemSetStatus) && !string.Equals(itemSetStatus, "not-ready", StringComparison.Ordinal))
            builder.AppendLine().Append("装备集：").Append(FormatItemSetStatus(itemSetStatus));
        return builder.ToString();
    }

    private static string BuildItemSetConfirmation(LeagueItemSetPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("英雄：")
            .Append(string.IsNullOrWhiteSpace(plan.ChampionName)
                ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
                : plan.ChampionName)
            .AppendLine()
            .Append("推荐：").Append(plan.Blocks.Count).Append(" 组 / ").Append(plan.ItemCount).Append(" 件装备")
            .AppendLine()
            .Append("模式：").Append(plan.Mode).Append(" / ").Append(plan.Position)
            .AppendLine().AppendLine()
            .Append("确认后 FACM 才会写入 League 的推荐装备集目录；只管理 FACM 4 自己生成的 facm4-*.json，不会删除旧版装备集。");
        return builder.ToString();
    }

    private static string BuildItemSetResult(LeagueItemSetApplyResult result)
    {
        if (result.Succeeded)
        {
            var text = "已写入：" + result.FileName;
            if (result.RemovedOldFiles > 0) text += Environment.NewLine + "已清理旧 FACM 4 装备集：" + result.RemovedOldFiles;
            if (result.CleanupWarning) text += Environment.NewLine + "旧文件清理有部分失败，但新装备集已写入。";
            return text;
        }

        return result.Detail switch
        {
            "champ-select-required" => "当前已不在选人阶段，因此取消写入。",
            "champion-changed" => "英雄已经变化，因此取消写入。",
            "queue-changed" => "队列已经变化，因此取消写入。",
            "install-layout-unavailable" => "未找到受支持的 League 推荐装备集目录。",
            _ => "未写入装备集：" + result.Detail
        };
    }

    private static string FormatAdvisorCategory(string category) => category switch
    {
        "summoner-spells" => "召唤师技能",
        "runes" => "符文",
        "starter-items" => "出门装",
        "boots" => "鞋子",
        "core-items" => "核心装",
        "skills" => "技能顺序",
        "counters" => "对位参考",
        _ => category
    };

    private static string FormatItemSetStatus(string status) => status switch
    {
        "prepared" => "已生成预览，等待确认",
        "prepare-unavailable" => "当前推荐无法生成装备集",
        "prepare-failed" => "生成装备集失败",
        "success" => "已应用",
        "champ-select-required" => "已离开选人阶段，未写入",
        "champion-changed" => "英雄已变化，未写入",
        "queue-changed" => "队列已变化，未写入",
        "apply-failed" => "应用失败",
        _ => status
    };

    private void DisposeLeagueWorkbenchProductActions()
    {
        if (_leagueAdvisorRefreshButton is not null)
            _leagueAdvisorRefreshButton.Click -= OnLeagueAdvisorRefreshClicked;
        if (_leagueItemSetApplyButton is not null)
            _leagueItemSetApplyButton.Click -= OnLeagueItemSetApplyClicked;
        if (_leagueProductActionsCard is not null)
            LeagueWorkbenchPanel.Children.Remove(_leagueProductActionsCard);

        _leagueProductActionsCard = null;
        _leagueAdvisorStatus = null;
        _leagueAdvisorRefreshButton = null;
        _leagueItemSetApplyButton = null;
    }
}
