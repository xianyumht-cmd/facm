using System.ComponentModel;
using FACM.App.ViewModels;
using FACM.Core.Cleanup;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private readonly ControlCenterViewModel _controlCenter;
    private readonly CleanupViewModel _cleanupCenter;
    private readonly RepairToolsViewModel _repairTools;
    private readonly LeagueWorkbenchViewModel _leagueWorkbench;
    private readonly DiagnosticsCenterViewModel _diagnosticsCenter;
    private readonly IUiTextProvider _text;
    private bool _closed;
    private bool _cleanupInitialized;
    private bool _cleanupUiBusy;
    private bool _diagnosticsLoaded;
    private bool _diagnosticsBusy;

    public MainWindow(
        ControlCenterViewModel controlCenter,
        CleanupViewModel cleanupCenter,
        RepairToolsViewModel repairTools,
        LeagueWorkbenchViewModel leagueWorkbench,
        DiagnosticsCenterViewModel diagnosticsCenter,
        IUiTextProvider text)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _cleanupCenter = cleanupCenter ?? throw new ArgumentNullException(nameof(cleanupCenter));
        _repairTools = repairTools ?? throw new ArgumentNullException(nameof(repairTools));
        _leagueWorkbench = leagueWorkbench ?? throw new ArgumentNullException(nameof(leagueWorkbench));
        _diagnosticsCenter = diagnosticsCenter ?? throw new ArgumentNullException(nameof(diagnosticsCenter));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            StartupFailureObserver.TryWrite(exception, "main-window-xaml-failure.txt");
            throw;
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyStaticText();
        ApplyLeagueRuntimeState();
        ApplyCleanupRuntimeState();
        ApplyRepairToolsState();
        InitializePersonalizationSurface();
        _cleanupCenter.PropertyChanged += OnCleanupPropertyChanged;
        _leagueWorkbench.PropertyChanged += OnLeagueWorkbenchPropertyChanged;
        Closed += OnClosed;
        RootNavigation.SelectedItem = RepairNav;
        RootNavigation.Loaded += OnRootNavigationLoaded;
    }

    public void NavigateToSection(string section)
    {
        if (_closed) return;
        var normalized = section switch
        {
            "league" => "league",
            "personalization" => "personalization",
            "settings" => "settings",
            _ => "repair"
        };

        var target = normalized switch
        {
            "league" => LeagueNav,
            "personalization" => PersonalizationNav,
            "settings" => SettingsNav,
            _ => RepairNav
        };
        RootNavigation.SelectedItem = target;
        ApplySection(normalized);
    }

    private void ApplyStaticText()
    {
        var appName = _text.Get(UiTextKeys.AppName);
        var repairText = _text.Get(UiTextKeys.ShellRepairTools);
        var leagueText = _text.Get(UiTextKeys.ShellLeague);
        var personalizationText = _text.Get(UiTextKeys.ShellPersonalization);
        var settingsText = _text.Get(UiTextKeys.ShellMoreSettings);

        Title = appName + " 4.0";
        TitleBarText.Text = appName;
        ProductTitle.Text = appName + " 4.0";
        RepairNav.Content = repairText;
        LeagueNav.Content = leagueText;
        PersonalizationNav.Content = personalizationText;
        SettingsNav.Content = settingsText;
        AutomationProperties.SetName(RepairNav, repairText);
        AutomationProperties.SetHelpText(RepairNav, _text.Get(UiTextKeys.ShellRepairSubtitle));
        AutomationProperties.SetName(LeagueNav, leagueText);
        AutomationProperties.SetHelpText(LeagueNav, _text.Get(UiTextKeys.ShellLeagueSubtitle));
        AutomationProperties.SetName(PersonalizationNav, personalizationText);
        AutomationProperties.SetHelpText(PersonalizationNav, _text.Get(UiTextKeys.ShellPersonalizationSubtitle));
        AutomationProperties.SetName(SettingsNav, settingsText);
        AutomationProperties.SetHelpText(SettingsNav, _text.Get(UiTextKeys.ShellMoreSettingsSubtitle));

        StatusLabel.Text = _text.Get(UiTextKeys.ShellStatusLabel);
        OverviewTitle.Text = _text.Get(UiTextKeys.ShellOverviewTitle);
        OverviewBody.Text = _text.Get(UiTextKeys.ShellOverviewBody);
        StateTitle.Text = _text.Get(UiTextKeys.ShellStateTitle);
        StateBody.Text = _text.Get(UiTextKeys.ShellStateBody);

        RepairToolsTitle.Text = _text.Get(UiTextKeys.RepairToolsTitle);
        RepairToolsDescription.Text = _text.Get(UiTextKeys.RepairToolsDescription);
        RepairPrivilegeLabel.Text = _text.Get(UiTextKeys.RepairPrivilegeLabel);
        RepairDriverCleanupButton.Content = _text.Get(UiTextKeys.RepairDriverCleanup);
        RepairDriverCleanupHint.Text = _text.Get(UiTextKeys.RepairDriverCleanupHint);
        AutomationProperties.SetName(RepairDriverCleanupButton, _text.Get(UiTextKeys.RepairDriverCleanup));
        AutomationProperties.SetHelpText(RepairDriverCleanupButton, _text.Get(UiTextKeys.RepairDriverCleanupHint));

        CleanupDirectoryTitle.Text = _text.Get(UiTextKeys.CleanupDirectoryTitle);
        CleanupDirectoryDescription.Text = _text.Get(UiTextKeys.CleanupDirectoryDescription);
        CleanupDetectButton.Content = _text.Get(UiTextKeys.CleanupAutoDetect);
        CleanupSelectButton.Content = _text.Get(UiTextKeys.CleanupSelectDirectory);
        CleanupPreviewTitle.Text = _text.Get(UiTextKeys.CleanupPreviewTitle);
        CleanupPreviewDescription.Text = _text.Get(UiTextKeys.CleanupPreviewDescription);
        CleanupSafetyHint.Text = _text.Get(UiTextKeys.CleanupSafetyHint);
        CleanupPreviewButton.Content = _text.Get(UiTextKeys.CleanupPreview);
        AutomationProperties.SetName(CleanupDetectButton, _text.Get(UiTextKeys.CleanupAutoDetect));
        AutomationProperties.SetName(CleanupSelectButton, _text.Get(UiTextKeys.CleanupSelectDirectory));
        AutomationProperties.SetName(CleanupPreviewButton, _text.Get(UiTextKeys.CleanupPreview));
        AutomationProperties.SetHelpText(CleanupPreviewButton, _text.Get(UiTextKeys.CleanupPreviewDescription));

        LeagueStateLabel.Text = _text.Get(UiTextKeys.LeagueWorkbenchStateLabel);
        LeagueBudgetLabel.Text = _text.Get(UiTextKeys.LeagueWorkbenchBudgetLabel);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Match),
            LeagueMatchTitle,
            LeagueMatchDescription);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Strategy),
            LeagueStrategyTitle,
            LeagueStrategyDescription);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Automation),
            LeagueAutomationTitle,
            LeagueAutomationDescription);

        var diagnosticsSummaryLabel = _text.Get(UiTextKeys.DiagnosticsSummaryLabel);
        var diagnosticsRefresh = _text.Get(UiTextKeys.DiagnosticsRefresh);
        var diagnosticsCopy = _text.Get(UiTextKeys.DiagnosticsCopySummary);
        var diagnosticsExport = _text.Get(UiTextKeys.DiagnosticsExportBundle);
        DiagnosticsTitle.Text = _text.Get(UiTextKeys.DiagnosticsTitle);
        DiagnosticsSubtitle.Text = _text.Get(UiTextKeys.DiagnosticsSubtitle);
        DiagnosticsSummaryLabel.Text = diagnosticsSummaryLabel;
        DiagnosticsRefreshButton.Content = diagnosticsRefresh;
        DiagnosticsCopyButton.Content = diagnosticsCopy;
        DiagnosticsExportButton.Content = diagnosticsExport;
        DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusReady);
        AutomationProperties.SetName(DiagnosticsSummaryText, diagnosticsSummaryLabel);
        AutomationProperties.SetHelpText(DiagnosticsSummaryText, _text.Get(UiTextKeys.DiagnosticsSubtitle));
        AutomationProperties.SetName(DiagnosticsRefreshButton, diagnosticsRefresh);
        AutomationProperties.SetHelpText(DiagnosticsRefreshButton, _text.Get(UiTextKeys.DiagnosticsRefreshHelp));
        AutomationProperties.SetName(DiagnosticsCopyButton, diagnosticsCopy);
        AutomationProperties.SetHelpText(DiagnosticsCopyButton, _text.Get(UiTextKeys.DiagnosticsCopySummaryHelp));
        AutomationProperties.SetName(DiagnosticsExportButton, diagnosticsExport);
        AutomationProperties.SetHelpText(DiagnosticsExportButton, _text.Get(UiTextKeys.DiagnosticsExportBundleHelp));

        ApplySection("repair");
        ApplyStatus(UiTextKeys.ShellStatusReady);
    }

    private void ApplyWorkbenchSectionText(
        LeagueWorkbenchSection section,
        TextBlock title,
        TextBlock description)
    {
        title.Text = _text.Get(section.TitleTextKey);
        description.Text = _text.Get(section.DescriptionTextKey);
    }

    private async void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= OnRootNavigationLoaded;
        try
        {
            var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(4, 0, 0);
            await _controlCenter.RefreshAsync(currentVersion);
            ApplyStatus(_controlCenter.StatusTextKey);
            await EnsureCleanupInitializedAsync();
        }
        catch (Exception)
        {
            ApplyStatus(UiTextKeys.ShellStatusUnavailable);
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item) return;
        ApplySection(item.Tag?.ToString() ?? "repair");
    }

    private void ApplySection(string tag)
    {
        var isRepair = string.Equals(tag, "repair", StringComparison.Ordinal);
        var isLeague = string.Equals(tag, "league", StringComparison.Ordinal);
        var isSettings = string.Equals(tag, "settings", StringComparison.Ordinal);
        var (titleKey, subtitleKey) = tag switch
        {
            "league" => (UiTextKeys.ShellLeague, UiTextKeys.ShellLeagueSubtitle),
            "personalization" => (UiTextKeys.ShellPersonalization, UiTextKeys.ShellPersonalizationSubtitle),
            "settings" => (UiTextKeys.ShellMoreSettings, UiTextKeys.ShellMoreSettingsSubtitle),
            _ => (UiTextKeys.ShellRepairTools, UiTextKeys.ShellRepairSubtitle)
        };
        SectionTitle.Text = _text.Get(titleKey);
        SectionSubtitle.Text = _text.Get(subtitleKey);
        CleanupPanel.Visibility = isRepair ? Visibility.Visible : Visibility.Collapsed;
        GeneralOverviewGrid.Visibility = !isRepair && !isLeague ? Visibility.Visible : Visibility.Collapsed;
        LeagueWorkbenchPanel.Visibility = isLeague ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        if (isRepair)
        {
            ApplyRepairToolsState();
            _ = EnsureCleanupInitializedAsync();
        }
        if (isLeague) ApplyLeagueRuntimeState();
        if (isSettings && !_diagnosticsLoaded) _ = RefreshDiagnosticsAsync();
    }

    private async Task EnsureCleanupInitializedAsync()
    {
        if (_cleanupInitialized || _cleanupUiBusy || _closed) return;
        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.InitializeAsync();
            _cleanupInitialized = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
        }
    }

    private void OnRepairDriverCleanupClick(object sender, RoutedEventArgs args)
    {
        if (_repairTools.IsBusy) return;
        _ = _repairTools.LaunchDriverCleanup();
        ApplyRepairToolsState();
    }

    private void ApplyRepairToolsState()
    {
        if (_closed) return;
        RepairPrivilegeStatus.Text = _text.Get(
            _cleanupCenter.IsAdministrator
                ? UiTextKeys.RepairPrivilegeAdministrator
                : UiTextKeys.RepairPrivilegeStandard);
        RepairToolStatus.Text = _text.Get(_repairTools.StatusTextKey);
        RepairToolDetail.Text = _repairTools.StatusDetail;
        RepairDriverCleanupButton.IsEnabled = !_repairTools.IsBusy;
    }

    private async void OnCleanupDetectClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.DetectAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
        }
    }

    private async void OnCleanupSelectClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.SetSelectedPathAsync(folder.Path);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
        }
    }

    private async void OnCleanupPreviewClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        _cleanupUiBusy = true;
        CleanupProgressBar.IsIndeterminate = true;
        CleanupProgressBar.Visibility = Visibility.Visible;
        try
        {
            var plan = await _cleanupCenter.PreviewAsync();
            ApplyCleanupRuntimeState();
            if (plan is not null) await ShowCleanupReviewAsync(plan);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            CleanupProgressBar.Visibility = Visibility.Collapsed;
            ApplyCleanupRuntimeState();
        }
    }

    private async Task ShowCleanupReviewAsync(CleanupPlan plan)
    {
        var summary = new TextBlock
        {
            Text = BuildCleanupSummary(plan),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        var targets = new ListView
        {
            MaxHeight = 360,
            SelectionMode = ListViewSelectionMode.None,
            ItemsSource = plan.Targets.Select(FormatCleanupTarget).ToArray()
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = _text.Get(UiTextKeys.CleanupPreviewDescription),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        content.Children.Add(summary);
        content.Children.Add(targets);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _text.Get(UiTextKeys.CleanupConfirmTitle),
            Content = content,
            PrimaryButtonText = _text.Get(UiTextKeys.CleanupConfirmPrimary),
            CloseButtonText = _text.Get(UiTextKeys.CleanupCancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = plan.DeletableTargets.Count > 0
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        if (_cleanupCenter.RequiresElevation)
        {
            await ShowCleanupElevationAsync();
            return;
        }

        await ExecuteCleanupAsync();
    }

    private async Task ShowCleanupElevationAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _text.Get(UiTextKeys.CleanupRequiresAdmin),
            Content = _text.Get(UiTextKeys.CleanupConfirmBody),
            PrimaryButtonText = _text.Get(UiTextKeys.CleanupRestartElevated),
            CloseButtonText = _text.Get(UiTextKeys.CleanupCancel),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var started = _cleanupCenter.RestartElevatedForCleanup();
        if (!started)
        {
            CleanupOperationStatus.Text = _text.Get(UiTextKeys.CleanupFailed);
            return;
        }

        CleanupOperationStatus.Text = _text.Get(UiTextKeys.CleanupRequiresAdmin);
        _ = DispatcherQueue.TryEnqueue(() => Application.Current.Exit());
    }

    private async Task ExecuteCleanupAsync()
    {
        if (_cleanupUiBusy || _cleanupCenter.CurrentPlan is null) return;
        _cleanupUiBusy = true;
        CleanupProgressBar.IsIndeterminate = false;
        CleanupProgressBar.Minimum = 0;
        CleanupProgressBar.Maximum = Math.Max(1, _cleanupCenter.CurrentPlan.Targets.Count);
        CleanupProgressBar.Value = 0;
        CleanupProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<CleanupProgress>(item =>
        {
            CleanupProgressBar.Maximum = Math.Max(1, item.TotalTargets);
            CleanupProgressBar.Value = Math.Min(item.CompletedTargets, item.TotalTargets);
            CleanupOperationStatus.Text = item.CurrentTarget;
        });
        try
        {
            var result = await _cleanupCenter.ExecuteConfirmedAsync(progress);
            ApplyCleanupRuntimeState();
            var resultDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = result.Success ? _text.Get(UiTextKeys.CleanupComplete) : _text.Get(UiTextKeys.CleanupFailed),
                Content = BuildCleanupResult(result),
                CloseButtonText = _text.Get(UiTextKeys.CleanupConfirmPrimary)
            };
            await resultDialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            CleanupProgressBar.Visibility = Visibility.Collapsed;
            ApplyCleanupRuntimeState();
        }
    }

    private string BuildCleanupSummary(CleanupPlan plan)
    {
        var summary = plan.Summary;
        var parts = new[]
        {
            $"{summary.TargetCount} {_text.Get(UiTextKeys.CleanupTargetSummary)}",
            $"{summary.FileCount} files / {summary.DirectoryCount} folders",
            FormatBytes(summary.EstimatedBytes),
            $"{summary.BlockedCount} {_text.Get(UiTextKeys.CleanupBlocked)}"
        };
        return string.Join(" · ", parts);
    }

    private static string FormatCleanupTarget(CleanupTarget target)
    {
        var detail = target.IsBlocked
            ? $"BLOCKED · {target.BlockedReason}"
            : $"{target.FileCount} files · {target.DirectoryCount} folders · {FormatBytes(target.EstimatedBytes)}";
        return $"{target.FullPath}\n{detail}";
    }

    private string BuildCleanupResult(CleanupResult result)
    {
        var lines = new List<string>
        {
            $"Deleted files: {result.DeletedFiles}",
            $"Deleted folders: {result.DeletedDirectories}"
        };
        if (result.Failures.Count > 0)
        {
            lines.Add($"Failures: {result.Failures.Count}");
            lines.AddRange(result.Failures);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):F1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):F2} GB";
    }

    private void ApplyCleanupRuntimeState()
    {
        CleanupPathText.Text = string.IsNullOrWhiteSpace(_cleanupCenter.GamePath)
            ? _text.Get(UiTextKeys.CleanupDirectoryMissing)
            : _cleanupCenter.GamePath;
        CleanupDirectoryStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        CleanupDirectoryDetail.Text = _cleanupCenter.StatusDetail;
        CleanupOperationStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        CleanupDetectButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy;
        CleanupSelectButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy;
        CleanupPreviewButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy && _cleanupCenter.IsGamePathValid;
    }

    private void OnCleanupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed) return;
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
        });
    }

    private void ApplyLeagueRuntimeState()
    {
        var snapshot = _leagueWorkbench.Snapshot;
        LeagueStateValue.Text = _text.Get(LeagueWorkbenchTextKeys.ForState(snapshot.League));
        LeagueBudgetValue.Text = snapshot.Budget.Name + " · " + snapshot.Budget.RefreshInterval.TotalMilliseconds.ToString("F0") + " ms";
        QueueLeagueWorkbenchSurfaceRefresh();
    }

    private void OnLeagueWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyLeagueRuntimeState();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed) ApplyLeagueRuntimeState();
        });
    }

    private void ApplyStatus(string textKey)
    {
        StatusValue.Text = _text.Get(textKey);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (_diagnosticsBusy || _closed) return;
        _diagnosticsBusy = true;
        try
        {
            await _diagnosticsCenter.RefreshAsync();
            _diagnosticsLoaded = true;
            ApplyDiagnosticsRuntimeState();
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            _diagnosticsBusy = false;
            ApplyDiagnosticsBusyState();
        }
    }

    private async void OnDiagnosticsRefreshClick(object sender, RoutedEventArgs args)
    {
        await RefreshDiagnosticsAsync();
    }

    private void OnDiagnosticsCopyClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(_diagnosticsCenter.SummaryText);
            Clipboard.SetContent(package);
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusCopied);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
    }

    private async void OnDiagnosticsExportClick(object sender, RoutedEventArgs args)
    {
        if (_diagnosticsBusy) return;
        _diagnosticsBusy = true;
        try
        {
            var result = await _diagnosticsCenter.ExportAsync();
            DiagnosticsStatus.Text = result.Success
                ? _text.Get(UiTextKeys.DiagnosticsStatusExported) + " · " + result.BundlePath
                : _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            _diagnosticsBusy = false;
            ApplyDiagnosticsBusyState();
        }
    }

    private void ApplyDiagnosticsRuntimeState()
    {
        DiagnosticsSummaryText.Text = _diagnosticsCenter.SummaryText;
        DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusRefreshed);
    }

    private void ApplyDiagnosticsBusyState()
    {
        var enabled = !_diagnosticsBusy;
        DiagnosticsRefreshButton.IsEnabled = enabled;
        DiagnosticsCopyButton.IsEnabled = enabled;
        DiagnosticsExportButton.IsEnabled = enabled;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        Closed -= OnClosed;
        RootNavigation.Loaded -= OnRootNavigationLoaded;
        _cleanupCenter.PropertyChanged -= OnCleanupPropertyChanged;
        _leagueWorkbench.PropertyChanged -= OnLeagueWorkbenchPropertyChanged;
        _leagueWorkbench.Dispose();
    }
}
