using System.ComponentModel;
using System.Globalization;
using System.Text;
using FACM.App.ViewModels;
using FACM.Core.Mayhem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace FACM.App;

public sealed partial class MainWindow
{
    private const int MayhemExportWidth = 840;

    private MayhemViewModel? _mayhem;
    private Border? _mayhemCard;
    private Border? _mayhemResultCard;
    private TextBox? _mayhemQueryBox;
    private Button? _mayhemQueryButton;
    private Button? _mayhemCancelButton;
    private Button? _mayhemSaveImageButton;
    private Button? _mayhemCopyImageButton;
    private ProgressRing? _mayhemProgress;
    private TextBlock? _mayhemStatus;
    private StackPanel? _mayhemResults;

    private void InitializeMayhemSurface()
    {
        if (_mayhemCard is not null || Application.Current is not App app) return;

        try { _mayhem = app.CreateMayhemViewModel(); }
        catch { return; }

        _mayhem.PropertyChanged += OnMayhemPropertyChanged;

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "海斗攻略",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "查英雄强度、强化符文和出装。强化榜优先展示胜率、选择率、样本量和效果；上游未提供统计时只展示可验证信息。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });

        _mayhemQueryBox = new TextBox
        {
            PlaceholderText = "英雄名称或别名，例如：寒冰、VN、滑板鞋",
            MaxLength = 48
        };
        AutomationProperties.SetAutomationId(_mayhemQueryBox, "FACM.League.Mayhem.Query");
        _mayhemQueryBox.KeyDown += OnMayhemQueryKeyDown;
        content.Children.Add(_mayhemQueryBox);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _mayhemQueryButton = new Button
        {
            Content = "查询",
            Style = (Style)Application.Current.Resources["FacmPrimaryButtonStyle"]
        };
        AutomationProperties.SetAutomationId(_mayhemQueryButton, "FACM.League.Mayhem.Search");
        _mayhemQueryButton.Click += OnMayhemQueryClick;
        actions.Children.Add(_mayhemQueryButton);

        _mayhemCancelButton = new Button { Content = "取消", IsEnabled = false };
        AutomationProperties.SetAutomationId(_mayhemCancelButton, "FACM.League.Mayhem.Cancel");
        _mayhemCancelButton.Click += OnMayhemCancelClick;
        actions.Children.Add(_mayhemCancelButton);

        _mayhemSaveImageButton = new Button { Content = "保存图片", IsEnabled = false };
        AutomationProperties.SetAutomationId(_mayhemSaveImageButton, "FACM.League.Mayhem.SaveImage");
        _mayhemSaveImageButton.Click += OnMayhemSaveImageClick;
        actions.Children.Add(_mayhemSaveImageButton);

        _mayhemCopyImageButton = new Button { Content = "复制图片", IsEnabled = false };
        AutomationProperties.SetAutomationId(_mayhemCopyImageButton, "FACM.League.Mayhem.CopyImage");
        _mayhemCopyImageButton.Click += OnMayhemCopyImageClick;
        actions.Children.Add(_mayhemCopyImageButton);

        _mayhemProgress = new ProgressRing
        {
            Width = 22,
            Height = 22,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(_mayhemProgress, "FACM.League.Mayhem.Progress");
        actions.Children.Add(_mayhemProgress);
        content.Children.Add(actions);

        _mayhemStatus = new TextBlock
        {
            Text = _mayhem.StatusText,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        AutomationProperties.SetAutomationId(_mayhemStatus, "FACM.League.Mayhem.Status");
        content.Children.Add(_mayhemStatus);

        _mayhemResults = new StackPanel { Spacing = 12 };
        AutomationProperties.SetAutomationId(_mayhemResults, "FACM.League.Mayhem.Results");
        _mayhemResultCard = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"],
            Padding = new Thickness(16),
            Visibility = Visibility.Collapsed,
            Child = _mayhemResults
        };
        AutomationProperties.SetAutomationId(_mayhemResultCard, "FACM.League.Mayhem.ExportCard");
        content.Children.Add(_mayhemResultCard);

        card.Child = content;
        _mayhemCard = card;
        LeagueWorkbenchPanel.Children.Add(card);
        ApplyMayhemSurface();
    }

    private async void OnMayhemQueryClick(object sender, RoutedEventArgs args) => await RunMayhemQueryAsync();

    private async void OnMayhemQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || _mayhem?.IsBusy == true) return;
        args.Handled = true;
        await RunMayhemQueryAsync();
    }

    private async Task RunMayhemQueryAsync()
    {
        var viewModel = _mayhem;
        if (viewModel is null || viewModel.IsBusy) return;
        viewModel.QueryText = _mayhemQueryBox?.Text ?? string.Empty;
        await viewModel.QueryAsync();
    }

    private void OnMayhemCancelClick(object sender, RoutedEventArgs args) => _mayhem?.Cancel();

    private async void OnMayhemSaveImageClick(object sender, RoutedEventArgs args)
    {
        if (!CanExportMayhemResult()) return;

        var picker = new FileSavePicker
        {
            SuggestedFileName = "GGman-海斗攻略-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        };
        picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        try
        {
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            stream.Size = 0;
            await EncodeMayhemResultPngAsync(stream);
            if (_mayhemStatus is not null) _mayhemStatus.Text = "图片已保存";
        }
        catch
        {
            if (_mayhemStatus is not null) _mayhemStatus.Text = "图片保存失败，请换一个位置后重试。";
        }
    }

    private async void OnMayhemCopyImageClick(object sender, RoutedEventArgs args)
    {
        if (!CanExportMayhemResult()) return;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await EncodeMayhemResultPngAsync(stream);
            stream.Seek(0);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            if (_mayhemStatus is not null) _mayhemStatus.Text = "图片已复制到剪贴板";
        }
        catch
        {
            if (_mayhemStatus is not null) _mayhemStatus.Text = "复制图片失败，请稍后重试。";
        }
    }

    private bool CanExportMayhemResult() =>
        _mayhem?.Result?.Success == true &&
        _mayhem.IsBusy == false &&
        _mayhemResultCard is not null &&
        _mayhemResultCard.Visibility == Visibility.Visible;

    private async Task EncodeMayhemResultPngAsync(IRandomAccessStream stream)
    {
        var target = _mayhemResultCard ?? throw new InvalidOperationException("Mayhem result card is unavailable.");
        if (target.ActualWidth < 1 || target.ActualHeight < 1)
            throw new InvalidOperationException("Mayhem result card has not been laid out yet.");

        var scaledHeight = Math.Max(1, (int)Math.Round(target.ActualHeight * MayhemExportWidth / target.ActualWidth));
        var rendered = new RenderTargetBitmap();
        await rendered.RenderAsync(target, MayhemExportWidth, scaledHeight);
        var buffer = await rendered.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(buffer);
        var pixels = new byte[checked((int)buffer.Length)];
        reader.ReadBytes(pixels);

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            checked((uint)rendered.PixelWidth),
            checked((uint)rendered.PixelHeight),
            96,
            96,
            pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
    }

    private void OnMayhemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        _ = DispatcherQueue.TryEnqueue(ApplyMayhemSurface);
    }

    private void ApplyMayhemSurface()
    {
        var viewModel = _mayhem;
        if (viewModel is null || _mayhemStatus is null || _mayhemResults is null) return;

        _mayhemStatus.Text = viewModel.StatusText;
        if (_mayhemQueryButton is not null) _mayhemQueryButton.IsEnabled = viewModel.CanQuery;
        if (_mayhemCancelButton is not null) _mayhemCancelButton.IsEnabled = viewModel.CanCancel;
        if (_mayhemProgress is not null)
        {
            _mayhemProgress.IsActive = viewModel.IsBusy;
            _mayhemProgress.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        _mayhemResults.Children.Clear();
        var result = viewModel.Result;
        var canExport = result?.Success == true && !viewModel.IsBusy;
        if (_mayhemSaveImageButton is not null) _mayhemSaveImageButton.IsEnabled = canExport;
        if (_mayhemCopyImageButton is not null) _mayhemCopyImageButton.IsEnabled = canExport;
        if (_mayhemResultCard is not null)
            _mayhemResultCard.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        if (result is null) return;

        if (!result.Success)
        {
            AddMayhemSection(_mayhemResults, "查询结果", result.ErrorMessage);
            return;
        }

        AddMayhemSection(_mayhemResults, "先看结论", BuildMayhemSummary(result));
        AddMayhemSection(_mayhemResults, "版本修正", BuildMayhemBalance(result));
        AddMayhemSection(_mayhemResults, "这一局怎么选", BuildMayhemDecisionRoutes(result));
        AddMayhemSection(_mayhemResults, "强化符文决策榜", BuildMayhemAugments(result));
        AddMayhemSection(_mayhemResults, "技能与出装", BuildMayhemBuild(result));
        AddMayhemSection(_mayhemResults, "版本胜率前十", BuildMayhemTopTen(result));
        AddMayhemSection(_mayhemResults, "数据来源", string.IsNullOrWhiteSpace(result.SourceNote) ? "—" : result.SourceNote);
    }

    private static void AddMayhemSection(StackPanel parent, string title, string body)
    {
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        section.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(body) ? "暂无可验证数据" : body,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        });
        parent.Children.Add(section);
    }

    private static string BuildMayhemSummary(MayhemChampionResult result)
    {
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(result.ChampionName) ? result.ChampionSlug : result.ChampionName
        };
        if (!string.IsNullOrWhiteSpace(result.Patch)) parts.Add("版本 " + result.Patch);
        if (!string.IsNullOrWhiteSpace(result.Tier)) parts.Add("梯队 " + result.Tier);
        if (result.Rank.HasValue) parts.Add("排行 #" + result.Rank.Value.ToString(CultureInfo.InvariantCulture));
        if (result.WinRate.HasValue) parts.Add("英雄胜率 " + result.WinRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
        if (result.PickRate.HasValue) parts.Add("选用率 " + result.PickRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
        return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildMayhemBalance(MayhemChampionResult result)
    {
        var baseAram = string.IsNullOrWhiteSpace(result.BaseBalanceSummary)
            ? "暂无可验证的基础 ARAM 修正"
            : result.BaseBalanceSummary;
        var mayhem = string.IsNullOrWhiteSpace(result.MayhemBalanceSummary)
            ? string.IsNullOrWhiteSpace(result.BalanceSummary) ? "暂无可验证的 Mayhem 专属修正" : result.BalanceSummary
            : result.MayhemBalanceSummary;
        var basePatch = string.IsNullOrWhiteSpace(result.BaseBalancePatch) ? string.Empty : "（" + result.BaseBalancePatch + "）";
        return "基础 ARAM" + basePatch + "：" + baseAram + Environment.NewLine +
               "Mayhem 专属：" + mayhem + Environment.NewLine +
               "两层独立展示，不做数值叠加。";
    }

    private static string BuildMayhemDecisionRoutes(MayhemChampionResult result)
    {
        if (result.AugmentRoutes.Count == 0)
            return "暂无足够单强化统计；不会伪造三强化组合胜率。";
        return string.Join(Environment.NewLine, result.AugmentRoutes.Take(3).Select(route =>
            route.Title + "：" + route.AugmentName +
            (string.IsNullOrWhiteSpace(route.Hint) ? string.Empty : " · " + route.Hint)));
    }

    private static string BuildMayhemAugments(MayhemChampionResult result)
    {
        if (result.AugmentRows.Count == 0)
            return result.Augments.Count == 0 ? "暂无强化排行，基础攻略仍可正常使用。" : string.Join(" · ", result.Augments.Take(5));

        return string.Join(Environment.NewLine, result.AugmentRows.Take(8).Select(row =>
        {
            var metrics = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Rarity)) metrics.Add(row.Rarity);
            if (row.WinRate.HasValue) metrics.Add("胜率 " + row.WinRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
            if (row.PickRate.HasValue) metrics.Add("选择 " + row.PickRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%");
            if (row.Games.HasValue) metrics.Add("样本 " + row.Games.Value.ToString("N0", CultureInfo.InvariantCulture) + " 局");
            var suffix = metrics.Count == 0 ? string.Empty : " · " + string.Join(" · ", metrics);
            var description = string.IsNullOrWhiteSpace(row.Description) ? string.Empty : Environment.NewLine + "    " + row.Description;
            return "#" + row.Rank.ToString(CultureInfo.InvariantCulture) + " " + row.Name + suffix + description;
        }));
    }

    private static string BuildMayhemBuild(MayhemChampionResult result)
    {
        var builder = new StringBuilder();
        foreach (var path in result.CoreBuilds.Take(2))
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.Append("核心方案 #").Append(path.Rank).Append("：")
                .Append(string.Join(" → ", path.Items.Take(5).Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name))));
        }
        AppendBuildLine(builder, "出门", result.StarterItems.Select(item => item.Name));
        AppendBuildLine(builder, "鞋子", result.BootItems.Select(item => item.Name));
        AppendBuildLine(builder, "召唤师", result.SummonerSpells.Select(item => item.Name));
        AppendBuildLine(builder, "技能加点", result.SkillPriority.Select(item => string.IsNullOrWhiteSpace(item.Name) ? item.Key : item.Key + " " + item.Name));
        if (builder.Length == 0 && !string.IsNullOrWhiteSpace(result.SkillOrder)) builder.Append("技能：").Append(result.SkillOrder);
        return builder.ToString();
    }

    private static void AppendBuildLine(StringBuilder builder, string label, IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(5).ToArray();
        if (items.Length == 0) return;
        if (builder.Length > 0) builder.AppendLine();
        builder.Append(label).Append("：").Append(string.Join(" → ", items));
    }

    private static string BuildMayhemTopTen(MayhemChampionResult result)
    {
        if (result.TopTen.Count == 0) return "暂无数据";
        return string.Join(Environment.NewLine, result.TopTen.Take(10).Select(item =>
            "#" + item.Rank.ToString(CultureInfo.InvariantCulture) + " " + item.Name +
            (item.WinRate.HasValue ? " · " + item.WinRate.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%" : string.Empty) +
            (string.IsNullOrWhiteSpace(item.Tier) ? string.Empty : " · " + item.Tier)));
    }

    private void DisposeMayhemSurface()
    {
        if (_mayhem is not null)
        {
            _mayhem.PropertyChanged -= OnMayhemPropertyChanged;
            _mayhem.Dispose();
            _mayhem = null;
        }
        if (_mayhemQueryBox is not null) _mayhemQueryBox.KeyDown -= OnMayhemQueryKeyDown;
        if (_mayhemQueryButton is not null) _mayhemQueryButton.Click -= OnMayhemQueryClick;
        if (_mayhemCancelButton is not null) _mayhemCancelButton.Click -= OnMayhemCancelClick;
        if (_mayhemSaveImageButton is not null) _mayhemSaveImageButton.Click -= OnMayhemSaveImageClick;
        if (_mayhemCopyImageButton is not null) _mayhemCopyImageButton.Click -= OnMayhemCopyImageClick;
        _mayhemCard = null;
        _mayhemResultCard = null;
        _mayhemQueryBox = null;
        _mayhemQueryButton = null;
        _mayhemCancelButton = null;
        _mayhemSaveImageButton = null;
        _mayhemCopyImageButton = null;
        _mayhemProgress = null;
        _mayhemStatus = null;
        _mayhemResults = null;
    }
}
