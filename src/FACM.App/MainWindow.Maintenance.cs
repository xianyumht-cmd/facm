using FACM.App.ViewModels;
using Microsoft.UI.Xaml;

namespace FACM.App;

public sealed partial class MainWindow
{
    private MaintenanceSettingsControl? _maintenanceControl;
    private MaintenanceViewModel? _maintenanceViewModel;
    private Action? _maintenanceShutdownRequested;
    private bool _maintenanceCloseHooked;

    public void ConfigureMaintenance(MaintenanceViewModel viewModel, Action requestApplicationShutdown)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(requestApplicationShutdown);
        if (_closed) throw new InvalidOperationException("Main window is closed.");

        _maintenanceViewModel = viewModel;
        _maintenanceShutdownRequested = requestApplicationShutdown;
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

        if (!_maintenanceCloseHooked)
        {
            _maintenanceCloseHooked = true;
            Closed += OnMaintenanceWindowClosed;
        }
    }

    private void OnMaintenanceReplacementStarted() => _maintenanceShutdownRequested?.Invoke();

    private void OnMaintenanceExitRequested() => _maintenanceShutdownRequested?.Invoke();

    private void OnMaintenanceWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_maintenanceCloseHooked) return;
        _maintenanceCloseHooked = false;
        Closed -= OnMaintenanceWindowClosed;
        if (_maintenanceControl is not null)
        {
            _maintenanceControl.ReplacementStarted -= OnMaintenanceReplacementStarted;
            _maintenanceControl.ExitRequested -= OnMaintenanceExitRequested;
            _maintenanceControl.Detach();
            _maintenanceControl = null;
        }
        _maintenanceViewModel = null;
        _maintenanceShutdownRequested = null;
    }
}
