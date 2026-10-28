using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Strongthium.FolderSize.App;

/// <summary>
/// A folder as a treemap: each folder in it a block as big as its share, coloured by the folder it's in at the top,
/// with the folders in it inside it, three levels down; a folder's own files are one paler block. Clicking a block goes
/// into that folder.
/// </summary>
public sealed class TreemapView : FrameworkElement
{
    private const int Levels = 10;
    private const double HeaderHeight = 20, Gap = 2, MinimumSide = 4;

    private sealed record Tile(Box Box, FolderNode Folder, bool IsFiles, string Label, long Size, Color Fill, int Depth)
    {
        public bool HasChildren { get; set; }
        public bool HasHeading { get; set; }
    }

    private readonly List<Tile> tiles = [];
    private FolderNode? folder;
    private Tile? hovered;
    private Size laidOutFor;

    /// <summary>A block was clicked: the folder it is, or the folder whose files it is.</summary>
    public event Action<FolderNode>? FolderClicked;
    /// <summary>The block under the mouse changed (null: none).</summary>
    public event Action<FolderNode?, bool, long>? Hovered;

    public TreemapView()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    public FolderNode? Folder
    {
        get => folder;
        set
        {
            folder = value;
            Refresh();
        }
    }

    /// <summary>Lays it out again: the folder's sizes changed.</summary>
    public void Refresh()
    {
        laidOutFor = default;
        hovered = null;
        InvalidateVisual();
    }

    /// <summary>The block under a point, deepest first.</summary>
    public (FolderNode Folder, bool IsFiles)? At(Point point) =>
        TileAt(point) is { } tile ? (tile.Folder, tile.IsFiles) : null;

    private Tile? TileAt(Point point)
    {
        for (var i = tiles.Count - 1; i >= 0; i--)
            if (tiles[i].Box.Contains(point.X, point.Y)) return tiles[i];
        return null;
    }

    private void Layout()
    {
        tiles.Clear();
        laidOutFor = RenderSize;
        if (folder is null || ActualWidth < 10 || ActualHeight < 10) return;
        Place(folder, new Box(0, 0, ActualWidth, ActualHeight), 0, null);
    }

    private void Place(FolderNode node, Box box, int depth, Color? inherited)
    {
        var parts = node.Folders.Where(f => f.Size > 0).OrderByDescending(f => f.Size)
            .Select(f => (Folder: f, IsFiles: false, Size: f.Size)).ToList();
        if (node.FilesSize > 0)
        {
            var at = parts.FindIndex(p => p.Size < node.FilesSize);
            parts.Insert(at < 0 ? parts.Count : at, (node, true, node.FilesSize));
        }
        var boxes = Treemap.Squarify(parts.Select(p => (double)p.Size).ToList(), box);

        for (var i = 0; i < parts.Count; i++)
        {
            var (part, isFiles, size) = parts[i];
            var outer = boxes[i].Inset(Gap / 2, Gap / 2, Gap / 2, Gap / 2);
            if (outer.Width < MinimumSide || outer.Height < MinimumSide) continue;
            var color = inherited is { } parent ? Palette.Lighten(parent, depth == 1 ? 0.22 : 0.12) : Palette.At(i);
            if (isFiles) color = Palette.Lighten(color, 0.45);
            var label = isFiles ? (part.FileCount == 1 ? "1 file" : $"{Sizes.Count(part.FileCount)} files") : part.Name;
            var tile = new Tile(outer, part, isFiles, label, size, color, depth);
            tiles.Add(tile);

            // The folders in it: under a heading where there's room for one, or else just inside it.
            if (isFiles || depth + 1 >= Levels || part.Folders.Count == 0) continue;
            var before = tiles.Count;
            tile.HasHeading = outer.Width > 48 && outer.Height > HeaderHeight + 28;
            if (tile.HasHeading) Place(part, outer.Inset(3, HeaderHeight, 3, 3), depth + 1, color);
            else if (outer.Width > 14 && outer.Height > 14) Place(part, outer.Inset(3, 3, 3, 3), depth + 1, color);
            tile.HasChildren = tiles.Count > before;
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        // Something to hit everywhere, for the mouse.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (laidOutFor != RenderSize) Layout();
        if (tiles.Count == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dark = Palette.Brush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        var muted = Palette.Brush(Color.FromArgb(0xB0, 0x1B, 0x1B, 0x1B));
        var font = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(font.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var edge = new Pen(Palette.Brush(Color.FromArgb(0x30, 0, 0, 0)), 1);
        edge.Freeze();

        foreach (var tile in tiles)
        {
            var rect = new Rect(tile.Box.X, tile.Box.Y, tile.Box.Width, tile.Box.Height);
            var radius = Math.Min(3, Math.Min(rect.Width, rect.Height) / 6);
            drawing.DrawRoundedRectangle(Palette.Brush(tile.Fill), edge, rect, radius, radius);

            var maxWidth = rect.Width - 10;
            if (maxWidth < 24 || rect.Height < 16 || (tile.HasChildren && !tile.HasHeading)) continue;
            var name = Text(tile.Label, tile.Depth == 0 ? bold : font, 12, dark, maxWidth, dpi);
            if (tile.HasChildren)
            {
                // A heading over the folders in it.
                drawing.DrawText(name, new Point(rect.X + 5, rect.Y + 3));
                var size = Text(Sizes.Format(tile.Size), font, 11, muted, maxWidth - name.Width - 8, dpi);
                if (name.Width + size.Width + 18 < rect.Width) drawing.DrawText(size, new Point(rect.Right - size.Width - 6, rect.Y + 4));
            }
            else if (rect.Height >= 34)
            {
                drawing.DrawText(name, new Point(rect.X + 5, rect.Y + 4));
                var size = Text(Sizes.Format(tile.Size), font, 11, muted, maxWidth, dpi);
                drawing.DrawText(size, new Point(rect.X + 5, rect.Y + 5 + name.Height));
            }
            else drawing.DrawText(name, new Point(rect.X + 5, rect.Y + (rect.Height - name.Height) / 2));
        }

        if (hovered is not null)
        {
            var outline = new Pen((Brush)FindResource("TextFillColorPrimaryBrush"), 2);
            var b = hovered.Box;
            drawing.DrawRoundedRectangle(null, outline, new Rect(b.X + 1, b.Y + 1, Math.Max(0, b.Width - 2), Math.Max(0, b.Height - 2)), 3, 3);
        }
    }

    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double maxWidth, double dpi)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, dpi)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return formatted;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = TileAt(e.GetPosition(this));
        if (tile == hovered) return;
        hovered = tile;
        InvalidateVisual();
        Hovered?.Invoke(tile?.Folder, tile?.IsFiles ?? false, tile?.Size ?? 0);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hovered is null) return;
        hovered = null;
        InvalidateVisual();
        Hovered?.Invoke(null, false, 0);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (TileAt(e.GetPosition(this)) is { } tile && tile.Folder != folder) FolderClicked?.Invoke(tile.Folder);
    }
}
