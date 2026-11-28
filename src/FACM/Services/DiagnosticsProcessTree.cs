using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace FACM.Services
{
    internal static class DiagnosticsProcessTree
    {
        private sealed class Entry
        {
            internal int Id;
            internal int Parent;
            internal string Name;
        }

        internal static void AppendWebViewMetrics(StringBuilder builder)
        {
            try
            {
                using (var host = Process.GetCurrentProcess())
                {
                    var entries = ReadSnapshot();
                    var ids = SelectWebViewDescendants(host.Id, entries);
                    long working = 0, privateBytes = 0, threads = 0;
                    int sampled = 0;
                    var hostStart = host.StartTime.ToUniversalTime();
                    foreach (var id in ids)
                    {
                        try
                        {
                            using (var process = Process.GetProcessById(id))
                            {
                                if (!string.Equals(process.ProcessName, "msedgewebview2", StringComparison.OrdinalIgnoreCase) ||
                                    process.StartTime.ToUniversalTime() < hostStart) continue;
                                var ws = process.WorkingSet64;
                                var priv = process.PrivateMemorySize64;
                                var count = process.Threads.Count;
                                working += ws;
                                privateBytes += priv;
                                threads += count;
                                sampled++;
                            }
                        }
                        catch (Exception) { }
                    }
                    builder.AppendLine("WebViewMetricsScope=host_descendants_snapshot");
                    Append(builder, "WebViewProcessCount", ids.Count);
                    Append(builder, "WebViewSampledCount", sampled);
                    Append(builder, "WebViewWorkingSetSumBytes", working);
                    Append(builder, "WebViewPrivateMemorySumBytes", privateBytes);
                    Append(builder, "WebViewThreadSum", threads);
                    builder.Append("WebViewMetricsState=").AppendLine(sampled == ids.Count ? "complete_snapshot" : "partial_snapshot");
                }
            }
            catch (Exception error)
            {
                builder.Append("WebViewMetricsState=unavailable_").AppendLine(error.GetType().Name);
            }
        }

        private static void Append(StringBuilder builder, string key, long value)
        {
            builder.Append(key).Append('=').AppendLine(value.ToString(CultureInfo.InvariantCulture));
        }

        private static List<int> SelectWebViewDescendants(int root, List<Entry> entries)
        {
            var descendants = new HashSet<int> { root };
            bool changed;
            do
            {
                changed = false;
                foreach (var entry in entries)
                    if (entry.Id > 0 && descendants.Contains(entry.Parent))
                        changed |= descendants.Add(entry.Id);
            } while (changed);

            var result = new List<int>();
            foreach (var entry in entries)
                if (entry.Id != root && descendants.Contains(entry.Id) &&
                    string.Equals(entry.Name, "msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                    result.Add(entry.Id);
            return result;
        }

        private static List<Entry> ReadSnapshot()
        {
            var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) throw new InvalidOperationException("Process snapshot unavailable.");
            try
            {
                var result = new List<Entry>();
                var item = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (!Process32First(snapshot, ref item)) throw new InvalidOperationException("Process snapshot empty.");
                do
                {
                    result.Add(new Entry { Id = (int)item.ProcessId, Parent = (int)item.ParentId, Name = item.Executable });
                    if (result.Count > 32768) throw new InvalidOperationException("Process snapshot limit exceeded.");
                    item.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
                } while (Process32Next(snapshot, ref item));
                if (Marshal.GetLastWin32Error() != 18) throw new InvalidOperationException("Process snapshot incomplete.");
                return result;
            }
            finally { CloseHandle(snapshot); }
        }

        internal static void ValidateForSmokeTest()
        {
            var ids = SelectWebViewDescendants(1, new List<Entry>
            {
                new Entry { Id = 3, Parent = 2, Name = "msedgewebview2.exe" },
                new Entry { Id = 2, Parent = 1, Name = "msedgewebview2.exe" },
                new Entry { Id = 5, Parent = 4, Name = "msedgewebview2.exe" },
                new Entry { Id = 6, Parent = 1, Name = "other.exe" }
            });
            if (ids.Count != 2 || !ids.Contains(2) || !ids.Contains(3))
                throw new InvalidOperationException("Diagnostic browser scope included unrelated processes.");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal uint Size, Usage, ProcessId;
            internal UIntPtr DefaultHeapId;
            internal uint ModuleId, Threads, ParentId;
            internal int Priority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
