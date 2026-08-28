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
    private ComboBox? _petPicker;
    private TextBlock? _petDescription;
    private TextBlock? _personalizationStatus;
    private UIElement[]? _overviewDefaultChildren;
    private bool _syncingThemeSelection;
    private bool _syncingPetSelection;

    private void InitializePersonalizationSurface()
    {
        var app = Application.Current as App ?? throw new InvalidOperationException("FACM App composition root is unavailable.");
        var viewModel = app.CreatePersonalizationViewModel(_controlCenter);
        ConfigurePersonalization(viewModel);
        _ = app.InitializeDesktopPetAfterLauncherReadyAsync(viewModel);
    }

    internal void ConfigurePersonalization(PersonalizationViewModel viewModel)
    {
        if (_personalizationCenter is not null) return;
        _personalizationCenter = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _overviewDefaultChildren = GeneralOverviewGrid.Children.Cast<UIElement>().ToArray();
        _personalizationPanel = BuildPersonalizationPanel(viewModel);
        Grid.SetColumnSpan(_personalizationPanel, 2);
        _personalizationPanel.Visibility = Visibility.Collapsed;
        GeneralOverviewGrid.Children.Add(_personalizationPanel);
        RootNavigation.SelectionChanged += OnPersonalizationNavigationChanged;
        Closed += OnPersonalizationClosed;
        SetPersonalizationVisible(IsPersonalizationSelected());
        SyncPersonalizationSurface();
    }

    private StackPanel BuildPersonalizationPanel(PersonalizationViewModel viewModel)
    {
        var panel = new StackPanel { Spacing = 18 };
        var themeCard = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var themeContent = new StackPanel { Spacing = 10 };
        var themeTitle = new TextBlock
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

        themeContent.Children.Add(themeTitle);
        themeContent.Children.Add(_themePicker);
        themeContent.Children.Add(_themeDescription);
        themeContent.Children.Add(statusChip);
        themeCard.Child = themeContent;
        panel.Children.Add(themeCard);

        var petCard = new Border
        {
            Style = (Style)Application.Current.Resources["FacmCardBorderStyle"]
        };
        var petContent = new StackPanel { Spacing = 10 };
        var petTitle = new TextBlock
        {
            Text = _text.Get(UiTextKeys.DesktopPet),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmCardTitleTextStyle"]
        };
        _petPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = viewModel.PetOptions,
            DisplayMemberPath = nameof(FacmPetDefinition.Name)
        };
        AutomationProperties.SetAutomationId(_petPicker, "FACM.Personalization.PetPicker");
        AutomationProperties.SetName(_petPicker, _text.Get(UiTextKeys.DesktopPet));
        _petPicker.SelectionChanged += OnPetSelectionChanged;
        _petDescription = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        };
        petContent.Children.Add(petTitle);
        petContent.Children.Add(_petPicker);
        petContent.Children.Add(_petDescription);
        petCard.Child = petContent;
        panel.Children.Add(petCard);
        return panel;
    }

    private void OnPersonalizationNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selected = args.SelectedItemContainer as NavigationViewItem;
        SetPersonalizationVisible(string.Equals(selected?.Tag?.ToString(), "personalization", StringComparison.Ordinal));
    }

    private bool IsPersonalizationSelected() =>
        RootNavigation.SelectedItem is NavigationViewItem item &&
        string.Equals(item.Tag?.ToString(), "personalization", StringComparison.Ordinal);

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
        if (viewModel is null ||
            _themePicker is null ||
            _themeDescription is null ||
            _petPicker is null ||
            _petDescription is null ||
            _personalizationStatus is null)
        {
            return;
        }

        _syncingThemeSelection = true;
        _syncingPetSelection = true;
        try
        {
            _themePicker.SelectedItem = viewModel.ThemeOptions.FirstOrDefault(theme =>
                string.Equals(theme.Id, viewModel.SelectedTheme.Id, StringComparison.OrdinalIgnoreCase));
            _petPicker.SelectedItem = viewModel.PetOptions.FirstOrDefault(pet =>
                string.Equals(pet.Id, viewModel.SelectedPet.Id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingThemeSelection = false;
            _syncingPetSelection = false;
        }

        _themeDescription.Text = viewModel.SelectedTheme.Description;
        _petDescription.Text = viewModel.SelectedPet.Description;
        var failed = viewModel.Status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                     viewModel.Status.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
        _personalizationStatus.Text = viewModel.IsRecoveryReadOnly
            ? _text.Get(UiTextKeys.CleanupPathRecoveryReadOnly)
            : _text.Get(failed ? UiTextKeys.ShellStatusUnavailable : UiTextKeys.ShellStatusReady);
        _themePicker.IsEnabled = !viewModel.IsBusy;
        _petPicker.IsEnabled = !viewModel.IsBusy;
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

    private async void OnPetSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingPetSelection || _personalizationCenter is null || _petPicker?.SelectedItem is not FacmPetDefinition selected)
            return;

        try
        {
            await _personalizationCenter.SelectPetAsync(selected.Id);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private void OnPersonalizationClosed(object sender, WindowEventArgs args)
    {
        RootNavigation.SelectionChanged -= OnPersonalizationNavigationChanged;
        Closed -= OnPersonalizationClosed;
        if (_themePicker is not null) _themePicker.SelectionChanged -= OnThemeSelectionChanged;
        if (_petPicker is not null) _petPicker.SelectionChanged -= OnPetSelectionChanged;
        _themePicker = null;
        _themeDescription = null;
        _petPicker = null;
        _petDescription = null;
        _personalizationStatus = null;
        _personalizationPanel = null;
        _personalizationCenter = null;
        _overviewDefaultChildren = null;
    }
}
