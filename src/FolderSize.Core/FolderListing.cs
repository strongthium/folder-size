using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Strongthium.FolderSize;

/// <summary>One entry of a folder, as NtQueryDirectoryFile gives it. Its name is in the listing's buffer, and good
/// until the next entry is read.</summary>
internal readonly ref struct FolderEntry(ReadOnlySpan<char> name, long size, long allocated, uint attributes, long lastWrite)
{
    private const uint DirectoryAttribute = 0x10, ReparsePointAttribute = 0x400;

    public ReadOnlySpan<char> Name { get; } = name;
    public long Size { get; } = size;
    public long Allocated { get; } = allocated;
    public uint Attributes { get; } = attributes;
    public DateTime LastWrite => Native.Time(lastWrite);
    public bool IsDirectory => (Attributes & DirectoryAttribute) != 0;
    /// <summary>A junction, a symbolic link, a mount point: what it points at is counted where it really is.</summary>
    public bool IsReparsePoint => (Attributes & ReparsePointAttribute) != 0;
}

/// <summary>
/// Lists a folder with NtQueryDirectoryFile, which hands back dozens of entries a call, each with its size and its
/// size on disk. FindFirstFile has no size on disk, and asking for it file by file would cost a call each.
/// </summary>
internal sealed unsafe class FolderListing : IDisposable
{
    /// <summary>The most a network share hands back in one call.</summary>
    public const int BufferSize = 64 * 1024;

    private readonly SafeFileHandle handle;
    private readonly byte[] buffer;
    private int offset = -1;   // where the next entry is in the buffer; -1 when it needs filling
    private bool finished;

    private FolderListing(SafeFileHandle handle, byte[] buffer)
    {
        this.handle = handle;
        this.buffer = buffer;
    }

    /// <summary>Opens a folder for listing into <paramref name="buffer"/> (<see cref="BufferSize"/> bytes, which a
    /// thread can use for one folder after another); null if it can't be opened.</summary>
    public static FolderListing? Open(string path, byte[] buffer)
    {
        var handle = Native.CreateFile(Native.LongPath(path), Native.FileListDirectory | Native.Synchronize,
            FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, Native.BackupSemantics, 0);
        if (!handle.IsInvalid) return new FolderListing(handle, buffer);
        handle.Dispose();
        return null;
    }

    public bool Next(out FolderEntry entry)
    {
        while (true)
        {
            if (offset < 0)
            {
                if (finished)
                {
                    entry = default;
                    return false;
                }
                int status;
                fixed (byte* start = buffer)
                    status = Native.NtQueryDirectoryFile(handle, 0, 0, 0, out _, start, (uint)buffer.Length, Native.FileDirectoryInformation, 0, 0, 0);
                // STATUS_NO_MORE_FILES at the end; anything else that isn't success is an error, and ends it too.
                if (status != Native.StatusSuccess)
                {
                    finished = true;
                    entry = default;
                    return false;
                }
                offset = 0;
            }

            // FILE_DIRECTORY_INFORMATION
            var span = buffer.AsSpan(offset);
            var next = BinaryPrimitives.ReadInt32LittleEndian(span);
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(span[60..]);
            var name = MemoryMarshal.Cast<byte, char>(span.Slice(64, nameLength));
            offset = next == 0 ? -1 : offset + next;
            if (name is "." or "..") continue;
            entry = new FolderEntry(name,
                size: BinaryPrimitives.ReadInt64LittleEndian(span[40..]),
                allocated: BinaryPrimitives.ReadInt64LittleEndian(span[48..]),
                attributes: BinaryPrimitives.ReadUInt32LittleEndian(span[56..]),
                lastWrite: BinaryPrimitives.ReadInt64LittleEndian(span[24..]));
            return true;
        }
    }

    public void Dispose() => handle.Dispose();
}

/// <summary>The files directly in a folder.</summary>
public static class FolderFiles
{
    /// <summary>What the scan kept, or else what's on the disk now, largest first. Empty if it can't be read.</summary>
    public static List<FileItem> Read(FolderNode folder)
    {
        if (folder.Files is { } known) return [.. known.OrderByDescending(file => file.Size)];
        var files = new List<FileItem>();
        using var listing = FolderListing.Open(folder.Path, new byte[FolderListing.BufferSize]);
        if (listing is null) return files;
        while (listing.Next(out var entry))
            if (!entry.IsDirectory) files.Add(new FileItem(entry.Name.ToString(), folder, entry.Size, entry.Allocated, entry.LastWrite));
        files.Sort((a, b) => b.Size.CompareTo(a.Size));
        return files;
    }
}
