using System.ComponentModel;
using FACM.App.ViewModels;
using FACM.Core.League;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private readonly ControlCenterViewModel _controlCenter;
    private readonly LeagueWorkbenchViewModel _leagueWorkbench;
    private readonly IUiTextProvider _text;
    private bool _closed;

    public MainWindow(
        ControlCenterViewModel controlCenter,
        LeagueWorkbenchViewModel leagueWorkbench,
        IUiTextProvider text)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _leagueWorkbench = leagueWorkbench ?? throw new ArgumentNullException(nameof(leagueWorkbench));
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
        Title = appName + " 4.0";
        TitleBarText.Text = appName;
        ProductTitle.Text = appName + " 4.0";
        RepairNav.Content = _text.Get(UiTextKeys.ShellRepairTools);
        LeagueNav.Content = _text.Get(UiTextKeys.ShellLeague);
        PersonalizationNav.Content = _text.Get(UiTextKeys.ShellPersonalization);
        SettingsNav.Content = _text.Get(UiTextKeys.ShellMoreSettings);
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
        if (isLeague) ApplyLeagueRuntimeState();
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
