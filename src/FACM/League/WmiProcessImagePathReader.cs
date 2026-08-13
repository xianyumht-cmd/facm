using System;
using System.Management;

namespace FACM.League
{
    internal static class WmiProcessImagePathReader
    {
        public static bool TryRead(int processId, out string imagePath)
        {
            imagePath = null;
            if (processId <= 0 || Environment.OSVersion.Platform != PlatformID.Win32NT) return false;

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + processId))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        using (item)
                        {
                            imagePath = item["ExecutablePath"] as string;
                            return !string.IsNullOrWhiteSpace(imagePath);
                        }
                    }
                }
            }
            catch
            {
                imagePath = null;
            }

            return false;
        }
    }
}
