using System;
using System.Diagnostics;
using System.IO;

namespace FACM.Services
{
    internal static class PortablePaths
    {
        private static readonly string BaseDirectoryValue = ResolveBaseDirectory();

        public static string BaseDirectory
        {
            get { return BaseDirectoryValue; }
        }

        public static string File(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("File name is required.", nameof(name));
            return Path.Combine(BaseDirectoryValue, name);
        }

        private static string ResolveBaseDirectory()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var executable = process.MainModule == null ? null : process.MainModule.FileName;
                    var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
                    if (!string.IsNullOrWhiteSpace(directory)) return Path.GetFullPath(directory);
                }
            }
            catch
            {
            }

            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
