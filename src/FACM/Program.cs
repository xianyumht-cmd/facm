using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
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
            var petSmokeTest = HasArgument(args, "--pet-smoke-test");
            var instanceMutex = petSmokeTest
                ? MutexName + "-PetSmokeTest"
                : (startCleanup ? MutexName + "-ElevatedCleanup" : MutexName);

            bool createdNew;
            using (var mutex = new Mutex(true, instanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    if (!petSmokeTest)
                        MessageBox.Show("FACM 已经在运行。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.ExitCode = 2;
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
                    if (!petSmokeTest)
                    {
                        MessageBox.Show(
                            "无法在 FACM.exe 所在目录创建或更新运行文件。\r\n\r\n" +
                            "请把整个 FACM 文件夹放到可写目录后重试，例如 D:\\FACM。\r\n\r\n" +
                            exception.Message,
                            "FACM 启动失败",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    Environment.ExitCode = 3;
                    return;
                }

                if (petSmokeTest)
                {
                    Environment.ExitCode = RunPetSmokeTest();
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

        private static int RunPetSmokeTest()
        {
            try
            {
                foreach (var pet in PetCatalog.All)
                {
                    var model = Pet3DModelFactory.Create(pet);
                    if (model == null || model.Model == null || model.Model.Children.Count < 2)
                        throw new InvalidOperationException("3D pet model is empty: " + pet.Id);

                    using (var scene = new Pet3DScene(pet))
                    {
                        scene.Measure(new System.Windows.Size(pet.Size.Width, pet.Size.Height));
                        scene.Arrange(new System.Windows.Rect(0, 0, pet.Size.Width, pet.Size.Height));
                        scene.UpdateLayout();
                    }
                    AppLog.Info("3D pet smoke test passed: " + pet.Id);
                }
                return 0;
            }
            catch (Exception exception)
            {
                AppLog.Error("3D pet smoke test failed", exception);
                return 4;
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            return args != null && args.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
