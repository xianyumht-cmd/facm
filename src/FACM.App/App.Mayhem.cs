using FACM.App.ViewModels;
using FACM.Core.Runtime;
using FACM.Infrastructure.Mayhem;
using FACM.Platform.Windows.Runtime;

namespace FACM.App;

public partial class App
{
    private MayhemProductQueryService? _mayhemQueryService;
    private bool _mayhemProcessExitHooked;

    internal MayhemViewModel CreateMayhemViewModel()
    {
        if (_mayhemQueryService is null)
        {
            var gateway = _leagueGateway
                ?? throw new InvalidOperationException("League read gateway is unavailable.");
            var layout = RuntimePathLayout.From(new WindowsExecutablePathProvider());
            _mayhemQueryService = new MayhemProductQueryService(layout.CacheDirectory, gateway);

            if (!_mayhemProcessExitHooked)
            {
                _mayhemProcessExitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += OnMayhemProcessExit;
            }
        }

        return new MayhemViewModel(_mayhemQueryService);
    }

    private void OnMayhemProcessExit(object? sender, EventArgs args)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnMayhemProcessExit;
        _mayhemProcessExitHooked = false;
        _mayhemQueryService?.Dispose();
        _mayhemQueryService = null;
    }
}
