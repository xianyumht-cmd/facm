using System.ComponentModel;
using FACM.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FACM.App;

public sealed partial class MainWindow
{
    private MaintenanceSettingsControl? _maintenanceControl;
    private MaintenanceViewModel? _maintenanceViewModel;
    private Action? _maintenanceShutdownRequested;
    private readonly Dictionary<UIElement, Visibility> _maintenanceDiagnosticVisibility = new();
    private bool _maintenanceCloseHooked;
    private bool _maintenanceNavigationHooked;
    private bool _maintenanceForceLockApplied;
    private bool _maintenanceRedirecting;

    public void ConfigureMaintenance(MaintenanceViewModel viewModel, Action requestApplicationShutdown)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(requestApplicationShutdown);
        if (_closed) throw new InvalidOperationException("Main window is closed.");

        if (_maintenanceViewModel is not null && !ReferenceEquals(_maintenanceViewModel, viewModel))
            _maintenanceViewModel.PropertyChanged -= OnMaintenanceViewModelPropertyChanged;

        _maintenanceViewModel = viewModel;
        _maintenanceShutdownRequested = requestApplicationShutdown;
        _maintenanceViewModel.PropertyChanged -= OnMaintenanceViewModelPropertyChanged;
        _maintenanceViewModel.PropertyChanged += OnMaintenanceViewModelPropertyChanged;

        if (_maintenanceControl is null)
        {
            var control = new MaintenanceSettingsControl();
            control.ReplacementStarted += OnMaintenanceReplacementStarted;
            control.ExitRequested += OnMaintenanceExitRequested;
            control.Configure(viewModel);
            _maintenanceControl = control;
            DiagnosticsPanel.Children.Insert(0, control);
        }
        else
        {
            _maintenanceControl.Configure(viewModel);
        }

        if (!_maintenanceNavigationHooked)
        {
            _maintenanceNavigationHooked = true;
            RootNavigation.SelectionChanged += OnMaintenanceNavigationSelectionChanged;
        }

        if (!_maintenanceCloseHooked)
        {
            _maintenanceCloseHooked = true;
            Closed += OnMaintenanceWindowClosed;
        }

        ApplyMaintenanceForceLock();
    }

    private void OnMaintenanceReplacementStarted() => _maintenanceShutdownRequested?.Invoke();

    private void OnMaintenanceExitRequested() => _maintenanceShutdownRequested?.Invoke();

    private void OnMaintenanceViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.PropertyName) &&
            !string.Equals(args.PropertyName, nameof(MaintenanceViewModel.ForceUpdateRequired), StringComparison.Ordinal))
            return;

        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyMaintenanceForceLock();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyMaintenanceForceLock);
    }

    private void OnMaintenanceNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_maintenanceRedirecting || _maintenanceViewModel?.ForceUpdateRequired != true) return;
        if (ReferenceEquals(args.SelectedItem, SettingsNav)) return;

        _maintenanceRedirecting = true;
        try
        {
            RootNavigation.SelectedItem = SettingsNav;
        }
        finally
        {
            _maintenanceRedirecting = false;
        }
    }

    private void ApplyMaintenanceForceLock()
    {
        var force = _maintenanceViewModel?.ForceUpdateRequired == true;
        RepairNav.IsEnabled = !force;
        LeagueNav.IsEnabled = !force;
        PersonalizationNav.IsEnabled = !force;
        SettingsNav.IsEnabled = true;

        if (force)
        {
            if (!_maintenanceForceLockApplied)
            {
                _maintenanceDiagnosticVisibility.Clear();
                foreach (var child in DiagnosticsPanel.Children)
                {
                    if (ReferenceEquals(child, _maintenanceControl)) continue;
                    _maintenanceDiagnosticVisibility[child] = child.Visibility;
                    child.Visibility = Visibility.Collapsed;
                }
                _maintenanceForceLockApplied = true;
            }

            if (!ReferenceEquals(RootNavigation.SelectedItem, SettingsNav))
            {
                _maintenanceRedirecting = true;
                try
                {
                    RootNavigation.SelectedItem = SettingsNav;
                }
                finally
                {
                    _maintenanceRedirecting = false;
                }
            }
            return;
        }

        if (!_maintenanceForceLockApplied) return;
        foreach (var pair in _maintenanceDiagnosticVisibility)
            pair.Key.Visibility = pair.Value;
        _maintenanceDiagnosticVisibility.Clear();
        _maintenanceForceLockApplied = false;
    }

    private void OnMaintenanceWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_maintenanceCloseHooked) return;
        _maintenanceCloseHooked = false;
        Closed -= OnMaintenanceWindowClosed;

        if (_maintenanceNavigationHooked)
        {
            _maintenanceNavigationHooked = false;
            RootNavigation.SelectionChanged -= OnMaintenanceNavigationSelectionChanged;
        }

        if (_maintenanceViewModel is not null)
            _maintenanceViewModel.PropertyChanged -= OnMaintenanceViewModelPropertyChanged;

        if (_maintenanceControl is not null)
        {
            _maintenanceControl.ReplacementStarted -= OnMaintenanceReplacementStarted;
            _maintenanceControl.ExitRequested -= OnMaintenanceExitRequested;
            _maintenanceControl.Detach();
            _maintenanceControl = null;
        }

        _maintenanceDiagnosticVisibility.Clear();
        _maintenanceForceLockApplied = false;
        _maintenanceViewModel = null;
        _maintenanceShutdownRequested = null;
    }
}
