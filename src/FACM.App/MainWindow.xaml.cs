using FACM.App.ViewModels;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private readonly ControlCenterViewModel _controlCenter;
    private readonly IUiTextProvider _text;

    public MainWindow(ControlCenterViewModel controlCenter, IUiTextProvider text)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        InitializeComponent();
        ApplyStaticText();
        RootNavigation.SelectedItem = RepairNav;
        RootNavigation.Loaded += OnRootNavigationLoaded;
    }

    private void ApplyStaticText()
    {
        var appName = _text.Get(UiTextKeys.AppName);
        Title = appName + " 4.0";
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
        ApplySection("repair");
        ApplyStatus(UiTextKeys.ShellStatusReady);
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
        var (titleKey, subtitleKey) = tag switch
        {
            "league" => (UiTextKeys.ShellLeague, UiTextKeys.ShellLeagueSubtitle),
            "personalization" => (UiTextKeys.ShellPersonalization, UiTextKeys.ShellPersonalizationSubtitle),
            "settings" => (UiTextKeys.ShellMoreSettings, UiTextKeys.ShellMoreSettingsSubtitle),
            _ => (UiTextKeys.ShellRepairTools, UiTextKeys.ShellRepairSubtitle)
        };
        SectionTitle.Text = _text.Get(titleKey);
        SectionSubtitle.Text = _text.Get(subtitleKey);
    }

    private void ApplyStatus(string key)
    {
        StatusValue.Text = _text.Get(key);
    }
}
