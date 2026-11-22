using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Closes a Windows process tree even if its root has already exited.</summary>
internal static class VideoExportJob
{
    internal static SafeFileHandle? Create()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var limits = new ExtendedLimits { Basic = new() { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())) return job;
        var error = Marshal.GetLastPInvokeError();
        job.Dispose();
        throw new Win32Exception(error);
    }

    internal static void Attach(SafeFileHandle? job, Process process)
    {
        if (job is null || AssignProcessToJobObject(job, process.SafeHandle)) return;
        var error = Marshal.GetLastPInvokeError();
        // A very short operation can exit before assignment. Redirected reads still have a deadline.
        if (!process.HasExited) throw new Win32Exception(error, "Could not supervise the video export subprocess tree.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimits limits, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
}
