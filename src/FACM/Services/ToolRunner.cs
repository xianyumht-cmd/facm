using System;
using System.Diagnostics;
using System.IO;

namespace FACM.Services
{
    internal static class ToolRunner
    {
        public static void RunStandaloneToolA()
        {
            var executable = ToolBundleLoader.Extract("tool-a");
            AppLog.Info("Run built-in tool A");
            Start(executable, string.Empty);
        }

        public static void RunFixLcu(int mode)
        {
            if (mode < 1 || mode > 4) throw new ArgumentOutOfRangeException(nameof(mode));

            // The scripts are bundled and released with the matching mode resource.
            // FACM invokes the executable directly so the selected mode is explicit.
            ToolBundleLoader.Extract("mode-script-" + mode);
            var executable = ToolBundleLoader.Extract("mode-tool");
            AppLog.Info("Run built-in mode " + mode);
            Start(executable, "--mode " + mode);
        }

        private static void Start(string executable, string arguments)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = true
            });
        }
    }
}
