namespace Strongthium.FolderSize;

/// <summary>
/// A made-up PC's C: drive, the same every time: Windows, programs and games, and the photos, videos and downloads of
/// someone called Alex. For trying the program out (<c>--sample</c>) and for its pictures, which then show nothing
/// of a real computer. Companies and programs in it are made up (Fabrikam, Northwind, Tailspin) or open source.
/// </summary>
public static class SampleData
{
    private const long KB = 1024, MB = KB * 1024, GB = MB * 1024;
    private static readonly DateTime Now = new(2026, 10, 27, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>The made-up drives: C: with this data on it, and a data drive.</summary>
    public static List<(string Root, string Label, long Total, long Free)> Drives(ScanResult sample) =>
    [
        (@"C:\", "Local Disk", 476_327_165_952, 476_327_165_952 - sample.Root.Allocated - 3 * GB),
        (@"D:\", "Data", 2_000_381_014_016, 1_247_105_302_528),
        (@"E:\", "Backup", 1_000_186_310_656, 611_428_499_456),
    ];

    private abstract record Part;
    private sealed record Folder(string Name, Part[] Parts) : Part;
    private sealed record OneFile(string Name, long Size) : Part;
    /// <summary>So many files of these extensions, adding up to about so much.</summary>
    private sealed record Files(int Count, long Total, string[] Extensions) : Part;
    /// <summary>So many folders named by <c>Name</c>, each with its share of files.</summary>
    private sealed record Spread(int Folders, int FilesEach, long Total, Func<Random, int, string> Name, string[] Extensions) : Part;

    private static Folder F(string name, params Part[] parts) => new(name, parts);
    private static OneFile One(string name, long size) => new(name, size);
    private static Files Many(int count, long total, params string[] extensions) => new(count, total, extensions);

    private static Folder Drive() => F(@"C:\",
        One("pagefile.sys", 16 * GB),
        One("hiberfil.sys", 13_632_000_000),
        One("swapfile.sys", 256 * MB),
        One("DumpStack.log.tmp", 8 * KB),
        F("Windows",
            F("System32", Many(5200, 6_400 * MB, ".dll", ".dll", ".dll", ".exe", ".mui", ".sys", ".winmd"),
                F("DriverStore", new Spread(420, 14, 3_900 * MB, (r, i) => $"{Tech(r).ToLowerInvariant()}.inf_amd64_{Hex(r, 16)}", [".sys", ".inf", ".dll", ".cat"])),
                F("drivers", Many(640, 180 * MB, ".sys")),
                F("config", Many(60, 210 * MB, ".log", ".blf", ".regtrans-ms"))),
            F("WinSxS", new Spread(1800, 9, 9_600 * MB, (r, i) => $"amd64_microsoft-windows-{Tech(r).ToLowerInvariant()}_31bf3856ad364e35_10.0.26100.{r.Next(1, 7000)}_none_{Hex(r, 16)}", [".dll", ".dll", ".exe", ".mui", ".manifest"])),
            F("SysWOW64", Many(3100, 1_700 * MB, ".dll", ".dll", ".exe", ".mui")),
            F("Installer", Many(310, 3_200 * MB, ".msi", ".msp", ".msp")),
            F("SoftwareDistribution", F("Download", new Spread(24, 6, 2_500 * MB, (r, i) => Hex(r, 32), [".cab", ".esd", ".psf"]))),
            F("assembly", new Spread(160, 3, 1_250 * MB, (r, i) => $"NativeImages_v4.0.30319_64_{Tech(r)}", [".dll"])),
            F("Fonts", Many(420, 480 * MB, ".ttf", ".ttf", ".otf")),
            F("servicing", Many(900, 540 * MB, ".dll", ".mum", ".cat")),
            F("Temp", Many(180, 360 * MB, ".tmp", ".log", ".etl")),
            F("Logs", Many(260, 95 * MB, ".log", ".etl")),
            F("Prefetch", Many(240, 58 * MB, ".pf"))),
        F("Program Files",
            F("Fabrikam Office", F("root", F("Office16", Many(2200, 3_900 * MB, ".dll", ".exe", ".msi"))), F("Updates", Many(12, 640 * MB, ".cab"))),
            F("Northwind Photo Studio", Many(1400, 2_700 * MB, ".dll", ".exe", ".pak", ".dat"), F("Presets", Many(800, 310 * MB, ".xmp"))),
            F("Tailspin Video Editor", Many(950, 2_100 * MB, ".dll", ".exe", ".bin"), F("Effects", Many(600, 1_400 * MB, ".fx", ".png"))),
            F("WindowsApps", new Spread(140, 40, 9_800 * MB, (r, i) => $"Microsoft.{Tech(r)}_{r.Next(1, 30)}.{r.Next(0, 3000)}.0.0_x64__8wekyb3d8bbwe", [".dll", ".exe", ".pri", ".png", ".winmd"])),
            F("dotnet", F("shared", new Spread(6, 220, 820 * MB, (r, i) => $"Microsoft.NETCore.App\\{8 + i / 2}.0.{r.Next(1, 20)}", [".dll"]))),
            F("Git", Many(1300, 430 * MB, ".exe", ".dll", ".sh", ".pl")),
            F("Python313", Many(4800, 310 * MB, ".py", ".pyc", ".pyd", ".dll")),
            F("Common Files", Many(700, 1_100 * MB, ".dll", ".exe")),
            F("Windows Defender", Many(160, 410 * MB, ".dll", ".exe", ".vdm"))),
        F("Program Files (x86)",
            F("GameHub", Many(90, 650 * MB, ".dll", ".exe"),
                F("library",
                    F("common",
                        F("Starfall Odyssey", F("Content", F("Paks", Many(36, 61 * GB, ".pak", ".pak", ".utoc", ".ucas")), F("Movies", Many(80, 6_800 * MB, ".bk2"))), Many(30, 420 * MB, ".dll", ".exe")),
                        F("Iron Harbor", F("data", Many(240, 38_200 * MB, ".bundle", ".assets", ".resource")), Many(40, 900 * MB, ".dll", ".exe")),
                        F("Pixel Garden", Many(320, 2_300 * MB, ".pck", ".ogg", ".png"))),
                    F("shadercache", new Spread(40, 6, 3_100 * MB, (r, i) => r.Next(100000, 2000000).ToString(), [".bin", ".dat"])))),
            F("Northwind Browser", F("Application", Many(160, 720 * MB, ".dll", ".exe", ".pak", ".bin"))),
            F("Microsoft", F("EdgeUpdate", Many(40, 60 * MB, ".exe", ".dll")))),
        F("Users",
            F("Alex",
                F("Videos",
                    F("Vacation 2026", Many(46, 21_500 * MB, ".mp4", ".mp4", ".mov")),
                    F("Family", Many(120, 11_200 * MB, ".mp4")),
                    F("Captures", Many(38, 4_700 * MB, ".mp4"))),
                F("Pictures",
                    F("Camera Roll", F("2025", Many(3400, 9_800 * MB, ".jpg", ".heic", ".jpg")), F("2026", Many(2900, 8_700 * MB, ".jpg", ".heic", ".mp4"))),
                    F("Screenshots", Many(640, 820 * MB, ".png")),
                    F("Wallpapers", Many(90, 410 * MB, ".jpg", ".png"))),
                F("Documents",
                    F("Virtual Machines", F("Dev box", One("Dev box.vhdx", 31_800 * MB), One("Dev box.vmcx", 84 * KB))),
                    F("Taxes", Many(70, 160 * MB, ".pdf")),
                    F("Work", Many(520, 1_900 * MB, ".docx", ".xlsx", ".pptx", ".pdf")),
                    F("Recipes", Many(140, 95 * MB, ".pdf", ".docx", ".jpg")),
                    Many(60, 120 * MB, ".pdf", ".docx", ".txt")),
                F("Downloads",
                    One("ubuntu-26.04-desktop-amd64.iso", 6_480 * MB),
                    One("Tailspin Video Editor Setup.exe", 1_350 * MB),
                    One("photos-backup.zip", 3_900 * MB),
                    Many(310, 2_200 * MB, ".exe", ".msi", ".zip", ".pdf", ".jpg")),
                F("Music", new Spread(48, 12, 8_900 * MB, (r, i) => $"{Word(r, Artists)} - {Word(r, Albums)}", [".flac", ".mp3"])),
                F("Desktop", Many(40, 260 * MB, ".lnk", ".pdf", ".png", ".docx")),
                F("dev",
                    F("web-shop", F("node_modules", new Spread(1100, 18, 1_650 * MB, (r, i) => Package(r, i), [".js", ".js", ".json", ".map", ".ts", ".md"])), Many(220, 24 * MB, ".ts", ".tsx", ".json", ".css")),
                    F("photo-tools", F(".git", Many(900, 410 * MB, ".pack", ".idx", "")), Many(300, 12 * MB, ".cs", ".csproj", ".md"))),
                F("AppData",
                    F("Local",
                        F("Temp", Many(2600, 4_300 * MB, ".tmp", ".log", ".zip", ".msi", ".exe")),
                        F("Packages", new Spread(90, 30, 3_100 * MB, (r, i) => $"Microsoft.{Tech(r)}_8wekyb3d8bbwe", [".dat", ".db", ".json", ".png"])),
                        F("Northwind Browser", F("User Data", F("Default", F("Cache", Many(8200, 2_600 * MB, "")), Many(120, 380 * MB, ".db", ".log", ".json")))),
                        F("GameHub", F("htmlcache", Many(1900, 690 * MB, ""))),
                        F("Microsoft", Many(800, 1_100 * MB, ".dat", ".db", ".log"))),
                    F("Roaming", F("Tailspin Video Editor", Many(200, 2_400 * MB, ".cache", ".json")), Many(400, 520 * MB, ".json", ".db", ".dat"))),
                One("NTUSER.DAT", 6 * MB)),
            F("Public", F("Documents", Many(12, 2 * MB, ".ini")), F("Pictures", Many(30, 140 * MB, ".jpg"))),
            F("Default", Many(20, 9 * MB, ".DAT", ".LOG1", ".LOG2"))),
        F("ProgramData",
            F("Package Cache", new Spread(80, 4, 3_300 * MB, (r, i) => "{" + Guid(r) + "}", [".msi", ".cab", ".exe"])),
            F("Microsoft", Many(4200, 2_700 * MB, ".dat", ".db", ".log", ".xml", ".etl")),
            F("Fabrikam", Many(240, 860 * MB, ".dat", ".log"))),
        F("$Recycle.Bin", F("S-1-5-21-4021437781-1867442380-2981556733-1001", Many(140, 2_200 * MB, ".mp4", ".zip", ".jpg", ".docx"))),
        F("Recovery", F("WindowsRE", One("Winre.wim", 1_006 * MB), One("boot.sdi", 3 * MB))),
        F("System Volume Information", Many(18, 5_800 * MB, ""), One("tracking.log", 20 * KB)));

    /// <summary>The made-up drive, scanned.</summary>
    public static ScanResult Create()
    {
        var random = new Random(2026);
        var tally = new Tally();
        var spec = Drive();
        var root = new FolderNode(spec.Name, null) { Files = [] };
        foreach (var part in spec.Parts) Build(root, part, random, tally);
        ScanResult.Total(root);
        var (largest, types) = Tally.Merge([tally]);
        return new ScanResult { Root = root, Largest = largest, Types = types, Method = ScanMethod.Sample };
    }

    private static void Build(FolderNode folder, Part part, Random random, Tally tally)
    {
        switch (part)
        {
            case Folder sub:
                // "a\b" makes a folder in a folder.
                var node = folder;
                foreach (var name in sub.Name.Split('\\')) node = Child(node, name);
                foreach (var inner in sub.Parts) Build(node, inner, random, tally);
                break;
            case OneFile file:
                AddFile(folder, file.Name, file.Size, random, tally);
                break;
            case Files files:
                var used = new HashSet<string>(folder.Files!.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                var sizes = Split(files.Total, files.Count, random);
                for (var i = 0; i < files.Count; i++)
                {
                    var extension = files.Extensions[random.Next(files.Extensions.Length)];
                    var name = Unique(FileName(extension, random, i), used);
                    AddFile(folder, name, sizes[i], random, tally);
                }
                break;
            case Spread spread:
                var shares = Split(spread.Total, spread.Folders, random);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < spread.Folders; i++)
                {
                    var name = Unique(spread.Name(random, i), names);
                    var count = Math.Max(1, spread.FilesEach + random.Next(-spread.FilesEach / 2, spread.FilesEach / 2 + 1));
                    Build(folder, F(name, Many(count, shares[i], spread.Extensions)), random, tally);
                }
                break;
        }
    }

    private static FolderNode Child(FolderNode folder, string name)
    {
        var child = folder.Folders.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (child is not null) return child;
        child = new FolderNode(name, folder) { Files = [] };
        folder.Folders.Add(child);
        return child;
    }

    private static void AddFile(FolderNode folder, string name, long size, Random random, Tally tally)
    {
        // Small files live in their file record and are counted in 8s; the rest take whole 4 KB clusters.
        var allocated = size < 700 ? (size + 7) / 8 * 8 : (size + 4095) / 4096 * 4096;
        var written = Now - TimeSpan.FromMinutes(Math.Pow(random.NextDouble(), 2.2) * 2 * 365 * 24 * 60);
        var file = new FileItem(name, folder, size, allocated, written);
        folder.Files!.Add(file);
        folder.FileCount++;
        folder.FilesSize += size;
        folder.FilesAllocated += allocated;
        if (written > folder.LastWrite) folder.LastWrite = written;
        tally.Add(name, folder, size, allocated, written);
    }

    /// <summary>A total split into so many sizes, a few large and many small, as files are.</summary>
    private static long[] Split(long total, int count, Random random)
    {
        var weights = new double[count];
        for (var i = 0; i < count; i++)
        {
            // Normal, by Box-Muller, made log-normal.
            var normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            weights[i] = Math.Exp(normal * 1.4);
        }
        var sum = weights.Sum();
        var sizes = new long[count];
        long given = 0;
        for (var i = 0; i < count; i++)
        {
            sizes[i] = Math.Max(1, (long)(total * weights[i] / sum));
            given += sizes[i];
        }
        sizes[Array.IndexOf(weights, weights.Max())] += Math.Max(0, total - given);
        return sizes;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
            if (used.Add($"{stem} ({n}){extension}")) return $"{stem} ({n}){extension}";
    }

    private static readonly string[] TechWords =
    [
        "Core", "Shell", "Media", "Graphics", "Audio", "Network", "Storage", "Search", "Input", "Print", "Security", "Update",
        "Sync", "Data", "Render", "Codec", "Speech", "Ink", "Cloud", "Device", "Display", "Power", "Camera", "Bluetooth",
        "Accessibility", "Photos", "Store", "Calculator", "Terminal", "Widgets", "Notepad", "Paint", "Clock", "Mail",
    ];
    private static readonly string[] Titles =
    [
        "Budget", "Meeting notes", "Invoice", "Report", "Plan", "Resume", "Letter", "Receipt", "Contract", "Quote",
        "Itinerary", "Statement", "Checklist", "Proposal", "Minutes", "Inventory", "Schedule", "Notes",
    ];
    private static readonly string[] Artists = ["The Paper Lanterns", "Mira Vale", "North Coast", "Juniper Street", "Low Tide", "Ada Rook", "The Quiet Hours", "Copper Fields"];
    private static readonly string[] Albums = ["Night Ferry", "Glasshouse", "Slow Weather", "First Light", "Harbour Songs", "Paper Moons", "Long Way Round", "Kites"];
    private static readonly string[] PackageWords =
    [
        "react", "lodash", "webpack", "babel", "eslint", "typescript", "postcss", "rollup", "esbuild", "jest", "vite", "core-js",
        "chalk", "debug", "semver", "glob", "minimatch", "acorn", "terser", "picocolors", "yaml", "uuid", "ws", "zod",
    ];

    private static string Word(Random random, string[] words) => words[random.Next(words.Length)];
    private static string Tech(Random random) => Word(random, TechWords) + (random.Next(3) == 0 ? "" : "." + Word(random, TechWords));
    private static string Hex(Random random, int length) => string.Concat(Enumerable.Range(0, length).Select(_ => "0123456789abcdef"[random.Next(16)]));
    private static string Guid(Random random) => $"{Hex(random, 8)}-{Hex(random, 4)}-{Hex(random, 4)}-{Hex(random, 4)}-{Hex(random, 12)}";
    private static string Package(Random random, int i) =>
        i < PackageWords.Length ? PackageWords[i] : $"{Word(random, PackageWords)}-{Word(random, PackageWords)}";

    private static string Stamp(Random random, string prefix) =>
        prefix + (Now - TimeSpan.FromMinutes(random.Next(0, 700 * 24 * 60))).ToString("yyyyMMdd_HHmmss");

    private static string FileName(string extension, Random random, int i) => extension.ToLowerInvariant() switch
    {
        ".dll" or ".exe" or ".sys" or ".mui" or ".winmd" or ".pri" => (random.Next(4) == 0 ? "" : random.Next(2) == 0 ? "Windows." : "Microsoft.") + Tech(random) + extension,
        ".jpg" or ".heic" or ".png" => Stamp(random, "IMG_") + extension,
        ".mp4" or ".mov" => Stamp(random, "VID_") + extension,
        ".mp3" or ".flac" => $"{i % 14 + 1:00} {Word(random, Albums)}{extension}",
        ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".txt" => $"{Word(random, Titles)} {random.Next(2019, 2027)}{extension}",
        ".js" or ".json" or ".ts" or ".map" or ".md" or ".tsx" or ".css" => $"{Word(random, ["index", "utils", "parser", "types", "main", "config", "helpers", "loader", "client", "server"])}{(i < 10 ? "" : "-" + i)}{extension}",
        ".cab" or ".msi" or ".msp" or ".esd" or ".psf" => Hex(random, 12) + extension,
        ".log" or ".etl" or ".blf" => Tech(random) + "-" + i + extension,
        "" => Hex(random, 16),
        _ => $"{Word(random, ["data", "chunk", "content", "asset", "pack", "resource", "level"])}{i:000}{extension}",
    };
}
