using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Low-space guard for files that grow while the recorder or encoder is running.</summary>
internal sealed class VideoExportStorage(string directory, Func<long> availableBytes)
{
    internal const long MiB = 1024 * 1024;
    internal const long StartupFloor = 64 * MiB;
    internal const long WritingFloor = 32 * MiB;

    internal static VideoExportStorage ForDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (!OperatingSystem.IsWindows()) return new(path, () => new DriveInfo(path).AvailableFreeSpace);
        var apiPath = Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        if (!apiPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            apiPath = apiPath.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + apiPath[2..] : @"\\?\" + apiPath;
        return new(path, () => AvailableAt(apiPath));
    }

    internal void CheckStarting() => CheckSpace(StartupFloor);

    internal void CheckWriting() => CheckSpace(WritingFloor);
    internal void CheckSnapshotWrite(int bytes) => CheckSpace(WritingFloor + bytes);

    private void CheckSpace(long floor)
    {
        var free = availableBytes();
        if (free < floor) throw new VideoExportLowSpaceException(directory, free, floor);
    }

    private static long AvailableAt(string path)
    {
        if (!GetDiskFreeSpaceEx(path, out var available, out _, out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return (long)Math.Min(available, long.MaxValue);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong volumeBytes, out ulong freeBytes);
}

internal sealed class VideoExportLowSpaceException(string directory, long free, long floor)
    : IOException($"Low disk space in {directory} ({free / VideoExportStorage.MiB} MiB free; safety threshold {floor / VideoExportStorage.MiB} MiB).")
{
    internal string OutputDirectory { get; } = directory;
    internal long FreeMiB { get; } = free / VideoExportStorage.MiB;
    internal long ThresholdMiB { get; } = floor / VideoExportStorage.MiB;
}

