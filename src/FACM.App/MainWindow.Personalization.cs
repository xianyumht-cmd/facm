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
    private Button? _petEnableButton;
    private Button? _restoreLauncherButton;
    private Button? _resetDesktopPositionButton;
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
        _personalizationCenter.PropertyChanged += OnPersonalizationViewModelPropertyChanged;
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

        _petEnableButton = new Button
        {
            Content = "启用当前桌宠",
            Style = (Style)Application.Current.Resources["FacmPrimaryButtonStyle"]
        };
        AutomationProperties.SetAutomationId(_petEnableButton, "FACM.Personalization.EnablePet");
        AutomationProperties.SetName(_petEnableButton, "启用当前桌宠");
        _petEnableButton.Click += OnEnablePetClicked;

        _restoreLauncherButton = new Button
        {
            Content = "恢复默认 F"
        };
        AutomationProperties.SetAutomationId(_restoreLauncherButton, "FACM.Personalization.RestoreLauncher");
        AutomationProperties.SetName(_restoreLauncherButton, "恢复默认 F");
        _restoreLauncherButton.Click += OnRestoreLauncherClicked;

        _resetDesktopPositionButton = new Button
        {
            Content = "复位桌面位置"
        };
        AutomationProperties.SetAutomationId(_resetDesktopPositionButton, "FACM.Personalization.ResetDesktopPosition");
        AutomationProperties.SetName(_resetDesktopPositionButton, "复位桌面位置");
        _resetDesktopPositionButton.Click += OnResetDesktopPositionClicked;

        var petActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        petActions.Children.Add(_petEnableButton);
        petActions.Children.Add(_restoreLauncherButton);
        petActions.Children.Add(_resetDesktopPositionButton);

        petContent.Children.Add(petTitle);
        petContent.Children.Add(_petPicker);
        petContent.Children.Add(_petDescription);
        petContent.Children.Add(petActions);
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
                     viewModel.Status.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                     viewModel.Status.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        _personalizationStatus.Text = viewModel.IsRecoveryReadOnly
            ? _text.Get(UiTextKeys.CleanupPathRecoveryReadOnly)
            : _text.Get(failed ? UiTextKeys.ShellStatusUnavailable : UiTextKeys.ShellStatusReady);
        _themePicker.IsEnabled = !viewModel.IsBusy;
        _petPicker.IsEnabled = !viewModel.IsBusy;
        if (_petEnableButton is not null)
        {
            _petEnableButton.IsEnabled = !viewModel.IsBusy && viewModel.CanControlDesktopPet;
            _petEnableButton.Content = viewModel.IsPetEnabled ? "重新应用当前桌宠" : "启用当前桌宠";
        }
        if (_restoreLauncherButton is not null)
            _restoreLauncherButton.IsEnabled = !viewModel.IsBusy && viewModel.CanControlDesktopPet;
        if (_resetDesktopPositionButton is not null)
            _resetDesktopPositionButton.IsEnabled = !viewModel.IsBusy && viewModel.CanControlDesktopPet;
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
        catch
        {
            // ViewModel is fail-soft for persistence/runtime failures; this is the final async-void guard.
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private async void OnPetSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var viewModel = _personalizationCenter;
        if (_syncingPetSelection || viewModel is null || _petPicker?.SelectedItem is not FacmPetDefinition selected)
            return;
        if (viewModel.IsBusy)
        {
            TracePersonalizationPetAction("pet-select-busy-rejected", false, selected.Id, viewModel.Status);
            SyncPersonalizationSurface();
            return;
        }

        TracePersonalizationPetAction("pet-select-start", true, selected.Id, viewModel.Status);
        try
        {
            var success = await viewModel.SelectPetAsync(selected.Id);
            TracePersonalizationPetAction(success ? "pet-select-finish" : "pet-select-failed", success, selected.Id, viewModel.Status);
        }
        catch (OperationCanceledException)
        {
            TracePersonalizationPetAction("pet-select-cancelled", false, selected.Id, viewModel.Status);
        }
        catch (Exception exception)
        {
            TracePersonalizationPetAction("pet-select-exception", false, selected.Id, exception.GetType().Name);
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private async void OnEnablePetClicked(object sender, RoutedEventArgs e)
    {
        var viewModel = _personalizationCenter;
        if (viewModel is null) return;
        var petId = viewModel.SelectedPet.Id;
        if (viewModel.IsBusy)
        {
            TracePersonalizationPetAction("pet-enable-busy-rejected", false, petId, viewModel.Status);
            SyncPersonalizationSurface();
            return;
        }

        TracePersonalizationPetAction("pet-enable-start", true, petId, viewModel.Status);
        try
        {
            var success = await viewModel.EnableSelectedPetAsync();
            TracePersonalizationPetAction(success ? "pet-enable-finish" : "pet-enable-failed", success, petId, viewModel.Status);
        }
        catch (OperationCanceledException)
        {
            TracePersonalizationPetAction("pet-enable-cancelled", false, petId, viewModel.Status);
        }
        catch (Exception exception)
        {
            TracePersonalizationPetAction("pet-enable-exception", false, petId, exception.GetType().Name);
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private async void OnRestoreLauncherClicked(object sender, RoutedEventArgs e)
    {
        var viewModel = _personalizationCenter;
        if (viewModel is null) return;
        var petId = viewModel.SelectedPet.Id;
        if (viewModel.IsBusy)
        {
            TracePersonalizationPetAction("pet-restore-busy-rejected", false, petId, viewModel.Status);
            return;
        }

        TracePersonalizationPetAction("pet-restore-start", true, petId, viewModel.Status);
        try
        {
            await viewModel.RestoreDefaultLauncherAsync();
            TracePersonalizationPetAction("pet-restore-finish", true, petId, viewModel.Status);
        }
        catch (OperationCanceledException)
        {
            TracePersonalizationPetAction("pet-restore-cancelled", false, petId, viewModel.Status);
        }
        catch (Exception exception)
        {
            TracePersonalizationPetAction("pet-restore-exception", false, petId, exception.GetType().Name);
        }
        finally
        {
            SyncPersonalizationSurface();
        }
    }

    private async void OnResetDesktopPositionClicked(object sender, RoutedEventArgs e)
    {
        var viewModel = _personalizationCenter;
        if (viewModel is null) return;
        var petId = viewModel.SelectedPet.Id;
        if (viewModel.IsBusy)
        {
            TracePersonalizationPetAction("pet-reset-position-busy-rejected", false, petId, viewModel.Status);
            return;
        }

        TracePersonalizationPetAction("pet-reset-position-start", true, petId, viewModel.Status);
        try
        {
            await viewModel.ResetDesktopPositionAsync();
            TracePersonalizationPetAction("pet-reset-position-finish", true, petId, viewModel.Status);
        }
        catch (OperationCanceledException)
        {
            TracePersonalizationPetAction("pet-reset-position-cancelled", false, petId, viewModel.Status);
        }
        catch (Exception exception)
        {
            TracePersonalizationPetAction("pet-reset-position-exception", false, petId, exception.GetType().Name);
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
        if (_personalizationCenter is not null)
            _personalizationCenter.PropertyChanged -= OnPersonalizationViewModelPropertyChanged;
        if (_themePicker is not null) _themePicker.SelectionChanged -= OnThemeSelectionChanged;
        if (_petPicker is not null) _petPicker.SelectionChanged -= OnPetSelectionChanged;
        if (_petEnableButton is not null) _petEnableButton.Click -= OnEnablePetClicked;
        if (_restoreLauncherButton is not null) _restoreLauncherButton.Click -= OnRestoreLauncherClicked;
        if (_resetDesktopPositionButton is not null) _resetDesktopPositionButton.Click -= OnResetDesktopPositionClicked;
        _themePicker = null;
        _themeDescription = null;
        _petPicker = null;
        _petDescription = null;
        _personalizationStatus = null;
        _petEnableButton = null;
        _restoreLauncherButton = null;
        _resetDesktopPositionButton = null;
        _personalizationPanel = null;
        _personalizationCenter = null;
        _overviewDefaultChildren = null;
    }
}
