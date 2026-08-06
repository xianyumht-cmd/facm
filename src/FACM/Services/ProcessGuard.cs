using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FACM.Services
{
    internal static class ProcessGuard
    {
        private static readonly string[] BlockedProcessNames =
        {
            "LeagueClient",
            "LeagueClientUx",
            "LeagueClientUxRender",
            "League of Legends",
            "SGuard32",
            "SGuard64",
            "SGuardSvc32",
            "SGuardSvc64",
            "ACE-Helper",
            "ACE-Tray"
        };

        public static IReadOnlyList<string> GetRunningRelatedProcesses()
        {
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName;
                    if (BlockedProcessNames.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        running.Add(name);
                    }
                }
                catch
                {
                    // Processes can exit or deny access while enumerating.
                }
                finally
                {
                    process.Dispose();
                }
            }
            return running.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
