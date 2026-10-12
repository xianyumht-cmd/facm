using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FACM.Services
{
    internal static class DiagnosticsShellAction
    {
        public const string ActionName = "FACM.More.Diagnostics";
        public const int Order = 80;

        public static ToolStripMenuItem CreateMenuItem()
        {
            var ui = UiTextCatalog.Load();
            var item = new ToolStripMenuItem(DiagnosticsUiText.Get(ui, DiagnosticsUiTextKeys.Export))
            {
                Name = ActionName,
                Tag = Order
            };
            item.Click += async delegate { await ExportAsync(ui); };
            return item;
        }

        private static async Task ExportAsync(UiTextCatalog ui)
        {
            try
            {
                var receipt = await Task.Run(delegate { return DiagnosticsExportService.ExportCurrent(); });
                AppLog.Info(
                    "Diagnostics bundle exported; bytes=" + receipt.BundleBytes +
                    "; logs=" + receipt.LogFilesIncluded +
                    "; skipped=" + receipt.LogFilesSkipped);
                RevealInExplorer(receipt.BundlePath);
                MessageBox.Show(
                    string.Format(DiagnosticsUiText.Get(ui, DiagnosticsUiTextKeys.ExportSuccessFormat), receipt.BundlePath),
                    ui == null ? "FACM" : ui.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                AppLog.Error("Diagnostics bundle export failed", exception);
                MessageBox.Show(
                    string.Format(DiagnosticsUiText.Get(ui, DiagnosticsUiTextKeys.ExportFailedFormat), exception.Message),
                    ui == null ? "FACM" : ui.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // The bundle remains valid even if Explorer cannot be opened.
            }
        }
    }
}
