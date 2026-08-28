using System.ComponentModel;
using System.Globalization;
using System.Text;
using FACM.App.ViewModels;
using FACM.Core.League;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private bool _leagueWorkbenchRuntimeConfigured;

    private void InitializeLeagueWorkbenchRuntimeSurface()
    {
        if (_leagueWorkbenchRuntimeConfigured) return;
        _leagueWorkbenchRuntimeConfigured = true;

        // Product helpers are optional and fail-soft. They reuse the Workbench's existing shared
        // data source/gateway; a provider problem must never prevent Dashboard/Player/Live from working.
        if (Application.Current is App app)
        {
            try { app.ConfigureLeagueWorkbenchProductization(_leagueWorkbench); }
            catch { }
        }
        InitializeLeagueWorkbenchProductActions();
        InitializeLeagueAutomationSurface();
        InitializeLeaguePresenceSurface();

        RootNavigation.SelectionChanged += OnLeagueWorkbenchRuntimeNavigationChanged;
        _leagueWorkbench.PropertyChanged += OnLeagueWorkbenchRuntimePropertyChanged;
        Closed += OnLeagueWorkbenchRuntimeClosed;

        if (IsLeagueWorkbenchSelected())
            _ = RefreshLeagueWorkbenchRuntimeAsync();
    }

    private void OnLeagueWorkbenchRuntimeNavigationChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var selected = args.SelectedItemContainer as NavigationViewItem;
        if (!string.Equals(selected?.Tag?.ToString(), "league", StringComparison.Ordinal)) return;
        ApplyLeagueWorkbenchRuntimeSurface();
        _ = RefreshLeagueWorkbenchRuntimeAsync();
        _ = RefreshLeaguePresenceAsync();
    }

    private void OnLeagueWorkbenchRuntimePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed || !IsLeagueWorkbenchSelected()) return;
        var refreshForPhase = args.PropertyName is nameof(LeagueWorkbenchViewModel.LeagueState);
        var render = refreshForPhase || args.PropertyName is
            nameof(LeagueWorkbenchViewModel.Dashboard) or
            nameof(LeagueWorkbenchViewModel.Player) or
            nameof(LeagueWorkbenchViewModel.Live) or
            nameof(LeagueWorkbenchViewModel.Advisor) or
            nameof(LeagueWorkbenchViewModel.ItemSetStatus) or
            nameof(LeagueWorkbenchViewModel.PreparedItemSet) or
            nameof(LeagueWorkbenchViewModel.IsRefreshing) or
            nameof(LeagueWorkbenchViewModel.IsAdvisorRefreshing) or
            nameof(LeagueWorkbenchViewModel.IsItemSetBusy) or
            nameof(LeagueWorkbenchViewModel.CanPrepareItemSet) or
            nameof(LeagueWorkbenchViewModel.HasProductServices) or
            nameof(LeagueWorkbenchViewModel.HasMatchmakingAutomation) or
            nameof(LeagueWorkbenchViewModel.AutoMatchmakingEnabled) or
            nameof(LeagueWorkbenchViewModel.AutoAcceptEnabled) or
            nameof(LeagueWorkbenchViewModel.IsAutomationSettingsBusy);
        if (!render) return;

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !IsLeagueWorkbenchSelected()) return;
            ApplyLeagueWorkbenchRuntimeSurface();
            if (refreshForPhase) _ = RefreshLeagueWorkbenchRuntimeAsync();
        });
    }

    private async Task RefreshLeagueWorkbenchRuntimeAsync()
    {
        if (_closed || !IsLeagueWorkbenchSelected() || !_leagueWorkbench.HasRealDataSource) return;
        try
        {
            await _leagueWorkbench.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed && IsLeagueWorkbenchSelected()) ApplyLeagueWorkbenchRuntimeSurface();
            });
        }
    }

    private void ApplyLeagueWorkbenchRuntimeSurface()
    {
        if (_closed || !IsLeagueWorkbenchSelected()) return;

        LeagueMatchDescription.Text = BuildDashboardSummary(_leagueWorkbench.Dashboard, _leagueWorkbench.Live);
        LeagueStrategyDescription.Text = BuildPlayerSummary(_leagueWorkbench.Player);
        LeagueAutomationDescription.Text = BuildLiveSummary(_leagueWorkbench.Live, _leagueWorkbench.IsRefreshing);
        SyncLeagueWorkbenchProductActions();
        ApplyLeagueAutomationSettingsSurface();
    }

    private bool IsLeagueWorkbenchSelected() =>
        RootNavigation.SelectedItem is NavigationViewItem item &&
        string.Equals(item.Tag?.ToString(), "league", StringComparison.Ordinal);

    private static string BuildDashboardSummary(
        LeagueWorkbenchDashboardSnapshot dashboard,
        LeagueWorkbenchLiveSnapshot live)
    {
        if (dashboard.State == LeagueWorkbenchDataState.Unavailable)
            return "League 客户端未连接，或当前账号信息暂不可用。";

        var lines = new List<string>();
        if (dashboard.Account is { } account)
        {
            lines.Add($"账号：{Fallback(account.AccountName, "未知")} · 等级 {account.SummonerLevel}");
        }

        if (dashboard.Queue is { } queue)
        {
            lines.Add($"队列：{FormatQueue(queue)}");
        }

        if (dashboard.LobbyMembers.Count > 0)
            lines.Add($"房间：{dashboard.LobbyMembers.Count} 人");

        if (dashboard.ReadyCheck is { } ready)
        {
            var timer = ready.TimerMillisecondsLeft > 0
                ? $" · {ready.TimerMillisecondsLeft / 1000d:0.#} 秒"
                : string.Empty;
            lines.Add($"准备确认：{Fallback(ready.State, "未知")} / {Fallback(ready.PlayerResponse, "未响应")}{timer}");
        }

        if (!string.IsNullOrWhiteSpace(live.Phase))
            lines.Add($"当前阶段：{live.Phase}");

        return lines.Count == 0 ? "已连接 League，当前没有可显示的房间信息。" : string.Join(Environment.NewLine, lines);
    }

    private static string BuildPlayerSummary(LeagueWorkbenchPlayerSnapshot player)
    {
        if (player.State == LeagueWorkbenchDataState.Unavailable)
            return "当前玩家资料暂不可用。";

        var builder = new StringBuilder();
        if (player.Ranked is { } ranked)
        {
            builder.Append("单双排：")
                .Append(Fallback(ranked.Tier, "UNRANKED"));
            if (!string.IsNullOrWhiteSpace(ranked.Division)) builder.Append(' ').Append(ranked.Division);
            builder.Append(" · ").Append(ranked.LeaguePoints).Append(" LP")
                .Append(" · ").Append(ranked.Wins).Append('胜').Append(ranked.Losses).Append('负')
                .Append(" · ").Append(ranked.WinRate.ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
        }
        else
        {
            builder.Append("单双排：暂无可用排位数据");
        }

        foreach (var match in player.RecentMatches.Take(3))
        {
            builder.AppendLine();
            builder.Append(match.Win ? "胜" : "负")
                .Append(" · ")
                .Append(string.IsNullOrWhiteSpace(match.ChampionName)
                    ? "英雄 " + match.ChampionId.ToString(CultureInfo.InvariantCulture)
                    : match.ChampionName)
                .Append(" · ")
                .Append(match.Kills).Append('/').Append(match.Deaths).Append('/').Append(match.Assists)
                .Append(" · CS ").Append(match.CreepScore);
        }

        if (player.RecentMatches.Count == 0) builder.AppendLine().Append("最近战绩：暂无可用记录");
        return builder.ToString();
    }

    private static string BuildLiveSummary(LeagueWorkbenchLiveSnapshot live, bool refreshing)
    {
        if (refreshing) return "正在读取 League 当前状态…";
        if (live.State == LeagueWorkbenchDataState.Unavailable)
            return "当前没有可读取的选人或对局会话。";

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(live.Phase)) lines.Add("阶段：" + live.Phase);
        if (live.GameId > 0) lines.Add("对局：" + live.GameId.ToString(CultureInfo.InvariantCulture));
        if (live.Queue is { } queue) lines.Add("队列：" + FormatQueue(queue));
        if (!string.IsNullOrWhiteSpace(live.MapName)) lines.Add("地图：" + live.MapName);
        if (live.Players.Count > 0) lines.Add("玩家：" + live.Players.Count.ToString(CultureInfo.InvariantCulture) + " 人");

        if (live.BenchEnabled)
        {
            var bench = live.BenchChampionIds.Count == 0
                ? "当前无可交换英雄"
                : string.Join(", ", live.BenchChampionIds);
            lines.Add("随机模式候选：" + bench);
        }

        if (live.AllyBans.Count > 0 || live.EnemyBans.Count > 0)
            lines.Add($"禁用：我方 {FormatIds(live.AllyBans)} / 对方 {FormatIds(live.EnemyBans)}");

        if (!string.IsNullOrWhiteSpace(live.LocalActionType))
            lines.Add($"我的操作：{live.LocalActionType} · 英雄 {live.LocalActionChampionId}");

        if (live.TimerMillisecondsLeft > 0)
            lines.Add($"阶段剩余：{live.TimerMillisecondsLeft / 1000d:0.#} 秒");

        return lines.Count == 0 ? "League 已连接，当前阶段没有额外实时数据。" : string.Join(Environment.NewLine, lines);
    }

    private static string FormatQueue(LeagueWorkbenchQueue queue)
    {
        var name = Fallback(queue.QueueName, queue.GameMode);
        if (string.IsNullOrWhiteSpace(name)) name = "队列 " + queue.QueueId.ToString(CultureInfo.InvariantCulture);
        return queue.QueueId > 0 && !name.Contains(queue.QueueId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            ? $"{name} ({queue.QueueId})"
            : name;
    }

    private static string FormatIds(IReadOnlyList<int> ids) =>
        ids.Count == 0 ? "无" : string.Join(", ", ids);

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void OnLeagueWorkbenchRuntimeClosed(object sender, WindowEventArgs args)
    {
        if (!_leagueWorkbenchRuntimeConfigured) return;
        _leagueWorkbenchRuntimeConfigured = false;
        RootNavigation.SelectionChanged -= OnLeagueWorkbenchRuntimeNavigationChanged;
        _leagueWorkbench.PropertyChanged -= OnLeagueWorkbenchRuntimePropertyChanged;
        Closed -= OnLeagueWorkbenchRuntimeClosed;
        DisposeLeagueWorkbenchProductActions();
    }
}
