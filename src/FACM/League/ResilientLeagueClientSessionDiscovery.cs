using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FACM.Services;
using System.IO;
using System.Text;
using System.Threading;

namespace FACM.League
{
    internal sealed class ResilientLeagueClientSessionDiscovery : ILeagueClientSessionDiscovery
    {
        private static readonly string[] ProcessNames = { "LeagueClientUx", "LeagueClient" };

        private LeagueClientSession _failed;
        private DateTime _retryFailedAfterUtc;
        internal string LastEvidence { get; private set; } = "not_checked";

        internal void ReportConnectionFailure(LeagueClientSession session)
        {
            _failed = session;
            _retryFailedAfterUtc = DateTime.UtcNow.AddSeconds(3);
        }

        public LeagueClientSession TryDiscover()
        {
            var processes = new List<Process>();
            var live = new HashSet<int>();
            LastEvidence = "no_candidate";
            try
            {
                foreach (var name in ProcessNames)
                    processes.AddRange(Process.GetProcessesByName(name));
                foreach (var process in processes)
                {
                    try { if (!process.HasExited) live.Add(process.Id); }
                    catch (InvalidOperationException) { }
                }
                if (live.Count == 0) { LastEvidence = "client_not_running"; return null; }
                var listeners = ReadLoopbackListeners();

                // Current UX arguments are preferred; a parsed lockfile is not evidence of a live endpoint.
                foreach (var process in processes)
                {
                    try
                    {
                        if (!string.Equals(process.ProcessName, "LeagueClientUx", StringComparison.OrdinalIgnoreCase)) continue;
                        string commandLine;
                        LeagueClientSession candidate;
                        if (WmiProcessImagePathReader.TryReadCommandLine(process.Id, out commandLine) &&
                            LeagueClientSessionParser.TryParseCommandLine(commandLine, out candidate) &&
                            Accept(candidate, live, listeners)) return candidate;
                    }
                    catch (Exception e) { AppLog.Info("LCU command discovery unavailable: " + e.GetType().Name); }
                }

                foreach (var process in processes)
                {
                    try
                    {
                        string executable, pathSource;
                        if (!TryResolveImagePath(process, out executable, out pathSource)) continue;
                        var directory = Path.GetDirectoryName(executable);
                        if (string.IsNullOrWhiteSpace(directory)) continue;
                        LeagueClientSession candidate;
                        if (TryFromLockfile(Path.Combine(directory, "lockfile"), pathSource, out candidate) &&
                            Accept(candidate, live, listeners)) return candidate;
                    }
                    catch (Exception e) { AppLog.Info("LCU lockfile discovery unavailable: " + e.GetType().Name); }
                }
                return null;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }

        private bool Accept(LeagueClientSession candidate, HashSet<int> live,
            Dictionary<int, HashSet<int>> listeners)
        {
            var reason = CandidateStatus(candidate, live, listeners);
            if (reason != "listener_owned" && reason != "listener_unknown") { LastEvidence = reason; return false; }
            if (_failed != null && _failed.Matches(candidate) && DateTime.UtcNow < _retryFailedAfterUtc)
            { LastEvidence = "candidate_cooldown"; return false; }
            LastEvidence = reason;
            return true;
        }

        internal static string CandidateStatus(LeagueClientSession candidate, HashSet<int> live,
            Dictionary<int, HashSet<int>> listeners)
        {
            if (candidate == null) return "no_candidate";
            if (candidate.ProcessId > 0 && !live.Contains(candidate.ProcessId)) return "stale_pid";
            if (candidate.ProcessId <= 0 && candidate.Source.StartsWith("lockfile", StringComparison.Ordinal))
                return "invalid_lockfile_pid";
            if (listeners == null)
                return candidate.ProcessId > 0 ? "listener_unknown" : "owner_unknown";
            HashSet<int> owners;
            if (!listeners.TryGetValue(candidate.Port, out owners)) return "not_listening";
            foreach (var owner in owners)
                if (live.Contains(owner)) return "listener_owned";
            return "listener_owner_mismatch";
        }

        // IPv4 only: the LCU transport uses the literal 127.0.0.1 address.
        internal static Dictionary<int, HashSet<int>> ReadLoopbackListeners()
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    if (result != 122 || size < 4 || size > 4 * 1024 * 1024) return null;
                    buffer = Marshal.AllocHGlobal(size);
                    var allocated = size;
                    result = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                    if (result == 0)
                    {
                        var count = Marshal.ReadInt32(buffer);
                        if (count < 0 || count > (allocated - 4) / 24) return null;
                        var table = new Dictionary<int, HashSet<int>>();
                        for (var i = 0; i < count; i++)
                        {
                            var offset = 4 + i * 24;
                            var address = Marshal.ReadInt32(buffer, offset + 4);
                            if (address != 0 && address != 0x0100007f) continue;
                            var port = (Marshal.ReadByte(buffer, offset + 8) << 8) | Marshal.ReadByte(buffer, offset + 9);
                            var pid = Marshal.ReadInt32(buffer, offset + 20);
                            HashSet<int> owners;
                            if (!table.TryGetValue(port, out owners)) table.Add(port, owners = new HashSet<int>());
                            owners.Add(pid);
                        }
                        return table;
                    }
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                }
                return null;
            }
            catch (Exception) { return null; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }

        [DllImport("iphlpapi.dll", SetLastError = false)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);

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

        private static bool TryFromLockfile(string lockfile, string pathSource, out LeagueClientSession session)
        {
            session = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                string content;
                if (!TryReadSharedText(lockfile, out content)) break;

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
