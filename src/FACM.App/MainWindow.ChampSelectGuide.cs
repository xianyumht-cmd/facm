using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using FACM.Core.League;
using FACM.Core.Mayhem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FACM.App;

public sealed partial class MainWindow
{
    private bool _champSelectGuideVisible;
    private bool _champSelectGuideRequestStarted;
    private long _champSelectGuideGeneration;
    private int _champSelectGuideChampionId;
    private int _champSelectGuideIdentityRetryCount;
    private string _champSelectGuideRarity = "棱彩";
    private MayhemChampionResult? _champSelectGuideResult;
    private CancellationTokenSource? _champSelectGuideCts;
    private readonly Dictionary<string, int> _champSelectGuidePages = new(StringComparer.Ordinal);
    private TextBlock? _champSelectGuideInspector;

    private sealed record ChampSelectGuideImageTarget(
        Image Image,
        string Kind,
        int Id,
        string Reference);

    private int ResolveCurrentChampSelectChampionId(
        LeagueBenchRuntimeSnapshot? runtime,
        LeagueWorkbenchLiveSnapshot live)
    {
        if (runtime?.LocalChampionId > 0) return runtime.LocalChampionId;
        var local = live.Players.FirstOrDefault(player => player.IsLocalPlayer);
        if (local?.ChampionId > 0) return local.ChampionId;
        return live.LocalActionChampionId > 0 ? live.LocalActionChampionId : 0;
    }

    private void EnsureChampSelectAutomaticGuide(int championId)
    {
        if (championId <= 0 || _mayhem is null || _leagueBenchQuickPick is null) return;
        if (_champSelectGuideRequestStarted && _champSelectGuideChampionId == championId) return;

        var championChanged = _champSelectGuideChampionId != championId;
        CancelChampSelectAutomaticGuideRequest();
        _champSelectGuideVisible = true;
        _champSelectGuideRequestStarted = true;
        _champSelectGuideChampionId = championId;
        if (championChanged) _champSelectGuideIdentityRetryCount = 0;
        _champSelectGuideResult = null;
        _champSelectGuideRarity = "棱彩";
        _champSelectGuidePages.Clear();
        var generation = ++_champSelectGuideGeneration;
        ChampSelectGuidePanel.Visibility = Visibility.Visible;
        RenderChampSelectGuideLoading(championId);
        EnsureCurrentSurfacePresentation("champ-select-guide-opened");
        _champSelectGuideCts = new CancellationTokenSource();
        _ = LoadChampSelectAutomaticGuideAsync(generation, championId, _champSelectGuideCts);
    }

    private async Task LoadChampSelectAutomaticGuideAsync(
        long generation,
        int championId,
        CancellationTokenSource requestCts)
    {
        var service = _leagueBenchQuickPick;
        var viewModel = _mayhem;
        if (service is null || viewModel is null) return;

        try
        {
            var knownName = _champSelectCandidateNames.TryGetValue(championId, out var candidateName)
                ? candidateName
                : string.Empty;
            var query = string.Empty;
            LeagueChampionIdentity? identity = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var identities = await service.LoadChampionIdentitiesAsync([championId], requestCts.Token);
                identities.TryGetValue(championId, out identity);
                query = FirstUsableChampionQuery(identity?.Alias, knownName, identity?.Name);
                if (!string.IsNullOrWhiteSpace(query)) break;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromMilliseconds(350 + (attempt * 250)), requestCts.Token);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                QueueChampSelectGuideIdentityPending(generation, championId);
                return;
            }

