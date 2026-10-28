namespace Strongthium.FolderSize;

/// <summary>A folder a scan found: its own files, summed, and the folders in it. A scan keeps folders, not files, so
/// that a drive with millions of files fits in a little memory: a folder's files are read again when they're shown,
/// unless the scan kept them (<see cref="Files"/>).</summary>
public sealed class FolderNode(string name, FolderNode? parent)
{
    /// <summary>The folder's name; the root's is its whole path (<c>C:\</c>, <c>D:\Photos</c>).</summary>
    public string Name { get; internal set; } = name;
    public FolderNode? Parent { get; internal set; } = parent;
    public List<FolderNode> Folders { get; } = [];

    /// <summary>The files directly in it, when the scan kept them; otherwise null, and they're read from the disk.</summary>
    public List<FileItem>? Files { get; internal set; }

    // Its own files, not those in the folders under it.
    public long FilesSize { get; internal set; }
    public long FilesAllocated { get; internal set; }
    public long FileCount { get; internal set; }

    // Everything under it: its files, and every folder under it with theirs. Worked out by ScanResult.Total.
    public long Size { get; internal set; }
    /// <summary>What it takes on the disk, in whole clusters: less than <see cref="Size"/> when compressed or sparse,
    /// more for many small files.</summary>
    public long Allocated { get; internal set; }
    public long TotalFiles { get; internal set; }
    public long TotalFolders { get; internal set; }

    /// <summary>The newest change in it or under it (UTC).</summary>
    public DateTime LastWrite { get; internal set; }

    /// <summary>It couldn't be read (access denied, or gone): what's in it isn't counted.</summary>
    public bool Unreadable { get; internal set; }

    public string Path => Parent is null ? Name : System.IO.Path.Combine(Parent.Path, Name);

    public int Depth
    {
        get
        {
            var depth = 0;
            for (var node = Parent; node is not null; node = node.Parent) depth++;
            return depth;
        }
    }

    public bool IsUnder(FolderNode folder)
    {
        for (var node = this; node is not null; node = node.Parent)
            if (node == folder) return true;
        return false;
    }

    public override string ToString() => Path;
}

/// <summary>A file, in the folder it's in.</summary>
public sealed record FileItem(string Name, FolderNode Folder, long Size, long Allocated, DateTime LastWrite)
{
    public string Path => System.IO.Path.Combine(Folder.Path, Name);
    public string Extension => Extensions.Of(Name);
}

/// <summary>All the files of one extension.</summary>
public sealed record FileTypeStat(string Extension, long Count, long Size, long Allocated);

public enum ScanMethod
{
    /// <summary>Each folder listed, many at once.</summary>
    Folders,
    /// <summary>NTFS's file table (the MFT) read straight from the volume: needs administrator rights.</summary>
    FileTable,
    /// <summary>Made-up data, to try the program out or take its pictures.</summary>
    Sample,
}

/// <summary>What a scan found.</summary>
public sealed class ScanResult
{
    /// <summary>How many of the largest files are kept.</summary>
    public const int LargestKept = 1000;

    public required FolderNode Root { get; init; }
    /// <summary>The largest files, largest first.</summary>
    public required List<FileItem> Largest { get; init; }
    /// <summary>Each extension's files, largest first.</summary>
    public required List<FileTypeStat> Types { get; init; }
    public required ScanMethod Method { get; init; }
    public TimeSpan Elapsed { get; set; }
    /// <summary>How many folders couldn't be read.</summary>
    public long Unreadable { get; init; }

    /// <summary>Works out every folder's totals from its own files and its folders', from the bottom up. Not
    /// recursive, so that a very deep tree can't run out of stack.</summary>
    public static void Total(FolderNode root)
    {
        var order = new List<FolderNode>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            order.Add(node);
            foreach (var child in node.Folders) stack.Push(child);
        }
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            long size = node.FilesSize, allocated = node.FilesAllocated, files = node.FileCount, folders = node.Folders.Count;
            var lastWrite = node.LastWrite;
            foreach (var child in node.Folders)
            {
                size += child.Size;
                allocated += child.Allocated;
                files += child.TotalFiles;
                folders += child.TotalFolders;
                if (child.LastWrite > lastWrite) lastWrite = child.LastWrite;
            }
            node.Size = size;
            node.Allocated = allocated;
            node.TotalFiles = files;
            node.TotalFolders = folders;
            node.LastWrite = lastWrite;
        }
    }

    /// <summary>Takes a folder out of the tree once it's gone (to the Recycle Bin), and its sizes from every folder
    /// above it.</summary>
    public void Remove(FolderNode folder)
    {
        if (folder.Parent is not { } parent) return;
        parent.Folders.Remove(folder);
        for (var node = parent; node is not null; node = node.Parent)
        {
            node.Size -= folder.Size;
            node.Allocated -= folder.Allocated;
            node.TotalFiles -= folder.TotalFiles;
            node.TotalFolders -= folder.TotalFolders + 1;
        }
        Largest.RemoveAll(file => file.Folder.IsUnder(folder));
        folder.Parent = null;
    }

    /// <summary>Takes a file out once it's gone.</summary>
    public void Remove(FileItem file)
    {
        var folder = file.Folder;
        folder.FilesSize -= file.Size;
        folder.FilesAllocated -= file.Allocated;
        folder.FileCount--;
        folder.Files?.Remove(file);
        for (var node = folder; node is not null; node = node.Parent)
        {
            node.Size -= file.Size;
            node.Allocated -= file.Allocated;
            node.TotalFiles--;
        }
        Largest.RemoveAll(f => f.Folder == folder && string.Equals(f.Name, file.Name, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>How far a scan has got, read by the window while it runs.</summary>
public sealed class ScanProgress
{
    private long files, folders, bytes;

    public long Files => Interlocked.Read(ref files);
    public long Folders => Interlocked.Read(ref folders);
    public long Bytes => Interlocked.Read(ref bytes);

    /// <summary>How much is done, 0 to 1, when that's known (reading the file table); otherwise null.</summary>
    public double? Fraction { get; set; }

    /// <summary>What it's at: a folder, or a step.</summary>
    public string Current { get; set; } = "";

    public ScanMethod Method { get; set; } = ScanMethod.Folders;

    internal void Add(long files, long folders, long bytes)
    {
        Interlocked.Add(ref this.files, files);
        Interlocked.Add(ref this.folders, folders);
        Interlocked.Add(ref this.bytes, bytes);
    }
}

public static class Extensions
{
    /// <summary>A file's extension in lower case, with its dot (<c>.mp4</c>), or "" when it has none.</summary>
    public static string Of(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant();

    public static string Describe(string extension) => extension.Length == 0 ? "(no extension)" : extension;
}
