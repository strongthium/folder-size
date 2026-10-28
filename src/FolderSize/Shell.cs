using System.Diagnostics;
using System.Windows;

namespace Strongthium.FolderSize.App;

/// <summary>What Explorer does with a path: open it, or show it in its folder.</summary>
internal static class Shell
{
    public static void Open(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void ShowInExplorer(string path) => Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));

    public static void CopyPath(string path)
    {
        try { Clipboard.SetText(path); }
        catch (System.Runtime.InteropServices.COMException) { }   // another program has the clipboard open
    }

    private static void Start(ProcessStartInfo start)
    {
        try { Process.Start(start); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(exception.Message, "Folder Size", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

/// <summary>Gives one column of a list what the others leave, so that the list never needs to scroll sideways.</summary>
internal static class Columns
{
    public static void Fill(System.Windows.Controls.ListView list, int column)
    {
        void Fit()
        {
            if (list.View is not System.Windows.Controls.GridView view || list.ActualWidth <= 0) return;
            var others = view.Columns.Where((_, i) => i != column).Sum(c => c.ActualWidth);
            // Room for the scroll bar and the list's own padding.
            view.Columns[column].Width = Math.Max(160, list.ActualWidth - others - 36);
        }
        list.SizeChanged += (_, _) => Fit();
        list.Loaded += (_, _) => Fit();
    }
}
