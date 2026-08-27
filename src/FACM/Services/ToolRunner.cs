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

        [Obsolete("League game repair is FACM-native. Use LeagueGameRepairModule instead.")]
        public static void RunFixLcu(int mode)
        {
            if (mode < 1 || mode > 4) throw new ArgumentOutOfRangeException(nameof(mode));
            throw new NotSupportedException("旧 Fix-LCU-Window 外部进程已停用，请使用 FACM 的原生“游戏修复”。");
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
