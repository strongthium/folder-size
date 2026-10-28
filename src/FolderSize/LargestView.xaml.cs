using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Strongthium.FolderSize.App;

public sealed partial class LargestView : UserControl
{
    public LargestView()
    {
        InitializeComponent();
        Columns.Fill(List, 4);
    }

    private MainWindow Main => (MainWindow)Window.GetWindow(this);

    public void Load(ScanResult result) =>
        List.ItemsSource = result.Largest.Select((file, i) => new FileLine(file, result.Root.Size, i + 1)).ToList();

    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not FileLine line) return;
        List.SelectedItem = line;
        Main.ShowMenu(List, line.File.Folder, line.File);
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileLine line) Shell.ShowInExplorer(line.File.Path);
    }
}
