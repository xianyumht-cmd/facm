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
            var animalPetTest = HasArgument(args, "--animal-pet-test");
            var mayhemSourceTest = HasArgument(args, "--mayhem-source-test");
            var mayhemBodyCancellationTest = HasArgument(args, "--mayhem-body-cancellation-test");
            var tencentMayhemPatchTest = HasArgument(args, "--tencent-mayhem-patch-test");
            var aramBaseBalanceTest = HasArgument(args, "--aram-base-balance-test");
            var floatingBallTest = HasArgument(args, "--floating-ball-test");
            var petLocatorTest = HasArgument(args, "--pet-locator-test");
            var embeddedPetHostTest = HasArgument(args, "--embedded-pethost-test");
            var gameLocatorTest = HasArgument(args, "--game-locator-test");
            var testMode = petCatalogTest || animalPetTest || mayhemSourceTest || mayhemBodyCancellationTest ||
                           tencentMayhemPatchTest || aramBaseBalanceTest || floatingBallTest || petLocatorTest ||
                           embeddedPetHostTest || gameLocatorTest;
            var instanceMutex = ResolveMutexName(
                startCleanup,
                petCatalogTest,
                animalPetTest,
                mayhemSourceTest,
                mayhemBodyCancellationTest,
                tencentMayhemPatchTest,
                aramBaseBalanceTest,
                floatingBallTest,
                petLocatorTest,
                embeddedPetHostTest,
                gameLocatorTest);

            bool createdNew;
            using (var mutex = new Mutex(true, instanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    if (!testMode)
                        MessageBox.Show(Ui("FACM 已经在运行。"), Ui("FACM"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.ExitCode = 2;
                    return;
                }

                if (mayhemBodyCancellationTest)
                {
                    Environment.ExitCode = CancelableHttpContentReaderSmokeTest.Run();
                    return;
                }
                if (tencentMayhemPatchTest)
                {
                    Environment.ExitCode = TencentMayhemPatchSmokeTest.Run();
                    return;
                }
                if (aramBaseBalanceTest)
                {
                    Environment.ExitCode = OpggAramBaseBalanceSmokeTest.Run();
                    return;
                }
                if (gameLocatorTest)
                {
                    Environment.ExitCode = GameLocatorSmokeTest.Run();
                    return;
                }
                if (embeddedPetHostTest)
                {
                    Environment.ExitCode = EmbeddedPetHostSmokeTest.Run();
                    return;
                }
                if (petCatalogTest)
                {
                    Environment.ExitCode = RunPetCatalogTest();
                    return;
                }
                if (animalPetTest)
                {
                    Environment.ExitCode = AnimalPetSmokeTest.Run();
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
                if (petLocatorTest)
                {
                    Environment.ExitCode = DesktopHomunculusLocatorSmokeTest.Run();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                try
                {
                    RuntimePaths.Initialize();
                    ToolBundleLoader.Prepare();

                    // Start preparing the exact embedded PetHost as soon as FACM itself is ready. This is
                    // deliberately fire-and-forget: the WinForms message loop must not wait for a large
                    // self-contained host to be hashed/extracted/scanned by antivirus. VPetHostClient will
                    // join this same task if the user activates the pet before warmup has finished.
                    PetHostBundleLoader.BeginWarmup();
                }
                catch (Exception exception)
                {
                    AppLog.Error("FACM startup preparation failed", exception);
                    MessageBox.Show(
                        Ui(
                            "无法在 FACM.exe 所在目录创建或更新运行文件。\r\n\r\n" +
                            "请把整个 FACM 文件夹放到可写目录后重试，例如 D:\\FACM。\r\n\r\n" +
                            exception.Message),
                        Ui("FACM 启动失败"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    Environment.ExitCode = 3;
                    return;
                }

                Application.ThreadException += (sender, eventArgs) =>
                {
                    AppLog.Error("UI thread exception", eventArgs.Exception);
                    MessageBox.Show(Ui("程序遇到错误，详情已写入日志。"), Ui("FACM"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private static string ResolveMutexName(
            bool startCleanup,
            bool petCatalogTest,
            bool animalPetTest,
            bool mayhemSourceTest,
            bool mayhemBodyCancellationTest,
            bool tencentMayhemPatchTest,
            bool aramBaseBalanceTest,
            bool floatingBallTest,
            bool petLocatorTest,
            bool embeddedPetHostTest,
            bool gameLocatorTest)
        {
            if (mayhemBodyCancellationTest) return MutexName + "-MayhemBodyCancellationTest";
            if (tencentMayhemPatchTest) return MutexName + "-TencentMayhemPatchTest";
            if (aramBaseBalanceTest) return MutexName + "-AramBaseBalanceTest";
            if (gameLocatorTest) return MutexName + "-GameLocatorTest";
            if (embeddedPetHostTest) return MutexName + "-EmbeddedPetHostTest";
            if (petCatalogTest) return MutexName + "-PetCatalogTest";
            if (animalPetTest) return MutexName + "-AnimalPetTest";
            if (mayhemSourceTest) return MutexName + "-MayhemSourceTest";
            if (floatingBallTest) return MutexName + "-FloatingBallTest";
            if (petLocatorTest) return MutexName + "-PetLocatorTest";
            return startCleanup ? MutexName + "-ElevatedCleanup" : MutexName;
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

        private static string Ui(string text)
        {
            try
            {
                return UiTextCatalog.Load().Translate(text);
            }
            catch
            {
                return text ?? string.Empty;
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            return args != null && args.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
