using System;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM
{
    internal static class Program
    {
        private const string MutexName = @"Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("FACM 已经在运行。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (sender, args) =>
                {
                    AppLog.Error("UI thread exception", args.Exception);
                    MessageBox.Show("程序遇到错误，详情已写入日志。", "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                {
                    AppLog.Error("Unhandled exception", args.ExceptionObject as Exception);
                };

                AppLog.Info("FACM started");
                Application.Run(new MainForm());
            }
        }
    }
}
