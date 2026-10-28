namespace Strongthium.FolderSize;

/// <summary>
/// One scanning thread's count of the files it saw: the largest so far, and the totals by extension. Each thread has
/// its own, so that none waits on another; they're put together at the end. A file's name is only made into a string
/// when it's among the largest so far.
/// </summary>
internal sealed class Tally
{
    private sealed class TypeTotals
    {
        public long Count, Size, Allocated;
    }

    private readonly PriorityQueue<FileItem, long> largest = new();   // the smallest of them first
    private readonly Dictionary<string, TypeTotals> types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TypeTotals>.AlternateLookup<ReadOnlySpan<char>> typesByName;

    public Tally() => typesByName = types.GetAlternateLookup<ReadOnlySpan<char>>();

    public void Add(ReadOnlySpan<char> name, FolderNode folder, long size, long allocated, DateTime lastWrite)
    {
        var extension = Path.GetExtension(name);
        if (!typesByName.TryGetValue(extension, out var totals))
        {
            totals = new TypeTotals();
            types.Add(extension.ToString().ToLowerInvariant(), totals);
        }
        totals.Count++;
        totals.Size += size;
        totals.Allocated += allocated;

        if (largest.Count < ScanResult.LargestKept)
            largest.Enqueue(new FileItem(name.ToString(), folder, size, allocated, lastWrite), size);
        else if (largest.TryPeek(out _, out var smallest) && size > smallest)
            largest.EnqueueDequeue(new FileItem(name.ToString(), folder, size, allocated, lastWrite), size);
    }

    public static (List<FileItem> Largest, List<FileTypeStat> Types) Merge(IEnumerable<Tally> tallies)
    {
        var largest = new List<FileItem>();
        var types = new Dictionary<string, FileTypeStat>();
        foreach (var tally in tallies)
        {
            largest.AddRange(tally.largest.UnorderedItems.Select(item => item.Element));
            foreach (var (extension, totals) in tally.types)
                types[extension] = types.TryGetValue(extension, out var stat)
                    ? stat with { Count = stat.Count + totals.Count, Size = stat.Size + totals.Size, Allocated = stat.Allocated + totals.Allocated }
                    : new FileTypeStat(extension, totals.Count, totals.Size, totals.Allocated);
        }
        largest.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (largest.Count > ScanResult.LargestKept) largest.RemoveRange(ScanResult.LargestKept, largest.Count - ScanResult.LargestKept);
        return (largest, [.. types.Values.OrderByDescending(type => type.Size)]);
    }
}
