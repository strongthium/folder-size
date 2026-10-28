using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Strongthium.FolderSize.App;

/// <summary>The colours of the treemap's blocks and of the file types, soft enough for dark writing on them in light
/// and dark mode alike.</summary>
internal static class Palette
{
    public static readonly Color[] Colors =
    [
        Color.FromRgb(0x6C, 0xA8, 0xE0),   // blue
        Color.FromRgb(0xF0, 0x9A, 0x5E),   // orange
        Color.FromRgb(0x7F, 0xC0, 0x7A),   // green
        Color.FromRgb(0xF2, 0xC9, 0x4C),   // gold
        Color.FromRgb(0xB3, 0x92, 0xD8),   // purple
        Color.FromRgb(0x5E, 0xC4, 0xC0),   // teal
        Color.FromRgb(0xEC, 0x86, 0xA2),   // rose
        Color.FromRgb(0xA4, 0xB4, 0xC6),   // slate
        Color.FromRgb(0xBF, 0xD1, 0x6A),   // lime
        Color.FromRgb(0xE8, 0xB0, 0x86),   // peach
    ];

    /// <summary>What isn't one of the named ones.</summary>
    public static readonly Color Other = Color.FromRgb(0xB8, 0xB8, 0xB8);

    public static Color At(int index) => Colors[index % Colors.Length];

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Towards white by <paramref name="amount"/> (0 to 1).</summary>
    public static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * amount), (byte)(color.G + (255 - color.G) * amount), (byte)(color.B + (255 - color.B) * amount));

    /// <summary>Towards black by <paramref name="amount"/> (0 to 1).</summary>
    public static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)), (byte)(color.G * (1 - amount)), (byte)(color.B * (1 - amount)));
}

/// <summary>A share, 0 to 1, as two column widths: the filled part of a bar and the rest.</summary>
public abstract class Shared
{
    public double Share { get; protected init; }
    public string ShareText => Sizes.Percent(Share);
    public GridLength ShareFill => new(Math.Clamp(Share, 0, 1), GridUnitType.Star);
    public GridLength ShareRest => new(1 - Math.Clamp(Share, 0, 1), GridUnitType.Star);

    protected static double Of(long part, long whole) => whole > 0 ? (double)part / whole : 0;
    protected static string Date(DateTime utc) => utc == DateTime.MinValue ? "" : utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}

public enum RowKind
{
    Folder,
    /// <summary>A folder's own files, together: "[24 files]".</summary>
    Files,
    File,
}

/// <summary>A line of the folder tree: a folder, a folder's files together, or one of those files.</summary>
public sealed class Row : Shared, INotifyPropertyChanged
{
    private bool isExpanded;

    private Row(RowKind kind, int depth)
    {
        Kind = kind;
        Depth = depth;
    }

    public RowKind Kind { get; }
    public int Depth { get; }
    public FolderNode? Folder { get; private init; }
    public FileItem? File { get; private init; }

    public required string Name { get; init; }
    public long Size { get; private init; }
    public long Allocated { get; private init; }
    public long FileCount { get; private init; }
    public long FolderCount { get; private init; }
    public DateTime LastWrite { get; private init; }

    public string SizeText => Sizes.Format(Size);
    public string AllocatedText => Sizes.Format(Allocated);
    public string FilesText => Kind == RowKind.File ? "" : Sizes.Count(FileCount);
    public string FoldersText => Kind == RowKind.Folder ? Sizes.Count(FolderCount) : "";
    public string ModifiedText => Date(LastWrite);
    public string? Note { get; private init; }

    public Thickness Indent => new(Depth * 20, 0, 0, 0);
    public bool CanExpand { get; private init; }
    public bool IsFolder => Kind == RowKind.Folder;
    public bool IsFiles => Kind != RowKind.Folder;
    public string FileGlyph => Kind == RowKind.Files ? "" : "";   // ViewAll, Page

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chevron)));
        }
    }

    public string Chevron => IsExpanded ? "" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static Row ForFolder(FolderNode folder, int depth) => new(RowKind.Folder, depth)
    {
        Folder = folder,
        Name = folder.Name,
        Size = folder.Size,
        Allocated = folder.Allocated,
        FileCount = folder.TotalFiles,
        FolderCount = folder.TotalFolders,
        LastWrite = folder.LastWrite,
        Share = folder.Parent is { } parent ? Of(folder.Size, parent.Size) : 1,
        CanExpand = folder.Folders.Count > 0 || folder.FileCount > 0,
        Note = folder.Unreadable ? "couldn't be read" : null,
    };

    public static Row ForFiles(FolderNode folder, int depth) => new(RowKind.Files, depth)
    {
        Folder = folder,
        Name = folder.FileCount == 1 ? "[1 file]" : $"[{Sizes.Count(folder.FileCount)} files]",
        Size = folder.FilesSize,
        Allocated = folder.FilesAllocated,
        FileCount = folder.FileCount,
        LastWrite = DateTime.MinValue,
        Share = Of(folder.FilesSize, folder.Size),
        CanExpand = folder.FileCount > 0,
    };

    public static Row ForFile(FileItem file, int depth) => new(RowKind.File, depth)
    {
        Folder = file.Folder,
        File = file,
        Name = file.Name,
        Size = file.Size,
        Allocated = file.Allocated,
        LastWrite = file.LastWrite,
        Share = Of(file.Size, file.Folder.Size),
    };

    public string? Path => File?.Path ?? Folder?.Path;
}

/// <summary>A line of the largest files.</summary>
public sealed class FileLine : Shared
{
    public FileLine(FileItem file, long total, int rank)
    {
        File = file;
        Rank = rank;
        Share = Of(file.Size, total);
    }

    public FileItem File { get; }
    public int Rank { get; }
    public string Name => File.Name;
    public string FolderPath => File.Folder.Path;
    public string SizeText => Sizes.Format(File.Size);
    public string AllocatedText => Sizes.Format(File.Allocated);
    public string ModifiedText => Date(File.LastWrite);
}

/// <summary>A line of the file types.</summary>
public sealed class TypeLine : Shared
{
    public TypeLine(FileTypeStat type, long total, Color color)
    {
        Type = type;
        Swatch = Palette.Brush(color);
        Share = Of(type.Size, total);
    }

    public FileTypeStat Type { get; }
    public string Name => Extensions.Describe(Type.Extension);
    public Brush Swatch { get; }
    public string CountText => Sizes.Count(Type.Count);
    public string SizeText => Sizes.Format(Type.Size);
    public string AllocatedText => Sizes.Format(Type.Allocated);
    public string AverageText => Sizes.Format(Type.Count > 0 ? Type.Size / Type.Count : 0);
}
