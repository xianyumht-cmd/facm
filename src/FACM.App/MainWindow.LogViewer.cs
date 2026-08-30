using FACM.Core.Observability;
using FACM.Core.Desktop;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace FACM.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<DiagnosticEvent> _logEvents = Array.Empty<DiagnosticEvent>();
    private bool _syncingLogFilters;
    private bool _logsSubviewVisible;

    internal void OpenStructuredLogSurface()
    {
        if (_closed) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(OpenStructuredLogSurface);
            return;
        }
        if (_maintenanceViewModel?.ForceUpdateRequired == true) return;

        NavigateToSection("settings");
        SetLogsSubviewVisible(true);
        if (_morphingSurfaceEnabled)
            ShowMorphingSurface(FacmSurfaceMode.FeatureSurface, "structured-log-opened", true);
        _ = RefreshStructuredLogsAsync();
    }

    private void OnDiagnosticsViewClick(object sender, RoutedEventArgs args) => SetLogsSubviewVisible(false);

    private void OnLogsViewClick(object sender, RoutedEventArgs args) => OpenStructuredLogSurface();

    private void SetLogsSubviewVisible(bool visible)
    {
        _logsSubviewVisible = visible;
        DiagnosticsSummarySurface.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        LogViewerSurface.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsViewButton.IsEnabled = visible;
        LogsViewButton.IsEnabled = !visible;
        if (_maintenanceControl is not null)
            _maintenanceControl.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        if (_morphingSurfaceEnabled &&
            _surfaceStateMachine.Mode == FacmSurfaceMode.FeatureSurface &&
            string.Equals(_activeSection, "settings", StringComparison.Ordinal))
            EnsureCurrentSurfacePresentation(visible ? "logs-subview" : "diagnostics-subview");
    }

    private async void OnLogRefreshClick(object sender, RoutedEventArgs args) =>
        await RefreshStructuredLogsAsync();

    private async Task RefreshStructuredLogsAsync()
    {
        if (_closed || _diagnosticsBusy) return;
        _diagnosticsBusy = true;
        LogRefreshButton.IsEnabled = false;
        try
        {
            _logEvents = await _diagnosticsCenter.RefreshEventsAsync();
            PopulateLogFilters();
            RenderStructuredLogs();
            LogViewerStatus.Text = _logEvents.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) +
                                   " · " + _text.Get(UiTextKeys.LogsRefreshed);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            LogViewerStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            _diagnosticsBusy = false;
            LogRefreshButton.IsEnabled = true;
        }
    }

    private void PopulateLogFilters()
    {
        var previousDomain = LogDomainFilter.SelectedItem as string;
        var previousOutcome = LogOutcomeFilter.SelectedItem as string;
        _syncingLogFilters = true;
        try
        {
            LogDomainFilter.Items.Clear();
            LogDomainFilter.Items.Add(_text.Get(UiTextKeys.LogsAllDomains));
            foreach (var domain in _logEvents.Select(item => item.Module)
                         .Where(item => !string.IsNullOrWhiteSpace(item))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                LogDomainFilter.Items.Add(domain);
            LogDomainFilter.SelectedItem = previousDomain is not null && LogDomainFilter.Items.Contains(previousDomain)
                ? previousDomain
                : LogDomainFilter.Items[0];

            LogOutcomeFilter.Items.Clear();
            LogOutcomeFilter.Items.Add(_text.Get(UiTextKeys.LogsAllOutcomes));
            foreach (var outcome in Enum.GetNames<DiagnosticResult>()) LogOutcomeFilter.Items.Add(outcome);
            LogOutcomeFilter.SelectedItem = previousOutcome is not null && LogOutcomeFilter.Items.Contains(previousOutcome)
                ? previousOutcome
                : LogOutcomeFilter.Items[0];
        }
        finally
        {
            _syncingLogFilters = false;
        }
    }

    private void OnLogFilterChanged(object sender, RoutedEventArgs args)
    {
        if (_syncingLogFilters || !_logsSubviewVisible) return;
        RenderStructuredLogs();
    }

    private void RenderStructuredLogs()
    {
        var search = LogSearchBox.Text?.Trim() ?? string.Empty;
        var allDomains = _text.Get(UiTextKeys.LogsAllDomains);
        var selectedDomain = LogDomainFilter.SelectedItem as string ?? allDomains;
        var allOutcomes = _text.Get(UiTextKeys.LogsAllOutcomes);
        var selectedOutcome = LogOutcomeFilter.SelectedItem as string ?? allOutcomes;
        var filtered = _logEvents
            .Where(item => string.Equals(selectedDomain, allDomains, StringComparison.Ordinal) ||
                           string.Equals(item.Module, selectedDomain, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(selectedOutcome, allOutcomes, StringComparison.Ordinal) ||
                           string.Equals(item.Result.ToString(), selectedOutcome, StringComparison.OrdinalIgnoreCase))
            .Where(item => search.Length == 0 || MatchesLogSearch(item, search))
            .OrderByDescending(item => item.TimestampUtc)
            .Take(120)
            .ToArray();

        LogRowsPanel.Children.Clear();
        foreach (var item in filtered) LogRowsPanel.Children.Add(CreateLogRow(item));
        if (filtered.Length == 0) LogViewerStatus.Text = _text.Get(UiTextKeys.LogsNoEvents);
    }

    private static bool MatchesLogSearch(DiagnosticEvent item, string search) =>
        item.Module.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.ActionId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Reason.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        item.Data.Any(pair => pair.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                              pair.Value.Contains(search, StringComparison.OrdinalIgnoreCase));

    private UIElement CreateLogRow(DiagnosticEvent item)
    {
        var grid = new Grid
        {
            ColumnSpacing = 6,
            Padding = new Thickness(6, 4, 6, 4)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        AddLogCell(grid, item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"), 0);
        AddLogCell(grid, item.Module, 1);
        AddLogCell(grid, item.ActionId, 2);
        AddLogCell(grid, item.Result.ToString(), 3);
        AddLogCell(grid, item.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms", 4);
        ToolTipService.SetToolTip(grid, item.Reason);
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["FacmStrokeBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static void AddLogCell(Grid grid, string text, int column)
    {
        var cell = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private async void OnLogOpenFolderClick(object sender, RoutedEventArgs args)
    {
        var directory = _diagnosticsCenter.LogDirectory;
        if (directory.Length == 0) return;
        try
        {
            _ = await Windows.System.Launcher.LaunchFolderPathAsync(directory);
        }
        catch
        {
            LogViewerStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
    }

    private void OnLogCopyPathClick(object sender, RoutedEventArgs args)
    {
        var path = _diagnosticsCenter.LogPath;
        if (path.Length == 0) return;
        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            LogViewerStatus.Text = _text.Get(UiTextKeys.LogsPathCopied);
        }
        catch
        {
            LogViewerStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
    }
}
