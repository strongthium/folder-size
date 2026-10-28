using System.Collections.Concurrent;
using System.Diagnostics;

namespace Strongthium.FolderSize;

/// <summary>
/// Scans by listing every folder, many at once: a folder's subfolders go on a shared stack, and a thread for each
/// processor (twice over, since most of the time is spent waiting on the disk) takes the next one off it. Works on any
/// drive, with any rights; folders it isn't allowed into are marked and left out.
/// </summary>
public static class FolderScanner
{
    public static ScanResult Scan(string path, ScanProgress progress, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{path} isn't there.");
        var clock = Stopwatch.StartNew();
        progress.Method = ScanMethod.Folders;
        progress.Fraction = null;

        var root = new FolderNode(path, null);
        try { root.LastWrite = Directory.GetLastWriteTimeUtc(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        var workers = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
        var tallies = new Tally[workers];
        var work = new ConcurrentStack<(FolderNode Node, string Path)>();
        using var available = new SemaphoreSlim(0);
        // Folders on the stack or being read. The one that brings it to 0 was the last: it wakes every thread to stop.
        var pending = 1;
        long unreadable = 0;
        work.Push((root, path));
        available.Release();

        var threads = new Thread[workers];
        for (var i = 0; i < workers; i++)
        {
            var tally = tallies[i] = new Tally();
            threads[i] = new Thread(() => Work(tally)) { IsBackground = true, Name = "folder-size scan" };
            threads[i].Start();
        }
        foreach (var thread in threads) thread.Join();
        token.ThrowIfCancellationRequested();

        ScanResult.Total(root);
        var (largest, types) = Tally.Merge(tallies);
        return new ScanResult
        {
            Root = root,
            Largest = largest,
            Types = types,
            Method = ScanMethod.Folders,
            Unreadable = unreadable,
            Elapsed = clock.Elapsed,
        };

        void Work(Tally tally)
        {
            var buffer = new byte[FolderListing.BufferSize];
            while (true)
            {
                available.Wait();
                // Woken with nothing to take: everything's been read.
                if (!work.TryPop(out var item)) return;
                try
                {
                    // Once stopped, what's left is taken off without being read, so that it ends at once.
                    if (!token.IsCancellationRequested) Read(item.Node, item.Path, buffer, tally);
                }
                finally
                {
                    if (Interlocked.Decrement(ref pending) == 0) available.Release(workers);
                }
            }
        }

        void Read(FolderNode node, string folderPath, byte[] buffer, Tally tally)
        {
            using var listing = FolderListing.Open(folderPath, buffer);
            if (listing is null)
            {
                node.Unreadable = true;
                Interlocked.Increment(ref unreadable);
                return;
            }
            progress.Current = folderPath;
            long files = 0, size = 0, allocated = 0;
            var lastWrite = node.LastWrite;
            while (listing.Next(out var entry))
            {
                if (entry.IsDirectory)
                {
                    // A junction or a link: what it points at is counted where it is (or isn't in this scan).
                    if (entry.IsReparsePoint) continue;
                    var child = new FolderNode(entry.Name.ToString(), node) { LastWrite = entry.LastWrite };
                    node.Folders.Add(child);
                    Interlocked.Increment(ref pending);
                    work.Push((child, Path.Combine(folderPath, child.Name)));
                    available.Release();
                }
                else
                {
                    files++;
                    size += entry.Size;
                    allocated += entry.Allocated;
                    var written = entry.LastWrite;
                    if (written > lastWrite) lastWrite = written;
                    tally.Add(entry.Name, node, entry.Size, entry.Allocated, written);
                }
            }
            node.FileCount = files;
            node.FilesSize = size;
            node.FilesAllocated = allocated;
            node.LastWrite = lastWrite;
            progress.Add(files, node.Folders.Count, size);
        }
    }
}

/// <summary>Scans the fastest way there is for a path: the file table for the whole of a local NTFS drive when the
/// program runs as administrator, folder by folder otherwise.</summary>
public static class Scanner
{
    public static ScanResult Scan(string path, ScanProgress progress, CancellationToken token)
    {
        ScanResult? result = null;
        try
        {
            result = FileTableScanner.TryScan(path, progress, token);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            // A volume that can't be read as expected is listed folder by folder instead.
        }
        return result ?? FolderScanner.Scan(path, progress, token);
    }
}
