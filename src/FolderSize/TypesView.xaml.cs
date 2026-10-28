using System.Windows;
using System.Windows.Controls;

namespace Strongthium.FolderSize.App;

public sealed partial class TypesView : UserControl
{
    /// <summary>How many types the bar names; the rest are one part.</summary>
    private const int Named = 8;

    public TypesView()
    {
        InitializeComponent();
        Columns.Fill(List, 0);
    }

    public void Load(ScanResult result)
    {
        var total = result.Types.Sum(t => t.Size);
        var lines = result.Types.Select((type, i) => new TypeLine(type, total, i < Named ? Palette.At(i) : Palette.Other)).ToList();
        List.ItemsSource = lines;

        var named = lines.Take(Named).ToList();
        var rest = lines.Skip(Named).ToList();
        var parts = named.Select(line => (line.Type.Size, line.Swatch)).ToList();
        var legend = named.ToList();
        if (rest.Count > 0)
        {
            var other = new FileTypeStat($"{rest.Count:N0} other types", rest.Sum(l => l.Type.Count), rest.Sum(l => l.Type.Size), rest.Sum(l => l.Type.Allocated));
            var line = new TypeLine(other, total, Palette.Other);
            parts.Add((other.Size, line.Swatch));
            legend.Add(line);
        }
        Legend.ItemsSource = legend;

        Bar.Children.Clear();
        Bar.ColumnDefinitions.Clear();
        foreach (var (size, swatch) in parts)
        {
            if (size <= 0) continue;
            Bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size, GridUnitType.Star) });
            var part = new Border { Background = swatch, Margin = new Thickness(0, 0, 2, 0) };
            Grid.SetColumn(part, Bar.ColumnDefinitions.Count - 1);
            Bar.Children.Add(part);
        }
    }
}
