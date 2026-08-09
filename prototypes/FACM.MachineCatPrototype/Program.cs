using System.Windows;

namespace FACM.MachineCatPrototype;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
            return MachineCatSelfTest.Run();

        if (!OperatingSystem.IsWindows())
            return 52;

        var initialState = ReadState(args) ?? PetState.Idle;
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        var window = new MachineCatWindow(initialState);
        app.MainWindow = window;
        window.Show();
        return app.Run();
    }

    private static PetState? ReadState(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--state", StringComparison.OrdinalIgnoreCase)) continue;
            if (Enum.TryParse<PetState>(args[i + 1], ignoreCase: true, out var state)) return state;
        }
        return null;
    }
}
