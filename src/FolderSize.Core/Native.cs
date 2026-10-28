using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Strongthium.FolderSize;

/// <summary>What .NET leaves to Windows: listing a folder with its sizes on disk in one call, reading a volume, and the
/// Recycle Bin.</summary>
internal static unsafe partial class Native
{
    public const uint FileListDirectory = 0x0001, Synchronize = 0x0010_0000, GenericRead = 0x8000_0000;
    /// <summary>Lets CreateFile open a folder (and, for an administrator, one its permissions would refuse).</summary>
    public const uint BackupSemantics = 0x0200_0000;

    public const int StatusSuccess = 0;
    /// <summary>FileDirectoryInformation: names, times, sizes and attributes.</summary>
    public const int FileDirectoryInformation = 1;
    public const uint FsctlGetNtfsVolumeData = 0x0009_0064;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security, FileMode mode, uint flags, nint template);

    [StructLayout(LayoutKind.Sequential)]
    public struct IoStatusBlock
    {
        public nint Status;
        public nint Information;
    }

    /// <summary>Fills <paramref name="buffer"/> with as many of a folder's entries as fit; each call goes on from
    /// where the last stopped.</summary>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryDirectoryFile(SafeFileHandle file, nint evt, nint apcRoutine, nint apcContext, out IoStatusBlock status,
        void* buffer, uint length, int informationClass, byte returnSingleEntry, nint fileName, byte restartScan);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize,
        out int returned, nint overlapped);

    /// <summary>Reads at an offset: the handle is synchronous, so this returns once it has read.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(SafeFileHandle file, byte* buffer, int toRead, out int read, NativeOverlapped* overlapped);

    /// <summary>NTFS_VOLUME_DATA_BUFFER: where the file table starts, and how big its records and the clusters are.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NtfsVolumeData
    {
        public long VolumeSerialNumber, NumberSectors, TotalClusters, FreeClusters, TotalReserved;
        public uint BytesPerSector, BytesPerCluster, BytesPerFileRecordSegment, ClustersPerFileRecordSegment;
        public long MftValidDataLength, MftStartLcn, Mft2StartLcn, MftZoneStart, MftZoneEnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public nint From;
        public nint To;
        public ushort Flags;
        public int Aborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref FileOperation operation);

    /// <summary>Moves a file or a folder to the Recycle Bin, with Windows' own progress and error dialogs; Windows asks
    /// first if it's too big for the bin and would be deleted for good. False if it wasn't moved.</summary>
    public static bool Recycle(string path, nint owner)
    {
        const uint Delete = 3;
        const ushort AllowUndo = 0x40, NoConfirmation = 0x10, WantNukeWarning = 0x4000;
        // A list of paths, each ending in a null, and the list in another.
        var from = Marshal.StringToHGlobalUni(path + "\0\0");
        try
        {
            var operation = new FileOperation { Window = owner, Function = Delete, From = from, Flags = AllowUndo | NoConfirmation | WantNukeWarning };
            return SHFileOperation(ref operation) == 0 && operation.Aborted == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    /// <summary>A path CreateFile takes at any length: <c>\\?\C:\…</c>, or <c>\\?\UNC\server\share\…</c>.</summary>
    public static string LongPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    /// <summary>A FILETIME as a date, or DateTime.MinValue when it isn't one.</summary>
    public static DateTime Time(long fileTime) =>
        fileTime > 0 && fileTime < 2650467743999999999 ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue;
}

/// <summary>The Recycle Bin, for what the user chooses to remove.</summary>
public static class RecycleBin
{
    /// <summary>Moves a file or a folder to the Recycle Bin, with Windows' own progress and errors. False if it wasn't
    /// moved (it failed, or was cancelled).</summary>
    public static bool Move(string path, nint owner) => Native.Recycle(path, owner);
}