            var result = await viewModel.QueryForInputAsync(query, requestCts.Token);
            QueueChampSelectGuideResult(generation, championId, result, requestCts.Token);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
        }
        catch
        {
            QueueChampSelectGuideMessage(generation, championId, "自动攻略读取失败，横条仍可正常使用。\n可在完整工作台中手动查询。", false);
        }
        finally
        {
            if (ReferenceEquals(_champSelectGuideCts, requestCts))
            {
                requestCts.Dispose();
                _champSelectGuideCts = null;
            }
        }
    }

    private static string FirstUsableChampionQuery(params string?[] values)
    {
        foreach (var value in values)
        {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Length == 0 || candidate.Equals("英雄", StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals("Unknown champion", StringComparison.OrdinalIgnoreCase)) continue;
            return candidate;
        }
        return string.Empty;
    }

    private void QueueChampSelectGuideIdentityPending(long generation, int championId)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !MayhemAutomaticGuideProjection.IsCurrentGeneration(generation, _champSelectGuideGeneration) ||
                _champSelectGuideChampionId != championId)
                return;

            _champSelectGuideIdentityRetryCount++;
            var canRetry = _champSelectGuideIdentityRetryCount <= 3;
            RenderChampSelectGuideMessage(
                canRetry
                    ? "正在等待客户端英雄资料，自动攻略会继续重试…"
                    : "客户端暂未提供当前英雄名称，自动攻略暂时无法查询。\n可在完整工作台中手动查询。",
                canRetry);
            if (canRetry) _ = RetryChampSelectGuideAfterDelayAsync(generation, championId);
        });
    }

    private async Task RetryChampSelectGuideAfterDelayAsync(long generation, int championId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), _champSelectGuideCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !MayhemAutomaticGuideProjection.IsCurrentGeneration(generation, _champSelectGuideGeneration) ||
                _champSelectGuideChampionId != championId)
                return;
            _champSelectGuideRequestStarted = false;
            EnsureChampSelectAutomaticGuide(championId);
        });
    }

    private void QueueChampSelectGuideResult(
        long generation,
        int championId,
        MayhemChampionResult? result,
        CancellationToken requestToken)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !MayhemAutomaticGuideProjection.IsCurrentGeneration(generation, _champSelectGuideGeneration) ||
                _champSelectGuideChampionId != championId)
                return;
            _champSelectGuideResult = result;
            if (result is null)
            {
                RenderChampSelectGuideMessage("自动攻略暂时没有结果，横条仍可正常使用。\n可在完整工作台中手动查询。", false);
                return;
            }
            RenderChampSelectGuideResult(result, generation, requestToken);
        });
    }

    private void QueueChampSelectGuideMessage(
        long generation,
        int championId,
        string message,
        bool busy)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !MayhemAutomaticGuideProjection.IsCurrentGeneration(generation, _champSelectGuideGeneration) ||
                _champSelectGuideChampionId != championId)
                return;
            RenderChampSelectGuideMessage(message, busy);
        });
    }

    private void RenderChampSelectGuideLoading(int championId)
    {
        ChampSelectGuideContent.Children.Clear();
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = "海克斯大乱斗 · 自动攻略",
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = "正在识别当前英雄…（英雄 ID " + championId.ToString(CultureInfo.InvariantCulture) + "）",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
    }

    private void RenderChampSelectGuideMessage(string message, bool busy)
    {
        ChampSelectGuideContent.Children.Clear();
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = "海克斯大乱斗 · 自动攻略",
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        if (busy)
            ChampSelectGuideContent.Children.Add(new ProgressRing { Width = 22, Height = 22, IsActive = true });
    }

    private void RenderChampSelectGuideResult(
        MayhemChampionResult result,
        long generation,
        CancellationToken imageCancellationToken = default)
    {
        ChampSelectGuideContent.Children.Clear();
        _champSelectGuideInspector = null;
        var targets = new List<ChampSelectGuideImageTarget>();

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var championImage = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
        header.Children.Add(CreateIconFrame(championImage, 50));
        targets.Add(new ChampSelectGuideImageTarget(championImage, "champions", result.ChampionId, result.ChampionIconUrl));
        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(result.ChampionName) ? result.Query : result.ChampionName,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"],
            FontSize = 18
        });
        title.Children.Add(new TextBlock
        {
            Text = result.Success ? "图标攻略已加载 · 不会自动应用任何配置" : "自动攻略未得到完整结果",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        ChampSelectGuideContent.Children.Add(header);

        if (!result.Success)
        {
            RenderChampSelectGuideMessage(
                string.IsNullOrWhiteSpace(result.ErrorMessage) ? "自动攻略暂时不可用，横条仍可正常使用。" : result.ErrorMessage,
                false);
            return;
        }

        AddChampSelectGuideBuildSection("技能", result.SkillPriority.Take(4).Select(skill =>
            (skill.Name, skill.Key, skill.IconUrl, "skills", 0)), targets);
        AddChampSelectGuideBuildSection("召唤师技能", result.SummonerSpells.Take(2).Select(spell =>
            (spell.Name, spell.Id, spell.IconUrl, "summoner-spells", ParseNumericId(spell.Id))), targets);

        var items = result.StarterItems
            .Concat(result.BootItems)
            .Concat(result.CoreBuilds.FirstOrDefault()?.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.IconUrl))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Id) ? item.Name : item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToArray();
        AddChampSelectGuideBuildSection("出装", items.Select(item =>
            (item.Name, item.Id, item.IconUrl, "items", ParseNumericId(item.Id))), targets);

        AddChampSelectGuideAugmentSection(result, targets);
        _champSelectGuideInspector = new TextBlock
        {
            Text = "将鼠标移到图标上查看说明",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        AutomationProperties.SetAutomationId(_champSelectGuideInspector, "FACM.Surface.AutomaticGuideInspector");
        ChampSelectGuideContent.Children.Add(_champSelectGuideInspector);

        if (targets.Count > 0) _ = LoadChampSelectGuideImagesAsync(targets, generation, imageCancellationToken);
    }

    private void AddChampSelectGuideBuildSection(
        string title,
        IEnumerable<(string Name, string Id, string Reference, string Kind, int NumericId)> items,
        ICollection<ChampSelectGuideImageTarget> targets)
    {
        var values = items.Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Reference)).ToArray();
        if (values.Length == 0) return;
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var item in values)
        {
            var image = new Image { Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
            var label = string.IsNullOrWhiteSpace(item.Name) ? "未命名" : item.Name;
            var button = CreateGuideIconButton(image, label, label + "\n" + title);
            row.Children.Add(button);
            targets.Add(new ChampSelectGuideImageTarget(image, item.Kind, item.NumericId, item.Reference));
        }
        ChampSelectGuideContent.Children.Add(row);
    }

    private void AddChampSelectGuideAugmentSection(
        MayhemChampionResult result,
        ICollection<ChampSelectGuideImageTarget> targets)
    {
        var rows = MayhemAutomaticGuideProjection.NormalizeAugments(result.AugmentRows);
        ChampSelectGuideContent.Children.Add(new TextBlock
        {
            Text = "强化符文 · 完整排行",
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        var available = MayhemAutomaticGuideProjection.SupportedRarities
            .Where(rarity => MayhemAutomaticGuideProjection.PageCount(rows, rarity) > 0)
            .ToArray();
        if (available.Length == 0)
        {
            ChampSelectGuideContent.Children.Add(new TextBlock
            {
                Text = "当前数据源未提供可分级的海克斯图标。",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
            });
            return;
        }

        if (!available.Contains(_champSelectGuideRarity, StringComparer.OrdinalIgnoreCase))
            _champSelectGuideRarity = available[0];
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        foreach (var rarity in available)
        {
            var tab = new Button
            {
                Content = rarity,
                IsEnabled = true,
                Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
            };
            AutomationProperties.SetAutomationId(tab, "FACM.Surface.AutomaticGuide.Rarity." + rarity);
            tab.Click += (_, _) =>
            {
                _champSelectGuideRarity = rarity;
                RenderChampSelectGuideResult(result, _champSelectGuideGeneration);
            };
            tabs.Children.Add(tab);
        }
        ChampSelectGuideContent.Children.Add(tabs);

        var page = _champSelectGuidePages.TryGetValue(_champSelectGuideRarity, out var selectedPage)
            ? selectedPage
            : 0;
        var pageRows = MayhemAutomaticGuideProjection.Page(rows, _champSelectGuideRarity, page);
        var pageCount = MayhemAutomaticGuideProjection.PageCount(rows, _champSelectGuideRarity);
        if (page >= pageCount) page = Math.Max(0, pageCount - 1);
        _champSelectGuidePages[_champSelectGuideRarity] = page;

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < pageRows.Count; index++)
        {
            var augment = pageRows[index];
            var image = new Image { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
            var details = BuildAugmentDetails(augment);
            var button = CreateGuideIconButton(image, "#" + augment.Rank.ToString(CultureInfo.InvariantCulture), details);
            button.PointerEntered += (_, _) => SetChampSelectGuideInspector(details);
            button.GotFocus += (_, _) => SetChampSelectGuideInspector(details);
            Grid.SetColumn(button, index % 3);
            Grid.SetRow(button, index / 3);
            grid.Children.Add(button);
            targets.Add(new ChampSelectGuideImageTarget(image, "augments", ParseNumericId(augment.Id), augment.IconUrl));
        }
        ChampSelectGuideContent.Children.Add(grid);

        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var previous = new Button { Content = "上一页", IsEnabled = page > 0 };
        previous.Click += (_, _) =>
        {
            _champSelectGuidePages[_champSelectGuideRarity] = Math.Max(0, page - 1);
            RenderChampSelectGuideResult(result, _champSelectGuideGeneration);
        };
        var next = new Button { Content = "下一页", IsEnabled = page + 1 < pageCount };
        next.Click += (_, _) =>
        {
            _champSelectGuidePages[_champSelectGuideRarity] = Math.Min(pageCount - 1, page + 1);
            RenderChampSelectGuideResult(result, _champSelectGuideGeneration);
        };
        pager.Children.Add(previous);
        pager.Children.Add(new TextBlock
        {
            Text = (page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        pager.Children.Add(next);
        ChampSelectGuideContent.Children.Add(pager);
    }

    private Button CreateGuideIconButton(Image image, string label, string details)
    {
        var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(image);
        content.Children.Add(new TextBlock
        {
            Text = label,
            MaxWidth = 84,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        var button = new Button
        {
            Content = content,
            MinWidth = 52,
            MinHeight = 66,
            Padding = new Thickness(3),
            Style = (Style)Application.Current.Resources["FacmToolButtonStyle"]
        };
        ToolTipService.SetToolTip(button, details);
        AutomationProperties.SetHelpText(button, details);
        button.PointerEntered += (_, _) => SetChampSelectGuideInspector(details);
        button.GotFocus += (_, _) => SetChampSelectGuideInspector(details);
        return button;
    }

    private Border CreateIconFrame(Image image, double size) => new()
    {
        Width = size,
        Height = size,
        Padding = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Background = (Brush)Application.Current.Resources["FacmSurfaceBrush"],
        Child = image
    };

    private void SetChampSelectGuideInspector(string details)
    {
        if (_champSelectGuideInspector is not null) _champSelectGuideInspector.Text = details;
    }

    private static string BuildAugmentDetails(MayhemAugmentRow row)
    {
        var stats = new List<string>();
        if (row.WinRate.HasValue) stats.Add("胜率 " + row.WinRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
        if (row.PickRate.HasValue) stats.Add("选择率 " + row.PickRate.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
        if (row.Games.HasValue) stats.Add("样本 " + row.Games.Value.ToString(CultureInfo.InvariantCulture));
        var title = "#" + row.Rank.ToString(CultureInfo.InvariantCulture) + " · " + row.Name;
        var suffix = stats.Count == 0 ? row.Rarity : row.Rarity + " · " + string.Join(" · ", stats);
        return string.IsNullOrWhiteSpace(row.Description) ? title + "\n" + suffix : title + "\n" + suffix + "\n" + row.Description;
    }

    private async Task LoadChampSelectGuideImagesAsync(
        IReadOnlyList<ChampSelectGuideImageTarget> targets,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await Task.WhenAll(targets.Select(async target =>
            {
                try { return (Target: target, Bytes: await TryLoadChampSelectGuideBytesAsync(target, cancellationToken)); }
                catch (OperationCanceledException) { throw; }
                catch { return (Target: target, Bytes: (byte[]?)null); }
            }));
            if (_closed || !MayhemAutomaticGuideProjection.IsCurrentGeneration(generation, _champSelectGuideGeneration)) return;
            foreach (var item in loaded)
            {
                if (item.Bytes is null || item.Bytes.Length == 0) continue;
                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(item.Bytes.AsBuffer());
                    stream.Seek(0);
                    var image = new BitmapImage { DecodePixelWidth = 96, DecodePixelHeight = 96 };
                    await image.SetSourceAsync(stream);
                    item.Target.Image.Source = image;
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<byte[]?> TryLoadChampSelectGuideBytesAsync(
        ChampSelectGuideImageTarget target,
        CancellationToken cancellationToken)
    {
        var reference = target.Reference?.Trim() ?? string.Empty;
        if (reference.StartsWith("lcu:/", StringComparison.OrdinalIgnoreCase))
        {
            var gateway = _leagueReadGateway;
            if (gateway is null) return null;
            return await gateway.TryGetBytesAsync(reference[4..], cancellationToken);
        }
        if (target.Id > 0 && _leagueGuideAssets is not null)
            return await _leagueGuideAssets.TryGetBytesAsync(target.Kind, target.Id, reference, cancellationToken);
        if (target.Kind == "champions" && target.Id > 0 && _leagueBenchQuickPick is not null)
            return await _leagueBenchQuickPick.LoadChampionIconAsync(target.Id, cancellationToken);
        return null;
    }

    private void ResetChampSelectAutomaticGuide()
    {
        CancelChampSelectAutomaticGuideRequest();
        _champSelectGuideGeneration++;
        _champSelectGuideVisible = false;
        _champSelectGuideRequestStarted = false;
        _champSelectGuideChampionId = 0;
        _champSelectGuideIdentityRetryCount = 0;
        _champSelectGuideResult = null;
        _champSelectGuideRarity = "棱彩";
        _champSelectGuidePages.Clear();
        _champSelectGuideInspector = null;
        if (ChampSelectGuidePanel is not null)
        {
            ChampSelectGuidePanel.Visibility = Visibility.Collapsed;
            ChampSelectGuideContent.Children.Clear();
        }
    }

    private void CancelChampSelectAutomaticGuideRequest()
    {
        _champSelectGuideCts?.Cancel();
        _champSelectGuideCts?.Dispose();
        _champSelectGuideCts = null;
    }

    private static int ParseNumericId(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            return parsed;
        return 0;
    }
}
