using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using LinePutScript;
using VPet_Simulator.Core;
using FormsControl = System.Windows.Forms.Control;
using VPetMain = VPet_Simulator.Core.Main;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace FACM.PetHost;

internal sealed class PetHostWindow : Window
{
    private readonly PetHostIpc _ipc;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _root;
    private readonly Border _statusCard;
    private readonly TextBlock _statusText;
    private readonly WpfProgressBar _statusProgress;
    private readonly TextBlock _statusProgressText;
    private readonly TextBlock _statusNotice;
    private readonly PetWindowController _controller;
    private VPetMain? _main;
    private GameCore? _core;
    private System.Drawing.Point _leftDownPoint;
    private long _leftDownTicks;
    private bool _leftTracking;
    private int _bootstrapProgressTotal;

    public PetHostWindow(PetHostIpc ipc)
    {
        _ipc = ipc;
        _controller = new PetWindowController(this);

        Title = PetHostUiText.Translate("FACM PetHost");
        Width = 330;
        Height = 330;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = null;
        AllowsTransparency = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        _root = new Grid
        {
            Width = Width,
            Height = Height,
            Background = WpfBrushes.Transparent
        };

        _statusText = new TextBlock
        {
            Text = PetHostUiText.Translate("正在启动高精度桌宠…"),
            Foreground = WpfBrushes.White,
            FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(4, 0, 4, 0)
        };

        _statusProgress = new WpfProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 8,
            Margin = new Thickness(8, 10, 8, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Background = new SolidColorBrush(WpfColor.FromRgb(38, 48, 66)),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(44, 218, 255)),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed
        };

        _statusProgressText = new TextBlock
        {
            Foreground = new SolidColorBrush(WpfColor.FromRgb(184, 199, 222)),
            FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(4, 5, 4, 0),
            Visibility = Visibility.Collapsed
        };

