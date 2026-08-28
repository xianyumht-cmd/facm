using System.Diagnostics;
using System.Windows.Threading;
using VPet_Simulator.Core;

namespace FACM.PetHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        PetHostPaths.ConfigureDataRoot(ReadArgument(args, "--data-root"));
        PetHostUiText.Configure(ReadArgument(args, "--ui-text"));

        if (args.Any(x => string.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase)))
            return PetHostSelfTest.Run();

        var runtime = ReadArgument(args, "--runtime");
        var flying = string.Equals(runtime, "flying", StringComparison.OrdinalIgnoreCase);
        if (!flying)
            VPetAssetCacheValidator.InvalidateBrokenCompletionMarkers();

        var pipeName = ReadArgument(args, "--pipe");
        var petId = ReadArgument(args, "--pet-id");
        var parentPidText = ReadArgument(args, "--parent-pid");
        _ = int.TryParse(parentPidText, out var parentPid);

        var application = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        var ipc = new PetHostIpc(pipeName);
        System.Windows.Window window = flying
            ? new FlyingPetHostWindow(ipc, petId)
            : new PetHostWindow(ipc);
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
        window.Show();
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

internal static class PetHostSelfTest
{
    public static int Run()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("PetHost 仅支持 Windows。");
            if (!Environment.Is64BitProcess) throw new InvalidOperationException("PetHost 必须以 x64 运行。");
            if (!string.Equals(typeof(GameCore).Assembly.GetName().Name, "VPet-Simulator.Core", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("VPet-Simulator.Core 未正确加载。");
            if (!typeof(IController).IsAssignableFrom(typeof(PetWindowController)))
                throw new InvalidOperationException("PetWindowController 未实现 VPet IController。");
            if (PetHostPaths.UpstreamCommit.Length != 40)
                throw new InvalidOperationException("VPet 上游固定提交格式无效。");
            if (PetHostIpc.Escape("a|b\\c\r\nd") != "a\\pb\\\\c\\r\\nd")
                throw new InvalidOperationException("PetHost IPC 转义协议自检失败。");

            var expectedFlyingIds = new[] { "greenfly", "bee", "real-bee", "dragonfly", "butterfly", "moth" };
            if (expectedFlyingIds.Any(id => !FlyingPetProfiles.Contains(id)))
                throw new InvalidOperationException("Flying PetHost 配置不完整。");
            var greenfly = FlyingPetProfiles.Get("greenfly");
            if (greenfly.MinBaseSpeed != 82 || greenfly.MaxBaseSpeed != 140 || greenfly.VelocityResponse != 7.5)
                throw new InvalidOperationException("Flying PetHost 3.5 行为基线发生漂移。");
            var realBee = FlyingPetProfiles.Get("real-bee");
            if (realBee.MinBaseSpeed != 48 || realBee.MaxBaseSpeed != 82)
                throw new InvalidOperationException("真实蜜蜂必须继续复用 3.5 蜜蜂轨迹基线。");

            Directory.CreateDirectory(PetHostPaths.RootDirectory);
            Directory.CreateDirectory(PetHostPaths.CacheDirectory);
            var root = Path.GetFullPath(PetHostPaths.RootDirectory);
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("PetHost 数据目录无效。");
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                Directory.CreateDirectory(PetHostPaths.RootDirectory);
                File.WriteAllText(Path.Combine(PetHostPaths.RootDirectory, "self-test-error.txt"), exception.ToString());
            }
            catch { }
            return 41;
        }
    }
}
