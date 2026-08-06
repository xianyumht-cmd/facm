using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace FACM.Services
{
    internal static class ElevationService
    {
        public static bool IsAdministrator
        {
            get
            {
                try
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool RestartElevatedForCleanup()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--cleanup",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                });
                return true;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                AppLog.Info("Elevation was cancelled by the user");
                return false;
            }
            catch (Exception exception)
            {
                AppLog.Error("Failed to restart elevated", exception);
                MessageBox.Show("无法获取管理员权限：" + exception.Message, "FACM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
