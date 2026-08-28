using FACM.App.Personalization;
using FACM.App.ViewModels;
using FACM.Core.Personalization;
using FACM.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private PersonalizationViewModel? _personalizationCenter;
    private StackPanel? _personalizationPanel;
    private ComboBox? _themePicker;
    private TextBlock? _themeDescription;
    private TextBlock? _personalizationStatus;
    private UIElement[]? _overviewDefaultChildren;
    private bool _syncingThemeSelection;

    private void InitializePersonalizationSurface()
    {
        var themeRuntime = new WinUiThemeRuntime(Application.Current.Resources);
        _personalizationCenter = _controlCenter.CreatePersonalization(themeRuntime);
        _personalizationCenter.InitializeForStartup();

        _overviewDefaultChildren = GeneralOverviewGrid.Children.Cast<UIElement>().ToArray();
        _personalizationPanel = BuildPersonalizationPanel(_personalizationCenter);
        Grid.SetColumnSpan(_personalizationPanel, 2);
        _personalizationPanel.Visibility = Visibility.Collapsed;
        GeneralOverviewGrid.Children.Add(_personalizationPanel);
        SyncPersonalizationSurface();
    }

    private StackPanel BuildPersonalizationPanel(PersonalizationViewModel viewModel)
    {
        var panel = new StackPanel { Spacing = 18 };
        var themeCard = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var content = new StackPanel { Spacing = 10 };
        var title = new TextBlock
        {
            Text = _text.Get(UiTextKeys.ThemeSettings),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        };
        _themePicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = viewModel.ThemeOptions,
            DisplayMemberPath = nameof(FacmThemeDefinition.Name)
        };
        AutomationProperties.SetAutomationId(_themePicker, "FACM.Personalization.ThemePicker");
        AutomationProperties.SetName(_themePicker, _text.Get(UiTextKeys.ThemeSettings));
        _themePicker.SelectionChanged += OnThemeSelectionChanged;

        _themeDescription = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        _personalizationStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        var statusChip = new Border
        {
            Style = (Style)Application.Current.Resources["FacmStatusChipStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _personalizationStatus
        };

        content.Children.Add(title);
        content.Children.Add(_themePicker);
        content.Children.Add(_themeDescription);
        content.Children.Add(statusChip);
        themeCard.Child = content;
        panel.Children.Add(themeCard);
        return panel;
    }

    private void SetPersonalizationVisible(bool visible)
    {
        if (_personalizationPanel is null || _overviewDefaultChildren is null) return;
        _personalizationPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in _overviewDefaultChildren)
            child.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        if (visible) SyncPersonalizationSurface();
    }

    private void SyncPersonalizationSurface()
    {
        var viewModel = _personalizationCenter;
        if (viewModel is null || _themePicker is null || _themeDescription is null || _personalizationStatus is null) return;

        _syncingThemeSelection = true;
        try
        {
            _themePicker.SelectedItem = viewModel.ThemeOptions.FirstOrDefault(theme =>
                string.Equals(theme.Id, viewModel.SelectedTheme.Id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingThemeSelection = false;
        }

        _themeDescription.Text = viewModel.SelectedTheme.Description;
        _personalizationStatus.Text = viewModel.IsRecoveryReadOnly
            ? _text.Get(UiTextKeys.CleanupPathRecoveryReadOnly)
            : _text.Get(viewModel.Status == "failed" ? UiTextKeys.ShellStatusUnavailable : UiTextKeys.ShellStatusReady);
        _themePicker.IsEnabled = !viewModel.IsBusy;
    }

    private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingThemeSelection || _personalizationCenter is null || _themePicker?.SelectedItem is not FacmThemeDefinition selected)
            return;

        try
        {
            await _personalizationCenter.SelectThemeAsync(selected.Id);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private void DisposePersonalizationSurface()
    {
        if (_themePicker is not null) _themePicker.SelectionChanged -= OnThemeSelectionChanged;
        _themePicker = null;
        _themeDescription = null;
        _personalizationStatus = null;
        _personalizationPanel = null;
        _personalizationCenter = null;
        _overviewDefaultChildren = null;
    }
}
