using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FACM.App.Models;
using FACM.App.Services;

namespace FACM.App;

public partial class MainWindow : Window
{
    private readonly PayloadService _payloadService = new();
    private readonly MaintenanceService _maintenanceService;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _maintenanceService = new MaintenanceService(_payloadService.AppDataRoot);
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        CheckCurrentSignature();
        await LoadToolsAsync();
    }

    private async Task LoadToolsAsync()
    {
        try
        {
            SetStatus("正在读取内置工具清单…");
            PayloadManifest manifest = await _payloadService.LoadManifestAsync();
            ToolsPanel.Children.Clear();

            foreach (PayloadDefinition payload in manifest.Payloads)
            {
                ToolsPanel.Children.Add(CreateToolButton(payload));
            }

            ToolCountText.Text = $"{manifest.Payloads.Count} 项";
            EmptyToolsPanel.Visibility = manifest.Payloads.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            AddLog(manifest.Payloads.Count == 0
                ? "内置工具清单为空，等待接入原程序资源。"
                : $"已加载 {manifest.Payloads.Count} 个内置工具。 ");
            SetStatus("就绪");
        }
        catch (Exception ex)
        {
            EmptyToolsPanel.Visibility = Visibility.Visible;
            EmptyToolsPanel.Child = new TextBlock
            {
                Text = $"内置工具清单读取失败：{ex.Message}",
                Foreground = (Brush)FindResource("WarningBrush"),
                TextWrapping = TextWrapping.Wrap
            };
            AddLog($"清单读取失败：{ex.Message}");
            SetStatus("清单读取失败");
        }
    }

    private Button CreateToolButton(PayloadDefinition payload)
    {
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            Text = payload.DisplayName,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(payload.Description)
                ? "点击后校验并运行"
                : payload.Description,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondary"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = payload.RequiresElevation ? "按需请求管理员权限" : "以当前用户权限运行",
            FontSize = 10,
            Foreground = payload.RequiresElevation
                ? (Brush)FindResource("WarningBrush")
                : (Brush)FindResource("SuccessBrush"),
            Margin = new Thickness(0, 10, 0, 0)
        });

        Button button = new()
        {
            Content = content,
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(6),
            Padding = new Thickness(16, 14, 16, 14),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 102,
            Tag = payload
        };

        button.Click += async (_, _) => await RunPayloadAsync(payload, button);
        return button;
    }

    private async Task RunPayloadAsync(PayloadDefinition payload, Button sourceButton)
    {
        if (_busy)
        {
            return;
        }

        string permissionText = payload.RequiresElevation
            ? "该项目会单独触发 Windows 管理员权限确认。"
            : "该项目会使用当前用户权限运行。";

        MessageBoxResult confirmation = MessageBox.Show(
            $"即将运行：{payload.DisplayName}\n\n{permissionText}\n程序会先释放到 FACM 数据目录并校验 SHA-256。",
            "确认运行",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (confirmation != MessageBoxResult.OK)
        {
            AddLog($"已取消：{payload.DisplayName}");
            return;
        }

        try
        {
            _busy = true;
            sourceButton.IsEnabled = false;
            SetStatus($"正在校验并启动 {payload.DisplayName}…");
            AddLog($"开始处理：{payload.DisplayName}");

            PayloadRunResult result = await _payloadService.ExtractAndRunAsync(payload);
            AddLog(result.Started
                ? $"{payload.DisplayName}：{result.Message}"
                : $"{payload.DisplayName}：启动失败，{result.Message}");

            if (!result.Started)
            {
                MessageBox.Show(result.Message, "FACM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AddLog($"用户取消权限请求：{payload.DisplayName}");
        }
        catch (Exception ex)
        {
            AddLog($"{payload.DisplayName} 失败：{ex.Message}");
            MessageBox.Show(ex.Message, "运行失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sourceButton.IsEnabled = true;
            _busy = false;
            SetStatus("就绪");
        }
    }

    private async void CleanCacheButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            _busy = true;
            CleanCacheButton.IsEnabled = false;
            SetStatus("正在检查 FACM 临时文件…");
            MaintenancePreview preview = await _maintenanceService.InspectAsync();

            if (preview.FileCount == 0)
            {
                AddLog("未发现可清理的 FACM 临时文件。");
                MessageBox.Show("当前没有可清理的 FACM 临时文件。", "FACM",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string warnings = preview.Warnings.Count == 0
                ? string.Empty
                : $"\n\n另有 {preview.Warnings.Count} 个项目被安全跳过。";

            MessageBoxResult confirmation = MessageBox.Show(
                $"将清理 FACM 自身创建的 {preview.FileCount} 个文件，约 {FormatBytes(preview.TotalBytes)}。{warnings}\n\n不会处理其他程序的安装目录。",
                "确认清理",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.OK)
            {
                AddLog("用户取消临时文件清理。");
                return;
            }

            SetStatus("正在清理 FACM 临时文件…");
            MaintenanceResult result = await _maintenanceService.CleanAsync();
            AddLog($"清理完成：删除 {result.DeletedFiles} 个文件、{result.DeletedDirectories} 个目录；失败 {result.Failures.Count} 项。");

            MessageBox.Show(
                result.Failures.Count == 0
                    ? $"清理完成，共删除 {result.DeletedFiles} 个文件。"
                    : $"清理已完成，但有 {result.Failures.Count} 项因占用或权限不足未删除。",
                "FACM",
                MessageBoxButton.OK,
                result.Failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AddLog($"清理失败：{ex.Message}");
            MessageBox.Show(ex.Message, "清理失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CleanCacheButton.IsEnabled = true;
            _busy = false;
            SetStatus("就绪");
        }
    }

    private void OpenDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_payloadService.AppDataRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = _payloadService.AppDataRoot,
            UseShellExecute = true
        });
        AddLog("已打开 FACM 数据目录。");
    }

    private void ClearLogButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogList.Items.Clear();
        AddLog("运行记录已清空。");
    }

    private void CheckCurrentSignature()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(processPath);
            SignatureStatusText.Text = $"当前构建：已包含签名 · {certificate.Subject}";
            SignatureStatusText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        catch (CryptographicException)
        {
            SignatureStatusText.Text = "当前构建：未签名";
            SignatureStatusText.Foreground = (Brush)FindResource("WarningBrush");
        }
        catch
        {
            SignatureStatusText.Text = "当前构建：签名状态无法读取";
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void SetStatus(string text) => StatusText.Text = text;

    private void AddLog(string message)
    {
        LogList.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogList.Items.Count > 100)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
