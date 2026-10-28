using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Strongthium.FolderSize.App;

/// <summary>A list that can take or lose many lines at once, telling the list on screen once.</summary>
public sealed class RowList : ObservableCollection<Row>
{
    public void Reset(IEnumerable<Row> rows)
    {
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        Changed();
    }

    public void InsertRange(int index, IReadOnlyList<Row> rows)
    {
        for (var i = 0; i < rows.Count; i++) Items.Insert(index + i, rows[i]);
        Changed();
    }

    public void RemoveRange(int index, int count)
    {
        for (var i = 0; i < count; i++) Items.RemoveAt(index);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public sealed partial class FoldersView : UserControl
{
    private readonly RowList rows = [];
    // What's open, so that sorting again opens the same.
    private readonly HashSet<FolderNode> expanded = [], filesExpanded = [];
    private readonly Dictionary<FolderNode, List<FileItem>> files = [];
    private FolderNode? root;
    private string sortBy = "Size";
    private bool descending = true;

    public FoldersView()
    {
        InitializeComponent();
        List.ItemsSource = rows;
        Columns.Fill(List, 0);
    }

    private MainWindow Main => (MainWindow)Window.GetWindow(this);

    public Row? Selected => List.SelectedItem as Row;

    public void Load(FolderNode folder)
    {
        root = folder;
        expanded.Clear();
        filesExpanded.Clear();
        files.Clear();
        expanded.Add(folder);
        rows.Reset(Lines());
        List.SelectedIndex = 0;
    }

    /// <summary>Makes the lines again from the tree, keeping what's open and what's chosen: sizes changed, or the order.</summary>
    public void Refresh()
    {
        var chosen = Selected?.Path;
        rows.Reset(Lines());
        if (chosen is not null) Select(rows.FirstOrDefault(r => r.Path == chosen));
    }

    /// <summary>Forgets a folder's files as read, once one of them is gone.</summary>
    public void Forget(FolderNode folder) => files.Remove(folder);

    /// <summary>Opens the folders down a path under the root ("Users\Alex") and chooses the last.</summary>
    public void Open(string path)
    {
        if (root is null) return;
        var node = root;
        foreach (var name in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = node.Folders.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (next is null) break;
            expanded.Add(node);
            node = next;
        }
        expanded.Add(node);
        rows.Reset(Lines());
        Select(rows.FirstOrDefault(r => r.Kind == RowKind.Folder && r.Folder == node));
    }

    private void Select(Row? row)
    {
        if (row is null) return;
        List.SelectedItem = row;
        List.ScrollIntoView(row);
    }

    private List<Row> Lines()
    {
        var lines = new List<Row>();
        if (root is not null) AddFolder(lines, root, 0);
        return lines;
    }

    private void AddFolder(List<Row> lines, FolderNode folder, int depth)
    {
        var row = Row.ForFolder(folder, depth);
        row.IsExpanded = expanded.Contains(folder) && row.CanExpand;
        lines.Add(row);
        if (row.IsExpanded) AddInside(lines, folder, depth + 1);
    }

    /// <summary>The lines in an open folder: its folders and its files together, in order.</summary>
    private void AddInside(List<Row> lines, FolderNode folder, int depth)
    {
        var inside = folder.Folders.Select(f => Row.ForFolder(f, depth)).ToList();
        if (folder.FileCount > 0) inside.Add(Row.ForFiles(folder, depth));
        Sort(inside);
        foreach (var row in inside)
        {
            if (row.Kind == RowKind.Folder) AddFolder(lines, row.Folder!, depth);
            else
            {
                row.IsExpanded = filesExpanded.Contains(folder);
                lines.Add(row);
                if (row.IsExpanded) lines.AddRange(FileRows(folder, depth + 1));
            }
        }
    }

    private List<Row> FileRows(FolderNode folder, int depth)
    {
        if (!files.TryGetValue(folder, out var list))
        {
            using (new WaitCursor()) list = files[folder] = FolderFiles.Read(folder);
        }
        var lines = list.Select(f => Row.ForFile(f, depth)).ToList();
        Sort(lines);
        return lines;
    }

    private void Sort(List<Row> lines)
    {
        // A folder's files together go last when sorting by name.
        Comparison<Row> compare = sortBy switch
        {
            "Name" => (a, b) => a.Kind != b.Kind && (a.Kind == RowKind.Files || b.Kind == RowKind.Files)
                ? (a.Kind == RowKind.Files ? 1 : -1) * (descending ? -1 : 1)
                : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name),
            "Allocated" => (a, b) => a.Allocated.CompareTo(b.Allocated),
            "Files" => (a, b) => a.FileCount.CompareTo(b.FileCount),
            "Folders" => (a, b) => a.FolderCount.CompareTo(b.FolderCount),
            "Modified" => (a, b) => a.LastWrite.CompareTo(b.LastWrite),
            _ => (a, b) => a.Size.CompareTo(b.Size),
        };
        lines.Sort(descending ? (a, b) => compare(b, a) : compare);
    }

    private void Toggle(Row row)
    {
        if (!row.CanExpand || row.Folder is null) return;
        var index = rows.IndexOf(row);
        if (index < 0) return;
        if (row.IsExpanded)
        {
            var end = index + 1;
            while (end < rows.Count && rows[end].Depth > row.Depth) end++;
            if (row.Kind == RowKind.Folder) expanded.Remove(row.Folder);
            else filesExpanded.Remove(row.Folder);
            row.IsExpanded = false;
            rows.RemoveRange(index + 1, end - index - 1);
        }
        else
        {
            var inside = new List<Row>();
            if (row.Kind == RowKind.Folder)
            {
                expanded.Add(row.Folder);
                AddInside(inside, row.Folder, row.Depth + 1);
            }
            else
            {
                filesExpanded.Add(row.Folder);
                inside.AddRange(FileRows(row.Folder, row.Depth + 1));
            }
            row.IsExpanded = true;
            rows.InsertRange(index + 1, inside);
        }
        Select(row);
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row row) Toggle(row);
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Row row && row.Kind != RowKind.File) Toggle(row);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (Selected is not { } row) return;
        switch (e.Key)
        {
            case Key.Right when row.CanExpand && !row.IsExpanded:
            case Key.Left when row.IsExpanded:
            case Key.Enter when row.CanExpand:
                Toggle(row);
                e.Handled = true;
                break;
            case Key.Left when row.Depth > 0:
                // Up to the folder it's in.
                var index = rows.IndexOf(row);
                while (index > 0 && rows[index].Depth >= row.Depth) index--;
                Select(rows[index]);
                (List.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem)?.Focus();
                e.Handled = true;
                break;
        }
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string key }) return;
        if (key == sortBy) descending = !descending;
        else
        {
            sortBy = key;
            descending = key != "Name";
        }
        Refresh();
    }

    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not Row row) return;
        Select(row);
        Main.ShowMenu(List, row.Folder!, row.File, filesOnly: row.Kind == RowKind.Files);
    }
}

/// <summary>The wait cursor while something is read from the disk.</summary>
internal sealed class WaitCursor : IDisposable
{
    private readonly Cursor? before = Mouse.OverrideCursor;

    public WaitCursor() => Mouse.OverrideCursor = Cursors.Wait;

    public void Dispose() => Mouse.OverrideCursor = before;
}
