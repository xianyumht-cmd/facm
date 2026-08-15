using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.AppHost.Modules;
using FACM.Mayhem;
using FACM.Performance;
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
            var singleInstanceActivationTest = HasArgument(args, "--single-instance-activation-test");
            var facmHostTest = HasArgument(args, "--facm-host-test");
            var performanceContractTest = HasArgument(args, "--performance-contract-test");
            var leagueDashboardTest = HasArgument(args, "--league-dashboard-test");
            var testMode = petCatalogTest || animalPetTest || mayhemSourceTest || mayhemBodyCancellationTest ||
                           tencentMayhemPatchTest || aramBaseBalanceTest || floatingBallTest || petLocatorTest ||
                           embeddedPetHostTest || gameLocatorTest || singleInstanceActivationTest || facmHostTest ||
                           performanceContractTest || leagueDashboardTest;
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
                gameLocatorTest,
                singleInstanceActivationTest,
                facmHostTest,
                performanceContractTest,
                leagueDashboardTest);

            bool createdNew;
            using (var mutex = new Mutex(true, instanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    if (!testMode && !startCleanup && SingleInstanceActivation.TrySignalExisting(TimeSpan.FromMilliseconds(1600)))
                    {
                        Environment.ExitCode = 0;
                        return;
                    }

                    if (!testMode)
                        MessageBox.Show(Ui("FACM 已经在运行。"), Ui("FACM"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Environment.ExitCode = 2;
                    return;
                }

                if (facmHostTest)
                {
                    Environment.ExitCode = FacmHostSmokeTest.Run();
                    return;
                }
                if (performanceContractTest)
                {
                    Environment.ExitCode = PerformanceContractSmokeTest.Run();
                    return;
                }
                if (leagueDashboardTest)
                {
                    Environment.ExitCode = LeagueDashboardSmokeTest.Run();
                    return;
                }
                if (singleInstanceActivationTest)
                {
                    Environment.ExitCode = SingleInstanceActivation.RunSmokeTest();
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
                    // Only create the tiny writable runtime skeleton before the message loop starts.
                    // ToolBundle/PetHost hashing and extraction are deliberately deferred until the
                    // floating FACM shell has had a chance to paint, so a heavy optional runtime can
                    // never make the application look like it failed to launch.
                    RuntimePaths.Initialize();
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

                var settings = new SettingsModule();
                var tools = new ToolsModule();
                var online = new OnlineModule();
                var pets = new PetsModule();
                var performance = new PerformanceModule();
                var leagueClient = new LeagueClientModule();
                var leagueDashboard = new LeagueDashboardModule(leagueClient, performance);
                var leaguePlayer = new LeaguePlayerModule(leagueClient, performance);
                var leagueLive = new LeagueLiveModule(leagueClient, performance);
                var leagueAdvisor = new LeagueBuildAdvisorModule(leagueClient, performance);
                var leagueEfficiency = new LeagueEfficiencyModule(settings, leagueClient, leagueDashboard);
                var mayhem = new MayhemModule(leagueClient);
                var cleanup = new CleanupModule();
                var shell = new ShellModule(startCleanup, settings, tools, online, pets, leagueDashboard, leaguePlayer, leagueLive, mayhem, cleanup);
                using (var host = CreateHost(settings, tools, online, pets, performance, leagueClient, leagueDashboard, leaguePlayer, leagueLive, leagueAdvisor, leagueEfficiency, mayhem, cleanup, shell))
                {
                    try
                    {
                        host.Initialize();
                    }
                    catch (Exception exception)
                    {
                        AppLog.Error("FACM host startup failed", exception);
                        MessageBox.Show(
                            Ui("FACM 应用模块初始化失败，详情已写入日志。\r\n\r\n" + exception.Message),
                            Ui("FACM 启动失败"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        Environment.ExitCode = 3;
                        return;
                    }

                    var mainForm = shell.MainForm;
                    if (mainForm == null)
                    {
                        AppLog.Error("FACM shell module initialized without a MainForm", null);
                        Environment.ExitCode = 3;
                        return;
                    }

                    AppLog.Info("FACM started; cleanupRequested=" + startCleanup + "; elevated=" + cleanup.IsAdministrator);

                    SingleInstanceActivation activation = null;
                    try
                    {
                        if (!startCleanup)
                            activation = SingleInstanceActivation.Listen(mainForm.RequestExternalActivation);
                        Application.Run(mainForm);
                    }
                    finally
                    {
                        if (activation != null) activation.Dispose();
                    }
                }
            }
        }

        private static FacmHost CreateHost(
            SettingsModule settings,
            ToolsModule tools,
            OnlineModule online,
            PetsModule pets,
            PerformanceModule performance,
            LeagueClientModule leagueClient,
            LeagueDashboardModule leagueDashboard,
            LeaguePlayerModule leaguePlayer,
            LeagueLiveModule leagueLive,
            LeagueBuildAdvisorModule leagueAdvisor,
            LeagueEfficiencyModule leagueEfficiency,
            MayhemModule mayhem,
            CleanupModule cleanup,
            ShellModule shell)
        {
            var host = new FacmHost();
            host.Register(new CompactMenuEnhancerModule());
            host.Register(settings);
            host.Register(tools);
            host.Register(online);
            host.Register(pets);
            host.Register(performance);
            host.Register(leagueClient);
            host.Register(leagueDashboard);
            host.Register(leaguePlayer);
            host.Register(leagueLive);
            host.Register(leagueAdvisor);
            host.Register(leagueEfficiency);
            host.Register(mayhem);
            host.Register(cleanup);
            host.Register(shell);
            return host;
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
            bool gameLocatorTest,
            bool singleInstanceActivationTest,
            bool facmHostTest,
            bool performanceContractTest,
            bool leagueDashboardTest)
        {
            if (facmHostTest) return MutexName + "-FacmHostTest";
            if (performanceContractTest) return MutexName + "-PerformanceContractTest";
            if (leagueDashboardTest) return MutexName + "-LeagueDashboardTest";
            if (singleInstanceActivationTest) return MutexName + "-SingleInstanceActivationTest";
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
