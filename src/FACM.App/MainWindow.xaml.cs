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
        LeagueWorkbenchViewModel leagueWorkbench,
        DiagnosticsCenterViewModel diagnosticsCenter,
        IUiTextProvider text)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _cleanupCenter = cleanupCenter ?? throw new ArgumentNullException(nameof(cleanupCenter));
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
        if (isRepair) _ = EnsureCleanupInitializedAsync();
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
        }
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
            if (_closed) return;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                CleanupProgressBar.Value = Math.Clamp(item.CompletedTargets, 0, Math.Max(1, item.TotalTargets));
                CleanupOperationStatus.Text = _text.Get(UiTextKeys.CleanupExecuting) + " " + item.CurrentTarget;
            });
        });
        try
        {
            var result = await _cleanupCenter.ExecuteConfirmedAsync(confirmed: true, progress);
            ApplyCleanupRuntimeState();
            if (result is null) return;
            await ShowCleanupResultAsync(result);
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

    private async Task ShowCleanupResultAsync(CleanupResult result)
    {
        var body = result.Failures.Count == 0
            ? $"{_text.Get(UiTextKeys.CleanupComplete)}\n{result.DeletedFiles} files / {result.DeletedDirectories} folders"
            : $"{_text.Get(UiTextKeys.CleanupFailed)}\n{string.Join(Environment.NewLine, result.Failures.Take(12))}";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = result.Failures.Count == 0
                ? _text.Get(UiTextKeys.CleanupComplete)
                : _text.Get(UiTextKeys.CleanupFailed),
            Content = body,
            CloseButtonText = _text.Get(UiTextKeys.CleanupConfirmPrimary)
        };
        await dialog.ShowAsync();
    }

    private void OnCleanupPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        _ = DispatcherQueue.TryEnqueue(ApplyCleanupRuntimeState);
    }

    private void ApplyCleanupRuntimeState()
    {
        if (_closed) return;
        CleanupPathText.Text = string.IsNullOrWhiteSpace(_cleanupCenter.GamePath)
            ? _text.Get(UiTextKeys.CleanupDirectoryMissing)
            : _cleanupCenter.GamePath;
        CleanupDirectoryStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        CleanupDirectoryDetail.Text = TranslateCleanupDetail(_cleanupCenter.StatusDetail);
        CleanupOperationStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        var busy = _cleanupUiBusy || _cleanupCenter.IsBusy;
        CleanupDetectButton.IsEnabled = !busy;
        CleanupSelectButton.IsEnabled = !busy;
        CleanupPreviewButton.IsEnabled = !busy && _cleanupCenter.IsGamePathValid;
    }

    private string TranslateCleanupDetail(string detail) =>
        string.Equals(detail, UiTextKeys.CleanupPathRecoveryReadOnly, StringComparison.Ordinal)
            ? _text.Get(UiTextKeys.CleanupPathRecoveryReadOnly)
            : detail;

    private string BuildCleanupSummary(CleanupPlan plan)
    {
        var summary = plan.Summary;
        return $"{_text.Get(UiTextKeys.CleanupTargetSummary)}: {summary.TargetCount} targets / {summary.FileCount} files / {summary.DirectoryCount} folders / {FormatBytes(summary.EstimatedBytes)}\n{_text.Get(UiTextKeys.CleanupBlocked)}: {plan.BlockedTargets.Count}";
    }

    private string FormatCleanupTarget(CleanupTarget target)
    {
        var prefix = target.Blocked ? $"[{_text.Get(UiTextKeys.CleanupBlocked)}] " : string.Empty;
        return $"{prefix}{target.Group}\n{target.Path}\n{target.FileCount} files / {target.DirectoryCount} folders / {FormatBytes(target.EstimatedBytes)} · {target.Detail}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{Math.Max(0, bytes)} B";
        var value = bytes / 1024d;
        if (value < 1024) return $"{value:0.0} KB";
        value /= 1024d;
        if (value < 1024) return $"{value:0.0} MB";
        value /= 1024d;
        return $"{value:0.00} GB";
    }

    private void OnLeagueWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        if (args.PropertyName is not (
            nameof(LeagueWorkbenchViewModel.LeagueState) or
            nameof(LeagueWorkbenchViewModel.LeagueStateTextKey) or
            nameof(LeagueWorkbenchViewModel.BudgetName)))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyLeagueRuntimeState);
    }

    private void ApplyLeagueRuntimeState()
    {
        if (_closed) return;
        LeagueStateValue.Text = _text.Get(_leagueWorkbench.LeagueStateTextKey);
        LeagueBudgetValue.Text = _leagueWorkbench.BudgetName;
    }

    private async void OnDiagnosticsRefreshClick(object sender, RoutedEventArgs args) =>
        await RefreshDiagnosticsAsync();

    private async void OnDiagnosticsCopyClick(object sender, RoutedEventArgs args)
    {
        if (_diagnosticsBusy) return;
        if (!_diagnosticsLoaded) await RefreshDiagnosticsAsync();
        if (string.IsNullOrWhiteSpace(_diagnosticsCenter.Summary)) return;

        try
        {
            var package = new DataPackage();
            package.SetText(_diagnosticsCenter.Summary);
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
        SetDiagnosticsBusy(true);
        try
        {
            _ = await _diagnosticsCenter.ExportAsync();
            _diagnosticsLoaded = true;
            DiagnosticsSummaryText.Text = _diagnosticsCenter.Summary;
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusExported);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            SetDiagnosticsBusy(false);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (_diagnosticsBusy) return;
        SetDiagnosticsBusy(true);
        try
        {
            DiagnosticsSummaryText.Text = await _diagnosticsCenter.RefreshAsync();
            _diagnosticsLoaded = true;
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusRefreshed);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            SetDiagnosticsBusy(false);
        }
    }

    private void SetDiagnosticsBusy(bool busy)
    {
        _diagnosticsBusy = busy;
        DiagnosticsRefreshButton.IsEnabled = !busy;
        DiagnosticsCopyButton.IsEnabled = !busy;
        DiagnosticsExportButton.IsEnabled = !busy;
    }

    private void ApplyStatus(string key)
    {
        StatusValue.Text = _text.Get(key);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _cleanupCenter.PropertyChanged -= OnCleanupPropertyChanged;
        _leagueWorkbench.PropertyChanged -= OnLeagueWorkbenchPropertyChanged;
        Closed -= OnClosed;
    }
}
