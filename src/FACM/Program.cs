using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal static class Program
    {
        private const string MutexName = @"Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D";

        [STAThread]
        private static void Main(string[] args)
        {
            var startCleanup = args != null && args.Any(value => string.Equals(value, "--cleanup", StringComparison.OrdinalIgnoreCase));
            var instanceMutex = startCleanup ? MutexName + "-ElevatedCleanup" : MutexName;

            bool createdNew;
            using (var mutex = new Mutex(true, instanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("FACM 已经在运行。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (sender, eventArgs) =>
                {
                    AppLog.Error("UI thread exception", eventArgs.Exception);
                    MessageBox.Show("程序遇到错误，详情已写入日志。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                {
                    AppLog.Error("Unhandled exception", eventArgs.ExceptionObject as Exception);
                };

                AppLog.Info("FACM started; cleanupRequested=" + startCleanup + "; elevated=" + ElevationService.IsAdministrator);
                Application.Run(new MainForm(startCleanup));
            }
        }
    }
}
