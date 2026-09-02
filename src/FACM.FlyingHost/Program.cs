using System.Diagnostics;
using System.Windows.Threading;

namespace FACM.FlyingHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        PetHostUiText.Configure(ReadArgument(args, "--ui-text"));

        if (args.Any(x => string.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase)))
            return FlyingHostSelfTest.Run();

        var pipeName = ReadArgument(args, "--pipe");
        var petId = ReadArgument(args, "--pet-id");
        var parentPidText = ReadArgument(args, "--parent-pid");
        _ = int.TryParse(parentPidText, out var parentPid);

        var application = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        var ipc = new PetHostIpc(pipeName);
        System.Windows.Window window = new FlyingPetHostWindow(ipc, petId);
        application.MainWindow = window;

        DispatcherTimer? parentTimer = null;
        if (parentPid > 0)
        {
            parentTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            parentTimer.Tick += (_, _) =>
            {
                if (!IsProcessAlive(parentPid)) window.Close();
            };
            parentTimer.Start();
        }

        application.Exit += (_, _) => parentTimer?.Stop();
        return application.Run();
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

internal static class FlyingHostSelfTest
{
    public static int Run()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("FlyingHost 仅支持 Windows。");
            if (!Environment.Is64BitProcess) throw new InvalidOperationException("FlyingHost 必须以 x64 运行。");
            if (PetHostIpc.Escape("a|b\\c\r\nd") != "a\\pb\\\\c\\r\\nd")
                throw new InvalidOperationException("FlyingHost IPC 转义协议自检失败。");

            var expectedFlyingIds = new[] { "greenfly", "bee", "real-bee", "dragonfly", "butterfly", "moth" };
            if (expectedFlyingIds.Any(id => !FlyingPetProfiles.Contains(id)))
                throw new InvalidOperationException("FlyingHost 配置不完整。");
            var greenfly = FlyingPetProfiles.Get("greenfly");
            if (greenfly.MinBaseSpeed != 82 || greenfly.MaxBaseSpeed != 140 || greenfly.VelocityResponse != 7.5)
                throw new InvalidOperationException("FlyingHost 3.5 行为基线发生漂移。");
            var realBee = FlyingPetProfiles.Get("real-bee");
            if (realBee.MinBaseSpeed != 48 || realBee.MaxBaseSpeed != 82)
                throw new InvalidOperationException("真实蜜蜂必须继续复用 3.5 蜜蜂轨迹基线。");
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "facm-flyinghost-self-test-error.txt"), exception.ToString());
            }
            catch
            {
            }
            return 42;
        }
    }
}
