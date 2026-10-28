using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Strongthium.FolderSize;

/// <summary>
/// Scans a whole NTFS drive by reading its master file table (the MFT) straight from the volume, in big pieces, one
/// read ahead of the processors that pick its records apart. Every file and folder is a record in it, with its name,
/// its folder and its size, so this reads one file instead of opening every folder: a drive of a million files in
/// seconds. Reading a volume needs administrator rights.
/// </summary>
public static unsafe class FileTableScanner
{
    /// <summary>The scan, or null when the path isn't the root of a local NTFS drive this program can read as a
    /// volume (it needs administrator rights): it's scanned folder by folder then.</summary>
    public static ScanResult? TryScan(string path, ScanProgress progress, CancellationToken token)
    {
        var full = Path.GetFullPath(path);
        if (full.Length != 3 || !char.IsAsciiLetter(full[0]) || full[1] != ':' || full[2] != '\\') return null;
        using var volume = Native.CreateFile(@"\\.\" + full[..2], Native.GenericRead, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, 0, 0);
        if (volume.IsInvalid) return null;
        Native.NtfsVolumeData data;
        if (!Native.DeviceIoControl(volume, Native.FsctlGetNtfsVolumeData, null, 0, &data, sizeof(Native.NtfsVolumeData), out _, 0)) return null;
        if (data.BytesPerFileRecordSegment is < 512 or > 65536 || data.BytesPerCluster == 0) return null;
        return new Reader(volume, data, full, progress, token).Scan();
    }

    private sealed class Reader(SafeFileHandle volume, Native.NtfsVolumeData data, string rootPath, ScanProgress progress, CancellationToken token)
    {
        private const int RootRecord = 5;
        private const int ChunkBytes = 4 << 20;
        private const byte Unused = 0, IsFile = 1, IsFolder = 2;
        private const int DosNameSpace = 2;

        private readonly int recordSize = (int)data.BytesPerFileRecordSegment;
        private readonly long clusterSize = data.BytesPerCluster;
        private int count;

        // A slot for each record.
        private byte[] kind = [];
        private int[] parent = [];
        private long[] size = [], allocated = [], lastWrite = [];
        private int[] extension = [];
        private string?[] names = [];
        private sbyte[] nameSpaces = [];

        // Each thread's largest files so far (their records, smallest first), so that only their names are kept.
        private readonly ThreadLocal<PriorityQueue<int, long>> largest = new(() => new(), trackAllValues: true);
        // Files whose attributes go on in other records: their size may be there.
        private readonly ConcurrentBag<int> listed = [];
        private readonly ConcurrentQueue<(int Base, RecordInfo Info, string? Name)> extensionRecords = new();

        private readonly ConcurrentDictionary<string, int> extensionIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> extensionNames = [];
        private readonly Lock extensionLock = new();

        public ScanResult? Scan()
        {
            var clock = Stopwatch.StartNew();
            progress.Method = ScanMethod.FileTable;
            progress.Current = "Reading the file table";
            progress.Fraction = 0;

            // Record 0 is the file table's own: its $DATA says where on the volume the rest of the table is.
            var first = ReadRecordZero();
            if (!FileRecord.ApplyFixups(first)) throw new InvalidDataException("The file table's first record is torn.");
            var runs = FileRecord.DecodeRuns(FileRecord.DataRuns(first));
            // A table so spread out that its own pieces go on in other records, or whose records straddle pieces
            // (clusters smaller than a record), is left to the folder scan.
            if (runs.Sum(run => run.Clusters) * clusterSize < data.MftValidDataLength) return null;
            if (runs.Any(run => run.Clusters * clusterSize % recordSize != 0)) return null;

            count = (int)Math.Min(data.MftValidDataLength / recordSize, int.MaxValue);
            kind = new byte[count];
            parent = new int[count];
            size = new long[count];
            allocated = new long[count];
            lastWrite = new long[count];
            extension = new int[count];
            names = new string?[count];
            nameSpaces = new sbyte[count];

            var chunks = Chunks(runs);
            var buffers = new[] { (nint)NativeMemory.AlignedAlloc(ChunkBytes, 4096), (nint)NativeMemory.AlignedAlloc(ChunkBytes, 4096) };
            Task? reading = null;
            try
            {
                if (chunks.Count > 0) reading = ReadAhead(buffers[0], chunks[0]);
                for (var i = 0; i < chunks.Count; i++)
                {
                    reading!.GetAwaiter().GetResult();
                    token.ThrowIfCancellationRequested();
                    reading = i + 1 < chunks.Count ? ReadAhead(buffers[(i + 1) % 2], chunks[i + 1]) : null;
                    ParseChunk(buffers[i % 2], chunks[i]);
                    progress.Fraction = (i + 1.0) / chunks.Count;
                }
            }
            finally
            {
                // The read under way has to be done with its buffer before it's freed.
                try { reading?.Wait(); }
                catch (AggregateException) { }
                foreach (var buffer in buffers) NativeMemory.AlignedFree((void*)buffer);
            }

            progress.Current = "Putting the folders together";
            progress.Fraction = null;
            ApplyExtensionRecords();
            var result = Build();
            result.Elapsed = clock.Elapsed;
            largest.Dispose();
            return result;
        }

        private byte[] ReadRecordZero()
        {
            var length = (int)Math.Max(recordSize, data.BytesPerSector);
            var buffer = NativeMemory.AlignedAlloc((nuint)length, 4096);
            try
            {
                ReadAt((byte*)buffer, data.MftStartLcn * clusterSize, length);
                return new Span<byte>(buffer, recordSize).ToArray();
            }
            finally
            {
                NativeMemory.AlignedFree(buffer);
            }
        }

        /// <summary>The table, in pieces of up to 4 MB: where each is on the volume, and its first record.</summary>
        private List<(long Offset, int Bytes, int First)> Chunks(List<(long Lcn, long Clusters)> runs)
        {
            var chunks = new List<(long, int, int)>();
            long record = 0;
            foreach (var (lcn, clusters) in runs)
            {
                long runBytes = clusters * clusterSize, position = 0;
                while (position < runBytes && record < count)
                {
                    var bytes = Math.Min(Math.Min(ChunkBytes, runBytes - position), (count - record) * recordSize);
                    // A sparse piece: records never written, so none in use.
                    if (lcn >= 0) chunks.Add((lcn * clusterSize + position, (int)bytes, (int)record));
                    record += bytes / recordSize;
                    position += bytes;
                }
            }
            return chunks;
        }

        private Task ReadAhead(nint buffer, (long Offset, int Bytes, int First) chunk) =>
            Task.Run(() => ReadAt((byte*)buffer, chunk.Offset, chunk.Bytes));

        private void ReadAt(byte* buffer, long offset, int length)
        {
            while (length > 0)
            {
                var overlapped = new NativeOverlapped { OffsetLow = (int)offset, OffsetHigh = (int)(offset >> 32) };
                if (!Native.ReadFile(volume, buffer, length, out var read, &overlapped) || read == 0)
                    throw new IOException($"Couldn't read the file table (error {Marshal.GetLastPInvokeError()}).");
                buffer += read;
                offset += read;
                length -= read;
            }
        }

        private void ParseChunk(nint buffer, (long Offset, int Bytes, int First) chunk)
        {
            var records = chunk.Bytes / recordSize;
            Parallel.ForEach(Partitioner.Create(0, records, 2048), range =>
            {
                var heap = largest.Value!;
                long files = 0, bytes = 0;
                for (var i = range.Item1; i < range.Item2; i++)
                {
                    var record = new Span<byte>((byte*)buffer + (long)i * recordSize, recordSize);
                    if (ParseRecord(chunk.First + i, record, heap, out var fileSize))
                    {
                        files++;
                        bytes += fileSize;
                    }
                }
                progress.Add(files, 0, bytes);
            });
        }

        /// <summary>Keeps what a record says in its slot; true for a file.</summary>
        private bool ParseRecord(int index, Span<byte> record, PriorityQueue<int, long> heap, out long fileSize)
        {
            fileSize = 0;
            if (!FileRecord.ApplyFixups(record) || !FileRecord.Parse(record, out var info)) return false;
            var name = info.NameSpace >= 0 ? MemoryMarshal.Cast<byte, char>(record.Slice(info.NameOffset, info.NameLength * 2)) : default;

            if (info.BaseRecord != 0)
            {
                // The rest of another file's attributes: put with it once every record has been read.
                if ((info.NameSpace >= 0 || info.HasData) && info.BaseRecord < count)
                    extensionRecords.Enqueue(((int)info.BaseRecord, info, info.NameSpace >= 0 ? name.ToString() : null));
                return false;
            }

            parent[index] = info.NameSpace >= 0 && info.Parent < int.MaxValue ? (int)info.Parent : -1;
            nameSpaces[index] = (sbyte)info.NameSpace;
            lastWrite[index] = info.LastWrite;
            if (info.IsDirectory)
            {
                kind[index] = IsFolder;
                names[index] = name.ToString();
                return false;
            }

            kind[index] = IsFile;
            size[index] = info.Size;
            allocated[index] = info.Allocated;
            extension[index] = ExtensionId(Path.GetExtension(name));
            if (info.HasAttributeList)
            {
                names[index] = name.ToString();
                listed.Add(index);
            }
            else if (heap.Count < ScanResult.LargestKept)
            {
                names[index] = name.ToString();
                heap.Enqueue(index, info.Size);
            }
            else if (heap.TryPeek(out _, out var smallest) && info.Size > smallest)
            {
                names[index] = name.ToString();
                heap.EnqueueDequeue(index, info.Size);
            }
            fileSize = info.Size;
            return true;
        }

        private int ExtensionId(ReadOnlySpan<char> name)
        {
            var lookup = extensionIds.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(name, out var id)) return id;
            lock (extensionLock)
            {
                if (lookup.TryGetValue(name, out id)) return id;
                var text = name.ToString().ToLowerInvariant();
                id = extensionNames.Count;
                extensionNames.Add(text);
                extensionIds[text] = id;
                return id;
            }
        }

        private void ApplyExtensionRecords()
        {
            foreach (var (baseRecord, info, name) in extensionRecords)
            {
                if (kind[baseRecord] == Unused) continue;
                // A file whose names are all in other records (one with many hard links), or whose long name is: its
                // own record may have only the short DOS one (a folder of WinSxS with many attributes, say).
                var better = parent[baseRecord] < 0 || (nameSpaces[baseRecord] == DosNameSpace && info.NameSpace != DosNameSpace);
                if (name is not null && better && info.Parent < int.MaxValue)
                {
                    parent[baseRecord] = (int)info.Parent;
                    nameSpaces[baseRecord] = (sbyte)info.NameSpace;
                    names[baseRecord] = name;
                    if (kind[baseRecord] == IsFile) extension[baseRecord] = ExtensionId(Path.GetExtension(name.AsSpan()));
                }
                if (info.HasData && kind[baseRecord] == IsFile)
                {
                    size[baseRecord] = info.Size;
                    allocated[baseRecord] = info.Allocated;
                }
            }
        }

        private ScanResult Build()
        {
            if (count <= RootRecord || kind[RootRecord] != IsFolder) throw new InvalidDataException("The file table has no root folder.");

            // A folder is in the tree if its folders lead up to the root; deleted ones whose record is reused, and
            // anything in them, aren't.
            var reachable = new sbyte[count];   // 0 not known yet, 1 yes, -1 no, 2 on the way up
            reachable[RootRecord] = 1;
            var path = new List<int>();
            bool Reachable(int record)
            {
                path.Clear();
                var node = record;
                bool yes;
                while (true)
                {
                    if (node < 0 || node >= count || kind[node] != IsFolder || reachable[node] == 2) { yes = false; break; }
                    if (reachable[node] != 0) { yes = reachable[node] > 0; break; }
                    reachable[node] = 2;
                    path.Add(node);
                    node = parent[node];
                }
                foreach (var step in path) reachable[step] = (sbyte)(yes ? 1 : -1);
                return yes;
            }

            var folders = new FolderNode?[count];
            for (var i = 0; i < count; i++)
                if (kind[i] == IsFolder && Reachable(i))
                    folders[i] = new FolderNode(names[i] ?? "", null) { LastWrite = Native.Time(lastWrite[i]) };
            var root = folders[RootRecord]!;
            root.Name = rootPath;
            for (var i = 0; i < count; i++)
            {
                if (i == RootRecord || folders[i] is not { } folder) continue;
                var up = folders[parent[i]]!;
                folder.Parent = up;
                up.Folders.Add(folder);
            }

            var typeCount = new long[extensionNames.Count];
            var typeSize = new long[extensionNames.Count];
            var typeAllocated = new long[extensionNames.Count];
            for (var i = 0; i < count; i++)
            {
                if (kind[i] != IsFile || parent[i] < 0 || parent[i] >= count || folders[parent[i]] is not { } folder) continue;
                folder.FileCount++;
                folder.FilesSize += size[i];
                folder.FilesAllocated += allocated[i];
                var written = Native.Time(lastWrite[i]);
                if (written > folder.LastWrite) folder.LastWrite = written;
                typeCount[extension[i]]++;
                typeSize[extension[i]] += size[i];
                typeAllocated[extension[i]] += allocated[i];
            }
            ScanResult.Total(root);

            var candidates = largest.Values.SelectMany(heap => heap.UnorderedItems.Select(item => item.Element)).Concat(listed).Distinct()
                .Where(i => kind[i] == IsFile && names[i] is not null && parent[i] >= 0 && parent[i] < count && folders[parent[i]] is not null)
                .OrderByDescending(i => size[i]).Take(ScanResult.LargestKept)
                .Select(i => new FileItem(names[i]!, folders[parent[i]]!, size[i], allocated[i], Native.Time(lastWrite[i])))
                .ToList();
            var types = Enumerable.Range(0, extensionNames.Count).Where(id => typeCount[id] > 0)
                .Select(id => new FileTypeStat(extensionNames[id], typeCount[id], typeSize[id], typeAllocated[id]))
                .OrderByDescending(type => type.Size).ToList();

            return new ScanResult { Root = root, Largest = candidates, Types = types, Method = ScanMethod.FileTable };
        }
    }
}