        _statusNotice = new TextBlock
        {
            Text = PetHostUiText.Translate("动画来源：VPet / VUP-Simulator（非商用授权）"),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(166, 178, 198)),
            FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
            FontSize = 10.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(4, 6, 4, 0)
        };

        var statusStack = new StackPanel
        {
            VerticalAlignment = WpfVerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch
        };
        statusStack.Children.Add(_statusText);
        statusStack.Children.Add(_statusProgress);
        statusStack.Children.Add(_statusProgressText);
        statusStack.Children.Add(_statusNotice);

        _statusCard = new Border
        {
            Width = 278,
            MinHeight = 104,
            Padding = new Thickness(14, 12, 14, 12),
            Background = new SolidColorBrush(WpfColor.FromArgb(222, 12, 17, 28)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(120, 121, 155, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            VerticalAlignment = WpfVerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Child = statusStack
        };
        _root.Children.Add(_statusCard);
        Content = _root;

        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewMouseLeftButtonDown += OnPreviewLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewLeftButtonUp;
        PreviewMouseRightButtonDown += OnPreviewRightButtonDown;

        _ipc.Start(HandleCommand);
    }

    internal void NotifyOpenFacm()
    {
        _ = _ipc.SendEventAsync("click");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _controller.ResetToPrimaryScreen();
        var progress = new Progress<string>(HandleBootstrapProgress);
        var started = Stopwatch.StartNew();

        try
        {
            var bootstrapper = new VPetAssetBootstrapper();
            await bootstrapper.EnsureAsync(progress, _lifetime.Token);

            SetStatus("正在解析动作配置…");
            GraphCore.CachePath = PetHostPaths.CacheDirectory;
            Directory.CreateDirectory(GraphCore.CachePath);

            // Match VPet-Simulator.Windows startup behavior. Core's default 2000 MB limit can stall
            // first-run graph generation when hundreds of high-resolution frames are being merged.
            var baselineMemoryMb = (int)Math.Ceiling(Function.MemoryUsage());
            var availableMemoryMb = Math.Max(0, (int)Math.Floor(Function.MemoryAvailable()));
            var additionalBudgetMb = Math.Max(512, availableMemoryMb / 2);
            PNGAnimation.MaxLoadMemory = baselineMemoryMb + additionalBudgetMb;
            await _ipc.SendEventAsync("status", $"graph-memory-limit={PNGAnimation.MaxLoadMemory}MB");

            var lps = new LpsDocument(await File.ReadAllTextAsync(PetHostPaths.PetConfigPath, _lifetime.Token));
            var loader = new PetLoader(lps, new DirectoryInfo(PetHostPaths.PetDirectory));
            var graph = loader.Graph(1000, Dispatcher);
            var graphCount = graph.GraphsList.Values.Sum(byAnimation => byAnimation.Values.Sum(items => items.Count));

            _core = new GameCore
            {
                Controller = _controller,
                Graph = graph,
                Save = new GameSave("FACM")
            };

            _main = new VPetMain(_core)
            {
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = WpfVerticalAlignment.Stretch,
                Opacity = 0
            };

            // Keep the loading card above the real VPet control while the first-run PNG caches are generated.
            _root.Children.Insert(0, _main);
            SetCacheProgress(0, graphCount);

            var lastReported = 0;
            await Task.Run(() =>
            {
                _main.LoadALL(readyCount =>
                {
                    if (readyCount <= Volatile.Read(ref lastReported)) return;
                    Volatile.Write(ref lastReported, readyCount);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetCacheProgress(Math.Min(readyCount, graphCount), graphCount);
                    }));
                });
            }, _lifetime.Token);

            if (_lifetime.IsCancellationRequested) return;

            // FACM embeds only the pet rendering/interaction layer. VPet's full simulator toolbar
            // (Feed / Panel / Interaction / System) is intentionally disabled here because its
            // product actions are not wired into FACM and would create a second, non-functional menu.
            if (_main.ToolBar != null)
                _main.ToolBar.Visibility = System.Windows.Visibility.Collapsed;

            _root.Children.Remove(_statusCard);
            _main.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            await _ipc.SendEventAsync("ready", $"vpet-core-1.1.0.66;graphs={graphCount};startup-ms={started.ElapsedMilliseconds}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetStatus("高精度桌宠启动失败\n" + Trim(exception.Message, 120) + "\n右键可返回 FACM 菜单", includeNotice: false);
            await _ipc.SendEventAsync("error", exception.GetType().Name + ": " + exception.Message);
        }
    }

    private void HandleBootstrapProgress(string message)
    {
        var text = message ?? string.Empty;
        var fraction = Regex.Match(text, @"(?<current>\d+)\s*/\s*(?<total>\d+)");
        if (fraction.Success &&
            int.TryParse(fraction.Groups["current"].Value, out var current) &&
            int.TryParse(fraction.Groups["total"].Value, out var total) &&
            total > 0)
        {
            _bootstrapProgressTotal = total;
            SetCacheProgress(Math.Clamp(current, 0, total), total);
            return;
        }

        var manifest = Regex.Match(text, @"官方动作集\s+(?<total>\d+)\s+个文件");
        if (manifest.Success && int.TryParse(manifest.Groups["total"].Value, out total) && total > 0)
        {
            _bootstrapProgressTotal = total;
            SetCacheProgress(0, total);
            return;
        }

        if (_bootstrapProgressTotal > 0 && text.Contains("资源准备完成", StringComparison.Ordinal))
        {
            SetCacheProgress(_bootstrapProgressTotal, _bootstrapProgressTotal);
            return;
        }

        SetStatus(text);
    }

    private void SetStatus(string message, bool includeNotice = true)
    {
        _statusText.Text = PetHostUiText.Translate(message);
        _statusProgress.Visibility = Visibility.Collapsed;
        _statusProgressText.Visibility = Visibility.Collapsed;
        _statusNotice.Visibility = includeNotice ? Visibility.Visible : Visibility.Collapsed;
        Title = PetHostUiText.Translate("FACM PetHost");
    }

    private void SetCacheProgress(int readyCount, int graphCount)
    {
        var total = Math.Max(1, graphCount);
        var current = Math.Clamp(readyCount, 0, total);
        var percent = current * 100d / total;

        _statusText.Text = PetHostUiText.Translate("正在编译着色器…");
        _statusProgress.Maximum = total;
        _statusProgress.Value = current;
        _statusProgress.Visibility = Visibility.Visible;
        _statusProgressText.Text = $"{percent:0}%   {current}/{graphCount}";
        _statusProgressText.Visibility = Visibility.Visible;
        _statusNotice.Visibility = Visibility.Visible;
        Title = PetHostUiText.Translate("FACM PetHost");
    }

    private void HandleCommand(string line)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var command = line.Split('|')[0].Trim().ToLowerInvariant();
            switch (command)
            {
                case "activate":
                    if (!IsVisible) Show();
                    Topmost = true;
                    break;
                case "reset":
                    _controller.ResetToPrimaryScreen();
                    break;
                case "stop":
                    Close();
                    break;
                case "ping":
                    _ = _ipc.SendEventAsync("pong");
                    break;
            }
        }));
    }

    private void OnPreviewLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _leftTracking = true;
        _leftDownPoint = FormsControl.MousePosition;
        _leftDownTicks = Stopwatch.GetTimestamp();
    }

    private void OnPreviewLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftTracking) return;
        _leftTracking = false;
        var now = FormsControl.MousePosition;
        var distance = Math.Abs(now.X - _leftDownPoint.X) + Math.Abs(now.Y - _leftDownPoint.Y);
        var elapsedMs = (Stopwatch.GetTimestamp() - _leftDownTicks) * 1000d / Stopwatch.Frequency;
        if (distance <= 7 && elapsedMs <= 650 && IsFacmOpenHit(e))
            _ = _ipc.SendEventAsync("click");
    }

    private bool IsFacmOpenHit(MouseButtonEventArgs e)
    {
        // FACM's open-panel bridge must not turn the whole transparent 330x330 host into a hit target.
        // Reuse VPet's own configured head/body rectangles, but do not handle the routed event; VPet keeps
        // receiving exactly the same touch/press interactions as before.
        if (_main == null || _core?.Graph?.GraphConfig == null) return false;
        var width = Math.Max(1d, _main.ActualWidth);
        var height = Math.Max(1d, _main.ActualHeight);
        var point = e.GetPosition(_main);
        var petX = point.X * 500d / width;
        var petY = point.Y * 500d / height;
        var config = _core.Graph.GraphConfig;
        return Contains(config.TouchHeadLocate, config.TouchHeadSize, petX, petY) ||
               Contains(config.TouchBodyLocate, config.TouchBodySize, petX, petY);
    }

    private static bool Contains(System.Windows.Point locate, System.Windows.Size size, double x, double y)
    {
        return x >= locate.X && x <= locate.X + size.Width && y >= locate.Y && y <= locate.Y + size.Height;
    }

    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // FACM owns right-click behavior in PetHost. Always consume the event so VPet Core never opens
        // its built-in full-simulator toolbar. Only the configured FACM hit area opens FACM's menu.
        var openFacmMenu = IsFacmOpenHit(e);
        e.Handled = true;
        if (_main?.ToolBar != null)
            _main.ToolBar.Visibility = System.Windows.Visibility.Collapsed;
        if (openFacmMenu)
            _ = _ipc.SendEventAsync("right-click");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        try { _lifetime.Cancel(); } catch { }
        try { _main?.Dispose(); } catch { }
        try { _core?.Graph?.Dispose(); } catch { }
        try { _ipc.Dispose(); } catch { }
        _lifetime.Dispose();
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未知错误";
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
    }
}
