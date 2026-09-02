using System.ComponentModel;
using System.Globalization;
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
            Text = "GGman（鸡鸡侠）· 海斗攻略",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "输入英雄名称或别名，查看当前模式的强度、技能、召唤师技能与出装建议。",
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

        _mayhemSaveImageButton = new Button { Content = "保存攻略图", IsEnabled = false };
        AutomationProperties.SetAutomationId(_mayhemSaveImageButton, "FACM.League.Mayhem.SaveImage");
        _mayhemSaveImageButton.Click += OnMayhemSaveImageClick;
        actions.Children.Add(_mayhemSaveImageButton);

        _mayhemCopyImageButton = new Button { Content = "复制攻略图", IsEnabled = false };
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
            Padding = new Thickness(20),
            MinWidth = 520,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
            if (_mayhemStatus is not null) _mayhemStatus.Text = "攻略图已保存";
        }
        catch
        {
            if (_mayhemStatus is not null) _mayhemStatus.Text = "攻略图保存失败，请换一个位置后重试。";
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
            if (_mayhemStatus is not null) _mayhemStatus.Text = "攻略图已复制到剪贴板";
        }
        catch
        {
            if (_mayhemStatus is not null) _mayhemStatus.Text = "复制攻略图失败，请稍后重试。";
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

        var guide = MayhemGuidePresentation.Create(result);
        AddMayhemGuideHeader(_mayhemResults, guide);
        foreach (var section in guide.Sections)
            AddMayhemSection(_mayhemResults, section.Title, section.Body);
    }

    private static void AddMayhemGuideHeader(StackPanel parent, MayhemGuidePresentation guide)
    {
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = guide.QueryTitle,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        if (!string.IsNullOrWhiteSpace(guide.OfficialName))
            header.Children.Add(new TextBlock
            {
                Text = guide.OfficialName,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
            });
        header.Children.Add(new TextBlock
        {
            Text = guide.ModeTitle,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        });
        parent.Children.Add(header);
    }

    private static void AddMayhemSection(StackPanel parent, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        });
        section.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        });
        parent.Children.Add(section);
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
