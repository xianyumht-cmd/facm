using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FACM.MachineCatPrototype;

internal static class WindowSmokeTest
{
    public static int Run()
    {
        if (!OperatingSystem.IsWindows()) return 52;

        var loaded = false;
        var renderFrames = 0;
        Exception? failure = null;
        DispatcherTimer? timeout = null;
        MachineCatWindow? window = null;

        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            window = new MachineCatWindow(PetState.Walk)
            {
                ShowActivated = false
            };

            window.Loaded += (_, _) => loaded = true;

            void OnRendering(object? sender, EventArgs args)
            {
                if (!loaded) return;
                renderFrames++;
                if (renderFrames < 3) return;

                CompositionTarget.Rendering -= OnRendering;
                timeout?.Stop();
                window.Close();
                app.Shutdown();
            }

            CompositionTarget.Rendering += OnRendering;

            timeout = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                CompositionTarget.Rendering -= OnRendering;
                failure = new TimeoutException($"WPF window smoke timed out: loaded={loaded}, renderFrames={renderFrames}.");
                window.Close();
                app.Shutdown();
            };

            timeout.Start();
            window.Show();
            app.Run();

            if (failure is not null) throw failure;
            if (!loaded) throw new InvalidOperationException("MachineCatWindow did not raise Loaded.");
            if (renderFrames < 3) throw new InvalidOperationException($"Expected at least 3 rendering frames, got {renderFrames}.");
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-cat-window-smoke-error.txt"), exception.ToString());
            }
            catch
            {
                // Best-effort diagnostic only.
            }

            return 53;
        }
        finally
        {
            timeout?.Stop();
            if (window is { IsVisible: true }) window.Close();
        }
    }
}
