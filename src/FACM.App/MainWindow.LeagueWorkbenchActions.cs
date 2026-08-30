using System.ComponentModel;
using System.Globalization;
using System.Text;
using FACM.App.ViewModels;
using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private Border? _leagueProductActionsCard;
    private TextBlock? _leagueAdvisorStatus;
    private TextBlock? _leagueLoadoutStatus;
    private Button? _leagueAdvisorRefreshButton;
    private Button? _leagueLoadoutApplyButton;
    private Button? _leagueItemSetApplyButton;
    private ILeagueBuildLoadoutService? _leagueLoadoutService;
    private LeagueRecommendedAutoApplySettingsViewModel? _recommendedAutoApplySettings;
    private ToggleSwitch? _recommendedAutoApplyToggle;
    private TextBlock? _recommendedAutoApplyStatus;
    private bool _recommendedAutoApplyUiApplying;
    private bool _leagueLoadoutBusy;

    private void InitializeLeagueWorkbenchProductActions()
    {
        if (_leagueProductActionsCard is not null) return;

        if (Application.Current is App app && _leagueWorkbench.DataSource is { } dataSource)
        {
            try { _leagueLoadoutService = app.CreateLeagueBuildLoadoutService(dataSource); }
            catch { }
            try
            {
                _recommendedAutoApplySettings = app.CreateLeagueRecommendedAutoApplySettingsViewModel();
                _recommendedAutoApplySettings.PropertyChanged += OnLeagueRecommendedAutoApplyPropertyChanged;
            }
            catch { }
        }

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "OP.GG 推荐与推荐配置",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "推荐只在选人阶段主动读取；进入游戏后严格使用已有缓存。符文/技能和装备集在真正写入前都会重新校验选人阶段、英雄和队列。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        _leagueAdvisorStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        content.Children.Add(_leagueAdvisorStatus);

        _leagueLoadoutStatus = new TextBlock
        {
            Text = "推荐符文/技能：等待可用推荐。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueLoadoutStatus, "FACM.League.LoadoutStatus");
        content.Children.Add(_leagueLoadoutStatus);

        _leagueAdvisorRefreshButton = new Button
        {
            Content = "刷新 OP.GG 推荐",
            Style = (Style)Application.Current.Resources["FacmPrimaryButtonStyle"]
        };
        AutomationProperties.SetAutomationId(_leagueAdvisorRefreshButton, "FACM.League.RefreshBuildAdvisor");
        AutomationProperties.SetName(_leagueAdvisorRefreshButton, "刷新 OP.GG 推荐");
        _leagueAdvisorRefreshButton.Click += OnLeagueAdvisorRefreshClicked;

        _leagueLoadoutApplyButton = new Button
        {
            Content = "应用推荐符文/技能"
        };
        AutomationProperties.SetAutomationId(_leagueLoadoutApplyButton, "FACM.League.ApplyRecommendedLoadout");
        AutomationProperties.SetName(_leagueLoadoutApplyButton, "应用推荐符文/技能");
        AutomationProperties.SetHelpText(
            _leagueLoadoutApplyButton,
            "先生成只读预览，明确确认后才写入 FACM 自有符文页和你的召唤师技能选择。" );
        _leagueLoadoutApplyButton.Click += OnLeagueLoadoutApplyClicked;

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
        actions.Children.Add(_leagueLoadoutApplyButton);
        actions.Children.Add(_leagueItemSetApplyButton);
        content.Children.Add(actions);

        _recommendedAutoApplyToggle = new ToggleSwitch
        {
            Header = "自动应用推荐配置"
        };
        AutomationProperties.SetAutomationId(_recommendedAutoApplyToggle, "FACM.League.AutoApplyRecommended");
        AutomationProperties.SetName(_recommendedAutoApplyToggle, "自动应用推荐配置");
        AutomationProperties.SetHelpText(
            _recommendedAutoApplyToggle,
            "复用唯一 gameflow heartbeat；选人上下文稳定后，每个推荐指纹最多执行一次符文/技能与装备集应用。" );
        _recommendedAutoApplyToggle.Toggled += OnLeagueRecommendedAutoApplyToggled;
        content.Children.Add(_recommendedAutoApplyToggle);

        content.Children.Add(new TextBlock
        {
            Text = "自动模式不会创建第二套 League 轮询。关闭主窗口后仍由 FACM 进程托管；英雄、队列或选人阶段变化会阻止过期写入。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        _recommendedAutoApplyStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        AutomationProperties.SetAutomationId(_recommendedAutoApplyStatus, "FACM.League.AutoApplyRecommendedStatus");
        content.Children.Add(_recommendedAutoApplyStatus);

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
            serviceReady && !_leagueWorkbench.IsAdvisorRefreshing && !_leagueWorkbench.IsItemSetBusy && !_leagueLoadoutBusy;
        _leagueItemSetApplyButton.IsEnabled =
            serviceReady && _leagueWorkbench.CanPrepareItemSet && !_leagueWorkbench.IsAdvisorRefreshing && !_leagueLoadoutBusy;

        if (_leagueLoadoutApplyButton is not null)
        {
            _leagueLoadoutApplyButton.IsEnabled =
                _leagueLoadoutService is not null &&
                !_leagueLoadoutBusy &&
                !_leagueWorkbench.IsAdvisorRefreshing &&
                !_leagueWorkbench.IsItemSetBusy &&
                _leagueWorkbench.Advisor.State == LeagueBuildAdvisorState.Ready &&
                _leagueWorkbench.Advisor.Recommendation is not null;
        }

        var auto = _recommendedAutoApplySettings;
        _recommendedAutoApplyUiApplying = true;
        try
        {
            if (_recommendedAutoApplyToggle is not null)
            {
                _recommendedAutoApplyToggle.IsOn = auto?.Enabled ?? false;
                _recommendedAutoApplyToggle.IsEnabled = auto is not null && !auto.IsBusy;
            }
            if (_recommendedAutoApplyStatus is not null)
                _recommendedAutoApplyStatus.Text = BuildRecommendedAutoApplyStatus(auto);
        }
        finally
        {
            _recommendedAutoApplyUiApplying = false;
        }
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
        catch
        {
        }
        finally
        {
            SyncLeagueWorkbenchProductActions();
        }
    }

    private async void OnLeagueLoadoutApplyClicked(object sender, RoutedEventArgs args)
    {
        var service = _leagueLoadoutService;
        var advisor = _leagueWorkbench.Advisor;
        if (_closed || service is null || _leagueLoadoutBusy ||
            advisor.State != LeagueBuildAdvisorState.Ready || advisor.Recommendation is null)
            return;

        _leagueLoadoutBusy = true;
        if (_leagueLoadoutStatus is not null) _leagueLoadoutStatus.Text = "正在生成符文/技能预览…";
        SyncLeagueWorkbenchProductActions();
        LeagueBuildLoadoutPlan? plan = null;
        try
        {
            plan = await service.PrepareAsync(advisor);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_leagueLoadoutStatus is not null) _leagueLoadoutStatus.Text = "推荐符文/技能预览失败。";
        }
        finally
        {
            _leagueLoadoutBusy = false;
            SyncLeagueWorkbenchProductActions();
        }
        if (plan is null || _closed)
        {
            if (_leagueLoadoutStatus is not null) _leagueLoadoutStatus.Text = "当前推荐没有可应用的符文或召唤师技能。";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "应用推荐符文/技能？",
            Content = BuildLoadoutConfirmation(plan),
            PrimaryButtonText = "应用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        try
        {
            using var outsideCloseSuppression = SuppressOutsideClose();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        _leagueLoadoutBusy = true;
        if (_leagueLoadoutStatus is not null) _leagueLoadoutStatus.Text = "正在应用并回读确认…";
        SyncLeagueWorkbenchProductActions();
        LeagueBuildLoadoutApplyResult? result = null;
        try
        {
            result = await service.ApplyAsync(plan);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_leagueLoadoutStatus is not null) _leagueLoadoutStatus.Text = "推荐符文/技能应用失败。";
        }
        finally
        {
            _leagueLoadoutBusy = false;
            SyncLeagueWorkbenchProductActions();
        }
        if (result is null || _closed) return;

        if (_leagueLoadoutStatus is not null)
            _leagueLoadoutStatus.Text = FormatLoadoutResult(result);
        var resultDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase)
                ? "推荐配置已应用"
                : "推荐配置未完全应用",
            Content = FormatLoadoutResult(result),
            CloseButtonText = "确定"
        };
        try
        {
            using var outsideCloseSuppression = SuppressOutsideClose();
            await resultDialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Window/XamlRoot teardown after the write result must not become an unhandled async-void failure.
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
        catch
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
        try
        {
            using var outsideCloseSuppression = SuppressOutsideClose();
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        LeagueItemSetApplyResult? result;
        try
        {
            result = await _leagueWorkbench.ApplyItemSetAsync(plan);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
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
        try
        {
            using var outsideCloseSuppression = SuppressOutsideClose();
            await resultDialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Closing the detailed shell can invalidate XamlRoot while the result dialog is pending.
        }
    }

    private async void OnLeagueRecommendedAutoApplyToggled(object sender, RoutedEventArgs args)
    {
        var settings = _recommendedAutoApplySettings;
        if (_recommendedAutoApplyUiApplying || _closed || settings is null || settings.IsBusy ||
            _recommendedAutoApplyToggle is null)
            return;

        try { _ = await settings.SetEnabledAsync(_recommendedAutoApplyToggle.IsOn); }
        catch (OperationCanceledException) { }
        catch { }
        finally { SyncLeagueWorkbenchProductActions(); }
    }

    private void OnLeagueRecommendedAutoApplyPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) SyncLeagueWorkbenchProductActions();
        });
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

    private static string BuildLoadoutConfirmation(LeagueBuildLoadoutPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("英雄：")
            .Append(string.IsNullOrWhiteSpace(plan.ChampionName)
                ? "#" + plan.ChampionId.ToString(CultureInfo.InvariantCulture)
                : plan.ChampionName)
            .AppendLine()
            .Append("模式：").Append(plan.Mode).Append(" / ").Append(plan.Position)
            .AppendLine();
        if (plan.HasSpells)
            builder.Append("召唤师技能：").Append(string.IsNullOrWhiteSpace(plan.SpellPreview)
                ? plan.Spell1Id + " / " + plan.Spell2Id
                : plan.SpellPreview).AppendLine();
        if (plan.HasRunes)
            builder.Append("符文：").Append(string.IsNullOrWhiteSpace(plan.RunePreview)
                ? string.Join(" / ", plan.SelectedPerkIds)
                : plan.RunePreview).AppendLine();
        builder.AppendLine()
            .Append("确认后 FACM 会再次读取当前选人状态；英雄或队列已变化时不会写入。符文只创建或复用 [FACM] 自有页面，闪现会尽量保持原来的槽位。HTTP 2xx 不会直接算成功，必须通过回读确认。");
        return builder.ToString();
    }

    private static string FormatLoadoutResult(LeagueBuildLoadoutApplyResult result)
    {
        if (string.Equals(result.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return result.BlockReason switch
            {
                "champ-select-required" => "已离开选人阶段，未写入符文/技能。",
                "champion-changed" => "英雄已经变化，未写入符文/技能。",
                "queue-changed" => "队列已经变化，未写入符文/技能。",
                _ => "推荐配置被安全门禁阻止：" + result.BlockReason
            };
        }

        var runes = result.RunesApplied ? "符文已确认" : "符文：" + result.RuneStatus;
        var spells = result.SpellsApplied ? "技能已确认" : "技能：" + result.SpellStatus;
        return result.Status switch
        {
            "success" => runes + " · " + spells,
            "partial" => "部分应用 · " + runes + " · " + spells,
            _ => "未完成 · " + runes + " · " + spells
        };
    }

    private static string BuildRecommendedAutoApplyStatus(LeagueRecommendedAutoApplySettingsViewModel? viewModel)
    {
        if (viewModel is null) return "自动应用推荐配置暂不可用。";
        if (viewModel.IsBusy) return "正在保存自动应用设置…";
        if (viewModel.RecoveryReadOnly) return "设置处于恢复只读模式，本次开关没有覆盖损坏的主设置文件。";
        var status = viewModel.LastStatus;
        return status.State switch
        {
            "disabled" => "自动应用已关闭。",
            "waiting" => "自动应用已就绪，等待选人阶段可用推荐。",
            "stabilizing" => "已识别推荐，正在等待英雄/队列上下文稳定。",
            "applying" => "正在应用推荐符文、技能和装备集，并执行回读/上下文确认。",
            "success" => "本次推荐配置已自动应用并完成确认。",
            "partial" => "本次推荐配置只完成了一部分：" + status.Detail,
            "blocked" => "上下文已经变化，本次自动写入已阻止：" + status.Detail,
            "failed" => "本次自动应用失败：" + status.Detail,
            "skipped" => "本次推荐没有可执行写入。",
            "already-attempted" => "当前稳定推荐已经处理过，不会重复写入。",
            _ => "自动应用状态：" + status.State + " · " + status.Detail
        };
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
        if (_leagueLoadoutApplyButton is not null)
            _leagueLoadoutApplyButton.Click -= OnLeagueLoadoutApplyClicked;
        if (_leagueItemSetApplyButton is not null)
            _leagueItemSetApplyButton.Click -= OnLeagueItemSetApplyClicked;
        if (_recommendedAutoApplyToggle is not null)
            _recommendedAutoApplyToggle.Toggled -= OnLeagueRecommendedAutoApplyToggled;
        if (_recommendedAutoApplySettings is not null)
        {
            _recommendedAutoApplySettings.PropertyChanged -= OnLeagueRecommendedAutoApplyPropertyChanged;
            _recommendedAutoApplySettings.Dispose();
        }
        if (_leagueLoadoutService is IDisposable disposableLoadout) disposableLoadout.Dispose();
        if (_leagueProductActionsCard is not null)
            LeagueWorkbenchPanel.Children.Remove(_leagueProductActionsCard);

        _leagueProductActionsCard = null;
        _leagueAdvisorStatus = null;
        _leagueLoadoutStatus = null;
        _leagueAdvisorRefreshButton = null;
        _leagueLoadoutApplyButton = null;
        _leagueItemSetApplyButton = null;
        _leagueLoadoutService = null;
        _recommendedAutoApplySettings = null;
        _recommendedAutoApplyToggle = null;
        _recommendedAutoApplyStatus = null;
    }
}
