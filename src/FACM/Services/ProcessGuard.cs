using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FACM.Configuration;

namespace FACM.Services
{
    internal static class ProcessGuard
    {
        public static IReadOnlyList<string> GetRunningRelatedProcesses()
        {
            if (!CleanupProfile.IsConfigured) return new string[0];

            var configured = new HashSet<string>(CleanupProfile.NormalizedProcessNames, StringComparer.OrdinalIgnoreCase);
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName;
                    if (configured.Contains(name)) running.Add(name);
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
