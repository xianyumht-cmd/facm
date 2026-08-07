using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FACM.Mayhem;
using FACM.Pets;
using FACM.Services;

namespace FACM
{
    internal static class Program
    {
        private const string MutexName = @"Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D";

        [STAThread]
        private static void Main(string[] args)
        {
            var startCleanup = HasArgument(args, "--cleanup");
            var petCatalogTest = HasArgument(args, "--pet-catalog-test");
            var mayhemSourceTest = HasArgument(args, "--mayhem-source-test");
            var floatingBallTest = HasArgument(args, "--floating-ball-test");
            var testMode = petCatalogTest || mayhemSourceTest || floatingBallTest;
            var instanceMutex = petCatalogTest
                ? MutexName + "-PetCatalogTest"
                : (mayhemSourceTest
                    ? MutexName + "-MayhemSourceTest"
                    : (floatingBallTest
                        ? MutexName + "-FloatingBallTest"
                        : (startCleanup ? MutexName + "-ElevatedCleanup" : MutexName)));

            bool createdNew;
            using (var mutex = new Mutex(true, instanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    if (!testMode)
                        MessageBox.Show("FACM 已经在运行。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.ExitCode = 2;
                    return;
                }

                if (petCatalogTest)
                {
                    Environment.ExitCode = RunPetCatalogTest();
                    return;
                }
                if (mayhemSourceTest)
                {
                    Environment.ExitCode = MayhemSourceSmokeTest.Run();
                    return;
                }
                if (floatingBallTest)
                {
                    Environment.ExitCode = FloatingBallSmokeTest.Run();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    RuntimePaths.Initialize();
                    ToolBundleLoader.Prepare();
                }
                catch (Exception exception)
                {
                    AppLog.Error("FACM startup preparation failed", exception);
                    MessageBox.Show(
                        "无法在 FACM.exe 所在目录创建或更新运行文件。\r\n\r\n" +
                        "请把整个 FACM 文件夹放到可写目录后重试，例如 D:\\FACM。\r\n\r\n" +
                        exception.Message,
                        "FACM 启动失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Environment.ExitCode = 3;
                    return;
                }

                Application.ThreadException += (sender, eventArgs) =>
                {
                    AppLog.Error("UI thread exception", eventArgs.Exception);
                    MessageBox.Show("程序遇到错误，详情已写入日志。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                {
                    AppLog.Error("Unhandled exception", eventArgs.ExceptionObject as Exception);
                };

                CompactMenuEnhancer.Install();
                AppLog.Info("FACM started; cleanupRequested=" + startCleanup + "; elevated=" + ElevationService.IsAdministrator);
                Application.Run(new MainForm(startCleanup));
            }
        }

        private static int RunPetCatalogTest()
        {
            try
            {
                PetCatalogSmokeTest.Validate();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 4;
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            return args != null && args.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
