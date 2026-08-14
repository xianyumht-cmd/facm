using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FACM.League
{
    internal sealed class ResilientLeagueClientSessionDiscovery : ILeagueClientSessionDiscovery
    {
        private static readonly string[] ProcessNames = { "LeagueClientUx", "LeagueClient" };

        public LeagueClientSession TryDiscover()
        {
            foreach (var processName in ProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        LeagueClientSession session;
                        if (TryFromProcess(process, out session)) return session;
                    }
                    catch
                    {
                        // The client can exit while discovery is in progress.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return null;
        }

        internal static bool TryResolveImagePath(Process process, out string imagePath, out string source)
        {
            imagePath = null;
            source = null;
            if (process == null) return false;

            try
            {
                var module = process.MainModule;
                imagePath = module == null ? null : module.FileName;
                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    source = "main-module";
                    return true;
                }
            }
            catch
            {
                // Fall through to the limited WMI ExecutablePath query.
            }

            if (WmiProcessImagePathReader.TryRead(process.Id, out imagePath))
            {
                source = "wmi-image-path";
                return true;
            }

            imagePath = null;
            source = null;
            return false;
        }

        private static bool TryFromProcess(Process process, out LeagueClientSession session)
        {
            session = null;
            string executable;
            string pathSource;
            if (!TryResolveImagePath(process, out executable, out pathSource)) return false;

            var directory = Path.GetDirectoryName(executable);
            if (string.IsNullOrWhiteSpace(directory)) return false;

            var lockfile = Path.Combine(directory, "lockfile");
            if (!File.Exists(lockfile)) return false;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                string content;
                if (!TryReadSharedText(lockfile, out content)) return false;

                LeagueClientSession parsed;
                if (LeagueClientSessionParser.TryParseLockfile(content, out parsed))
                {
                    session = new LeagueClientSession(
                        parsed.ProcessName,
                        parsed.ProcessId,
                        parsed.Port,
                        parsed.Password,
                        parsed.Protocol,
                        "lockfile-" + pathSource,
                        parsed.PlatformId,
                        parsed.Region);
                    return true;
                }

                if (attempt < 2) Thread.Sleep(20);
            }

            return false;
        }

        internal static bool TryReadSharedText(string path, out string content)
        {
            content = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        content = reader.ReadToEnd();
                        return !string.IsNullOrWhiteSpace(content);
                    }
                }
                catch (IOException)
                {
                    if (attempt >= 2) return false;
                    Thread.Sleep(35);
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
