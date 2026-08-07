using System.Diagnostics;
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
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace FACM.PetHost;

internal sealed class PetHostWindow : Window
{
    private readonly PetHostIpc _ipc;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Grid _root;
    private readonly Border _statusCard;
    private readonly TextBlock _statusText;
    private readonly PetWindowController _controller;
    private VPetMain? _main;
    private GameCore? _core;
    private System.Drawing.Point _leftDownPoint;
    private long _leftDownTicks;
    private bool _leftTracking;

    public PetHostWindow(PetHostIpc ipc)
    {
        _ipc = ipc;
        _controller = new PetWindowController(this);

        Title = "FACM PetHost";
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
            Text = "正在启动高精度桌宠…",
            Foreground = WpfBrushes.White,
            FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = WpfVerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Margin = new Thickness(18, 10, 18, 10)
        };

        _statusCard = new Border
        {
            Width = 278,
            MinHeight = 92,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(WpfColor.FromArgb(222, 12, 17, 28)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(120, 121, 155, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            VerticalAlignment = WpfVerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Child = _statusText
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
        var progress = new Progress<string>(message => SetStatus(message));
        var started = Stopwatch.StartNew();

        try
        {
            var bootstrapper = new VPetAssetBootstrapper();
            await bootstrapper.EnsureAsync(progress, _lifetime.Token);

            SetStatus("正在解析动作配置…");
            GraphCore.CachePath = PetHostPaths.CacheDirectory;
            Directory.CreateDirectory(GraphCore.CachePath);

            // VPet-Simulator.Windows initializes this before constructing any PNGAnimation. The Core package
            // defaults to a fixed 2000 MB ceiling; on a first run that can make later graph loaders wait
            // indefinitely once hundreds of high-resolution frames push the process over that threshold.
            // Mirror the official x64 initialization so first-run cache generation gets a machine-aware budget.
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

            // Keep the status card above the VPet control until all first-run graph caches are actually ready.
            _root.Children.Insert(0, _main);
            SetStatus($"正在生成动作缓存 0/{graphCount}\n首次启动会比之后慢");

            var lastReported = 0;
            await Task.Run(() =>
            {
                _main.LoadALL(readyCount =>
                {
                    if (readyCount <= Volatile.Read(ref lastReported)) return;
                    Volatile.Write(ref lastReported, readyCount);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetStatus($"正在生成动作缓存 {Math.Min(readyCount, graphCount)}/{graphCount}\n首次启动会比之后慢");
                    }));
                });
            }, _lifetime.Token);

            if (_lifetime.IsCancellationRequested) return;
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

    private void SetStatus(string message, bool includeNotice = true)
    {
        _statusText.Text = includeNotice
            ? message + "\n动画来源：VPet / VUP-Simulator（非商用授权）"
            : message;
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
        if (distance <= 7 && elapsedMs <= 650)
            _ = _ipc.SendEventAsync("click");
    }

    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
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
