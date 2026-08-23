using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SS14.Launcher;

/// <summary>
/// Owns a Windows Job Object configured to terminate every assigned process when
/// the launcher closes its last handle. Child processes inherit membership.
/// </summary>
internal sealed class WindowsJobObject : IDisposable
{
    private readonly SafeJobHandle _handle;

    private WindowsJobObject(SafeJobHandle handle) => _handle = handle;

    public static WindowsJobObject CreateAndAssign(Process process)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var handle = Native.CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось создать Windows Job Object.");
        try
        {
            var limits = new Native.JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new Native.JobObjectBasicLimitInformation
                {
                    LimitFlags = Native.JobObjectLimitKillOnJobClose
                }
            };
            var length = Marshal.SizeOf<Native.JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(limits, pointer, false);
                if (!Native.SetInformationJobObject(handle, Native.JobObjectInfoClass.ExtendedLimitInformation, pointer, (uint)length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось настроить Windows Job Object.");
            }
            finally { Marshal.FreeHGlobal(pointer); }

            if (!Native.AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось добавить сервер в Windows Job Object.");
            return new WindowsJobObject(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Terminate(uint exitCode = 1)
    {
        if (!_handle.IsInvalid && !Native.TerminateJobObject(_handle, exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось завершить дерево процессов сервера.");
    }

    public void Dispose() => _handle.Dispose();

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => Native.CloseHandle(handle);
    }

    private static class Native
    {
        internal const uint JobObjectLimitKillOnJobClose = 0x00002000;

        internal enum JobObjectInfoClass
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeJobHandle CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeJobHandle job, JobObjectInfoClass infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
