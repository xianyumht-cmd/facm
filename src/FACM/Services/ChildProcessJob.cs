using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FACM.Services
{
    internal static class ChildProcessJob
    {
        private const uint JobObjectExtendedLimitInformationClass = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private static readonly object Sync = new object();
        private static IntPtr _job;
        private static bool _creationAttempted;

        public static bool TryAssign(Process process)
        {
            if (process == null) return false;
            try
            {
                var job = EnsureJob();
                if (job == IntPtr.Zero) return false;
                if (AssignProcessToJobObject(job, process.Handle)) return true;

                var error = Marshal.GetLastWin32Error();
                AppLog.Info("Could not assign child process to FACM job object: Win32=" + error);
                return false;
            }
            catch (Exception exception)
            {
                AppLog.Info("Child process job assignment skipped: " + exception.Message);
                return false;
            }
        }

        private static IntPtr EnsureJob()
        {
            lock (Sync)
            {
                if (_job != IntPtr.Zero) return _job;
                if (_creationAttempted) return IntPtr.Zero;
                _creationAttempted = true;

                var job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    AppLog.Info("FACM child process job could not be created: Win32=" + Marshal.GetLastWin32Error());
                    return IntPtr.Zero;
                }

                var info = new JobObjectExtendedLimitInformation
                {
                    BasicLimitInformation = new JobObjectBasicLimitInformation
                    {
                        LimitFlags = JobObjectLimitKillOnJobClose
                    }
                };
                var length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, pointer, false);
                    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, pointer, (uint)length))
                    {
                        var error = Marshal.GetLastWin32Error();
                        CloseHandle(job);
                        AppLog.Info("FACM child process job could not enable kill-on-close: Win32=" + error);
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }

                _job = job;
                AppDomain.CurrentDomain.ProcessExit += delegate { CloseJob(); };
                AppDomain.CurrentDomain.DomainUnload += delegate { CloseJob(); };
                return _job;
            }
        }

        private static void CloseJob()
        {
            lock (Sync)
            {
                if (_job == IntPtr.Zero) return;
                try { CloseHandle(_job); }
                catch { }
                _job = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            uint informationClass,
            IntPtr jobObjectInformation,
            uint jobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
