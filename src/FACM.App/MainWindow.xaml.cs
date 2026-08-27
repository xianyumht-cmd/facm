using System.ComponentModel;
using FACM.App.ViewModels;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private readonly ControlCenterViewModel _controlCenter;
    private readonly LeagueWorkbenchViewModel _leagueWorkbench;
    private readonly DiagnosticsCenterViewModel _diagnosticsCenter;
    private readonly IUiTextProvider _text;
    private bool _closed;
    private bool _diagnosticsLoaded;
    private bool _diagnosticsBusy;

    public MainWindow(
        ControlCenterViewModel controlCenter,
        LeagueWorkbenchViewModel leagueWorkbench,
        DiagnosticsCenterViewModel diagnosticsCenter,
        IUiTextProvider text)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _leagueWorkbench = leagueWorkbench ?? throw new ArgumentNullException(nameof(leagueWorkbench));
        _diagnosticsCenter = diagnosticsCenter ?? throw new ArgumentNullException(nameof(diagnosticsCenter));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyStaticText();
        ApplyLeagueRuntimeState();
        _leagueWorkbench.PropertyChanged += OnLeagueWorkbenchPropertyChanged;
        Closed += OnClosed;
        RootNavigation.SelectedItem = RepairNav;
        RootNavigation.Loaded += OnRootNavigationLoaded;
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
        GeneralOverviewGrid.Visibility = isLeague ? Visibility.Collapsed : Visibility.Visible;
        LeagueWorkbenchPanel.Visibility = isLeague ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        if (isLeague) ApplyLeagueRuntimeState();
        if (isSettings && !_diagnosticsLoaded) _ = RefreshDiagnosticsAsync();
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
        _leagueWorkbench.PropertyChanged -= OnLeagueWorkbenchPropertyChanged;
        Closed -= OnClosed;
    }
}
