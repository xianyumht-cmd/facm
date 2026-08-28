using System.ComponentModel;
using FACM.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MaintenanceSettingsControl : UserControl
{
    private MaintenanceViewModel? _viewModel;
    private bool _syncing;
    private bool _loadedOnce;

    public MaintenanceSettingsControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event Action? ReplacementStarted;
    public event Action? ExitRequested;

    public void Configure(MaintenanceViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce || _viewModel is null) return;
        _loadedOnce = true;
        try
        {
            await _viewModel.InitializeAsync();
            await _viewModel.RefreshAnnouncementAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ApplyState();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The MainWindow owns the ViewModel lifetime. Keep the subscription while this control may
        // be reattached during the same window lifetime; MainWindow.Maintenance detaches on close.
    }

    internal void Detach()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyState();
            return;
        }
        _ = DispatcherQueue.TryEnqueue(ApplyState);
    }

    private async void OnAutoUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || _viewModel is null) return;
        await _viewModel.SetAutoUpdateEnabledAsync(AutoUpdateToggle.IsOn);
        ApplyState();
    }

    private async void OnCheckNowClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        try { await _viewModel.ManualCheckAsync(); } catch (OperationCanceledException) { }
        ApplyState();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.PrepareUpdateAsync();
        ApplyState();
    }

    private void OnCancelDownloadClick(object sender, RoutedEventArgs e) => _viewModel?.CancelUpdateDownload();

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.HasPreparedUpdate) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "安装 FACM 更新？",
            Content = "将启动管理员权限更新器。只有更新器成功启动后，当前 FACM 才会退出。",
            PrimaryButtonText = "安装并重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var result = await _viewModel.StartPreparedReplacementAsync();
        ApplyState();
        if (result.Started) ReplacementStarted?.Invoke();
    }

    private void OnForceExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private async void OnAnnouncementDetailClick(object sender, RoutedEventArgs e)
    {
        var uri = _viewModel?.AnnouncementDetailUri;
        if (uri is null) return;
        await Windows.System.Launcher.LaunchUriAsync(uri);
        if (_viewModel is not null) await _viewModel.MarkAnnouncementSeenAsync();
        ApplyState();
    }

    private async void OnOpenLogClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.OpenLogAsync();
        ApplyState();
    }

    private void ApplyState()
    {
        var vm = _viewModel;
        if (vm is null) return;
        _syncing = true;
        try
        {
            AutoUpdateToggle.IsOn = vm.AutoUpdateEnabled;
            CurrentVersionText.Text = vm.CurrentVersion;
            LatestVersionText.Text = string.IsNullOrWhiteSpace(vm.LatestVersion) ? "—" : vm.LatestVersion;
            UpdateStatusText.Text = StatusText(vm.Status, vm.ForceUpdateRequired);
            ReleaseNotesText.Text = vm.ReleaseNotes;
            ReleaseNotesText.Visibility = string.IsNullOrWhiteSpace(vm.ReleaseNotes) ? Visibility.Collapsed : Visibility.Visible;
            CheckNowButton.IsEnabled = !vm.IsBusy;
            DownloadButton.IsEnabled = vm.CanPrepareUpdate;
            CancelDownloadButton.IsEnabled = vm.IsBusy && vm.UpdateProgressStage is "connecting" or "downloading" or "verifying";
            InstallButton.IsEnabled = vm.HasPreparedUpdate && !vm.IsBusy;
            ForceExitButton.Visibility = vm.ForceUpdateRequired ? Visibility.Visible : Visibility.Collapsed;
            ForceExitButton.IsEnabled = !vm.IsBusy;
            UpdateProgressBar.Value = vm.UpdateProgressPercent;
            UpdateProgressBar.Visibility = vm.UpdateProgressPercent > 0 || vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgressText.Text = vm.UpdateProgressStage;
            UpdateProgressText.Visibility = string.IsNullOrWhiteSpace(vm.UpdateProgressStage) ? Visibility.Collapsed : Visibility.Visible;

            var announcement = vm.Announcement;
            AnnouncementTitleText.Text = announcement?.Title ?? "暂无公告";
            AnnouncementBodyText.Text = announcement?.Body ?? string.Empty;
            AnnouncementDetailButton.IsEnabled = vm.AnnouncementDetailUri is not null;
            AnnouncementDetailButton.Visibility = vm.AnnouncementDetailUri is null ? Visibility.Collapsed : Visibility.Visible;
            OpenLogButton.IsEnabled = vm.CanOpenLog && !vm.IsBusy;
        }
        finally
        {
            _syncing = false;
        }
    }

    private static string StatusText(string status, bool forceUpdateRequired) => status switch
    {
        "ready" => "维护功能已就绪",
        "recovery-loaded-no-save" => "已从恢复设置加载；未覆盖主设置",
        "checking" => "正在检查更新…",
        "update-available" when forceUpdateRequired => "发现必须更新的版本：请选择更新或退出 FACM",
        "update-available" => "发现新版本",
        "up-to-date" => "当前已是最新版本",
        "manifest-unavailable" => "暂时无法获取更新信息",
        "updates-disabled" => "更新清单当前已停用",
        "update-downloading" => "正在下载更新…",
        "update-prepared" => "更新已下载并校验，等待确认安装",
        "update-download-cancelled" => "更新下载已取消",
        "update-download-failed" => "更新下载或校验失败",
        "update-starting" => "正在启动更新器…",
        "replacement-started" => "更新器已启动",
        "launcher-not-started" => "未启动更新器；当前 FACM 保持运行",
        "log-opened" => "已打开当前日志",
        _ => status
    };
}
