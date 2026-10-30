using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Strongthium.FolderSize.App;

/// <summary>A drive down the side, and how full it is.</summary>
public sealed class Place(string root, string name, string glyph, long total, long free) : INotifyPropertyChanged
{
    private bool isChosen;

    public string Root { get; } = root;
    public string Name { get; } = name;
    public string Glyph { get; } = glyph;
    public double Used { get; } = total > 0 ? (double)(total - free) / total : 0;
    public GridLength UsedFill => new(Used, GridUnitType.Star);
    public GridLength UsedRest => new(1 - Used, GridUnitType.Star);
    public string FreeText { get; } = $"{Sizes.Format(free)} free of {Sizes.Format(total)}";

    public bool IsChosen
    {
        get => isChosen;
        set
        {
            if (isChosen == value) return;
            isChosen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChosen)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static List<Place> OnThisComputer()
    {
        var places = new List<Place>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;
                var label = drive.VolumeLabel is { Length: > 0 } named ? named : drive.DriveType switch
                {
                    DriveType.Removable => "USB Drive",
                    DriveType.Network => "Network Drive",
                    _ => "Local Disk",
                };
                var glyph = drive.DriveType switch
                {
                    DriveType.Removable => "",   // USB
                    DriveType.Network => "",     // MapDrive
                    _ => "",                     // HardDrive
                };
                places.Add(new Place(drive.RootDirectory.FullName, $"{label} ({drive.Name.TrimEnd('\\')})", glyph, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (IOException) { }   // gone while being looked at
            catch (UnauthorizedAccessException) { }
        }
        return places;
    }
}

public sealed partial class MainWindow : Window
{
    private ScanResult? result;
    private string? scannedPath;
    private CancellationTokenSource? scanning;
    private ScanProgress? progress;
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private List<Place> places = [];
    private Window? about;

    public MainWindow()
    {
        InitializeComponent();
        Logo.Source = App.Icon;
        Icon = App.Icon;
        AdminCard.Visibility = Program.IsAdministrator ? Visibility.Collapsed : Visibility.Visible;
        AdminNote.Visibility = Program.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        ticker.Tick += (_, _) => ShowProgress();
        Treemap.FolderClicked += ShowInTreemap;
        Treemap.Hovered += (folder, isFiles, size) => TreemapHover.Text = folder is null ? ""
            : $"{(isFiles ? $"Files in {folder.Name}" : folder.Path)}  ·  {Sizes.Format(size)}";
        Treemap.MouseRightButtonUp += Treemap_MouseRightButtonUp;
        Activated += (_, _) => { if (!Program.Sample && scanning is null) LoadPlaces(); };
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        if (Program.Sample)
        {
            var sample = SampleData.Create();
            places = [.. SampleData.Drives(sample).Select(d => new Place(d.Root, $"{d.Label} ({d.Root.TrimEnd('\\')})", "", d.Total, d.Free))];
            Drives.ItemsSource = places;
            scannedPath = sample.Root.Path;
            Show(sample);
        }
        else
        {
            LoadPlaces();
            if (Program.StartPath is { } path) Scan(path);
        }
        if (Program.ShowAbout) About_Click(this, new RoutedEventArgs());
        if (result is null) return;
        if (Program.Expand is { } expand) FoldersPage.Open(expand);
        var tab = Program.StartView switch
        {
            "treemap" => TreemapTab,
            "largest" => LargestTab,
            "types" => TypesTab,
            _ => FoldersTab,
        };
        tab.IsChecked = true;
    }

    private void LoadPlaces()
    {
        places = Place.OnThisComputer();
        foreach (var place in places) place.IsChosen = IsSameFolder(place.Root, scannedPath);
        Drives.ItemsSource = places;
    }

    private static bool IsSameFolder(string a, string? b) =>
        b is not null && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    // ---- Scanning ------------------------------------------------------------------------------------------------

    public async void Scan(string path)
    {
        if (Program.Sample) return;
        scanning?.Cancel();
        var cancel = scanning = new CancellationTokenSource();
        var thisProgress = progress = new ScanProgress();
        scannedPath = Path.GetFullPath(path);
        foreach (var place in places) place.IsChosen = IsSameFolder(place.Root, scannedPath);

        PageTitle.Text = DisplayName(scannedPath);
        PathText.Text = scannedPath;
        Welcome.Visibility = Totals.Visibility = Visibility.Collapsed;
        Scanning.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Visible;
        RescanButton.Visibility = UpButton.Visibility = Visibility.Collapsed;
        ScanHeading.Text = "Scanning…";
        ScanCurrent.Text = "";
        ScanBar.IsIndeterminate = true;
        clock.Restart();
        ticker.Start();

        try
        {
            var found = await Task.Run(() => Scanner.Scan(scannedPath, thisProgress, cancel.Token));
            if (cancel != scanning) return;
            Show(found);
        }
        catch (OperationCanceledException)
        {
            if (cancel != scanning) return;
            ShowStopped("Stopped. Scan again to see the whole of it.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (cancel != scanning) return;
            ShowStopped(exception.Message);
        }
        finally
        {
            if (cancel == scanning)
            {
                scanning = null;
                ticker.Stop();
            }
            cancel.Dispose();
        }
    }

    private void ShowProgress()
    {
        if (progress is null) return;
        ScanElapsed.Text = Sizes.Duration(clock.Elapsed);
        var counted = $"{Sizes.Count(progress.Files)} files, {Sizes.Format(progress.Bytes)}";
        if (progress.Method == ScanMethod.FileTable)
        {
            ScanHeading.Text = $"Reading the file table…  {counted}";
            if (progress.Fraction is { } fraction)
            {
                ScanBar.IsIndeterminate = false;
                ScanBar.Value = fraction * 100;
            }
            else ScanBar.IsIndeterminate = true;
            ScanCurrent.Text = progress.Current;
        }
        else
        {
            ScanHeading.Text = $"Scanning…  {counted} in {Sizes.Count(progress.Folders)} folders";
            ScanCurrent.Text = progress.Current;
        }
    }

    private void ShowStopped(string message)
    {
        ticker.Stop();
        Scanning.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Collapsed;
        RescanButton.Visibility = Visibility.Visible;
        if (result is not null && IsSameFolder(result.Root.Path, scannedPath))
        {
            Totals.Visibility = Visibility.Visible;
            return;
        }
        Welcome.Visibility = Visibility.Visible;
        ((TextBlock)((StackPanel)Welcome).Children[0]).Text = "Not scanned";
        ((TextBlock)((StackPanel)Welcome).Children[1]).Text = message;
    }

    private void Show(ScanResult found)
    {
        result = found;
        scannedPath = found.Root.Path;
        ticker.Stop();
        PageTitle.Text = DisplayName(found.Root.Path);
        PathText.Text = found.Method == ScanMethod.Sample ? $"{found.Root.Path}  ·  made-up sample data" : found.Root.Path;
        Welcome.Visibility = Scanning.Visibility = Empty.Visibility = Visibility.Collapsed;
        Totals.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        RescanButton.Visibility = found.Method == ScanMethod.Sample ? Visibility.Collapsed : Visibility.Visible;
        UpButton.Visibility = found.Method != ScanMethod.Sample && Path.GetDirectoryName(found.Root.Path) is not null ? Visibility.Visible : Visibility.Collapsed;
        Tabs.IsEnabled = true;
        ShowTotals();

        FoldersPage.Load(found.Root);
        ShowInTreemap(found.Root);
        LargestPage.Load(found);
        TypesPage.Load(found);
        Tab_Checked(this, new RoutedEventArgs());
    }

    private void ShowTotals()
    {
        if (result is null) return;
        var root = result.Root;
        SizeStat.Text = Sizes.Format(root.Size);
        AllocatedStat.Text = Sizes.Format(root.Allocated);
        FilesStat.Text = Sizes.Count(root.TotalFiles);
        FoldersStat.Text = Sizes.Count(root.TotalFolders);
        switch (result.Method)
        {
            case ScanMethod.Sample:
                TimeStat.Text = "Sample";
                MethodText.Text = "made-up data: nothing was scanned";
                break;
            case ScanMethod.FileTable:
                TimeStat.Text = Sizes.Duration(result.Elapsed);
                MethodText.Text = "read from the drive's file table";
                break;
            default:
                TimeStat.Text = Sizes.Duration(result.Elapsed);
                MethodText.Text = result.Unreadable == 0 ? "every folder read"
                    : result.Unreadable == 1 ? "1 folder couldn't be read" : $"{Sizes.Count(result.Unreadable)} folders couldn't be read";
                break;
        }
    }

    /// <summary>"Local Disk (C:)" for a drive's root, or else the folder's name.</summary>
    private string DisplayName(string path)
    {
        if (places.Find(p => IsSameFolder(p.Root, path)) is { } place) return place.Name;
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;
    }

    // ---- Buttons -------------------------------------------------------------------------------------------------

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Place place) return;
        if (Program.Sample || (IsSameFolder(place.Root, scannedPath) && (scanning is not null || result is not null))) return;
        Scan(place.Root);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan" };
        if (dialog.ShowDialog(this) == true) Scan(dialog.FolderName);
    }

    /// <summary>In a window of its own, so that what's scanned stays on the screen.</summary>
    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (about is null)
        {
            about = new Window { Title = "About Folder Size", Icon = App.Icon, Owner = this, Width = 900, Height = 780, MinWidth = 560, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new AboutPage() };
            about.Closed += (_, _) => about = null;
            about.Show();
        }
        else about.Activate();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => scanning?.Cancel();

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (scannedPath is not null) Scan(scannedPath);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (scannedPath is not null && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(scannedPath)) is { } up) Scan(up);
    }

    private void RunAsAdministrator_Click(object sender, RoutedEventArgs e)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        if (Program.Sample) start.ArgumentList.Add("--sample");
        else if (scannedPath is not null) start.ArgumentList.Add(scannedPath);
        try
        {
            Process.Start(start);
            Close();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Windows asked, and the answer was no.
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (FoldersPage is null || result is null) return;
        FoldersPage.Visibility = FoldersTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TreemapPage.Visibility = TreemapTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LargestPage.Visibility = LargestTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TypesPage.Visibility = TypesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- The treemap ---------------------------------------------------------------------------------------------

    private void ShowInTreemap(FolderNode folder)
    {
        Treemap.Folder = folder;
        TreemapPath.Text = $"{folder.Path}  ·  {Sizes.Format(folder.Size)}";
        TreemapUp.IsEnabled = folder.Parent is not null;
    }

    private void TreemapUp_Click(object sender, RoutedEventArgs e)
    {
        if (Treemap.Folder?.Parent is { } parent) ShowInTreemap(parent);
    }

    private void Treemap_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Treemap.At(e.GetPosition(Treemap)) is not { } hit) return;
        e.Handled = true;
        ShowMenu(Treemap, hit.Folder, null, filesOnly: hit.IsFiles);
    }

    // ---- What can be done with a folder or a file ----------------------------------------------------------------

    /// <summary>The menu for a folder, a folder's files (<paramref name="filesOnly"/>) or a file.</summary>
    public void ShowMenu(UIElement target, FolderNode folder, FileItem? file, bool filesOnly = false)
    {
        var path = file?.Path ?? folder.Path;
        var real = result?.Method != ScanMethod.Sample;
        var menu = new ContextMenu { PlacementTarget = target };

        Add(file is null ? "Open" : "Open", "", () => Shell.Open(file is null ? folder.Path : path), real);
        Add("Show in Explorer", "", () => Shell.ShowInExplorer(path), real);
        Add("Copy path", "", () => Shell.CopyPath(path), true);
        if (file is null)
        {
            menu.Items.Add(new Separator());
            Add("Show in the treemap", "", () =>
            {
                ShowInTreemap(folder);
                TreemapTab.IsChecked = true;
            }, !filesOnly && folder.Size > 0);
            Add("Scan this folder", "", () => Scan(folder.Path), real && !filesOnly && folder.Parent is not null);
        }
        if (!filesOnly)
        {
            menu.Items.Add(new Separator());
            Add("Move to the Recycle Bin…", "", () => Recycle(folder, file), real && (file is not null || folder.Parent is not null));
        }
        menu.IsOpen = true;

        void Add(string header, string glyph, Action action, bool enabled)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 14 },
                IsEnabled = enabled,
            };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    private void Recycle(FolderNode folder, FileItem? file)
    {
        if (result is null) return;
        var name = file?.Name ?? folder.Name;
        var what = file is not null ? Sizes.Format(file.Size)
            : $"{Sizes.Format(folder.Size)} in {Sizes.Count(folder.TotalFiles)} files and {Sizes.Count(folder.TotalFolders)} folders";
        var answer = MessageBox.Show(this, $"Move \"{name}\" to the Recycle Bin?\n\n{what}", "Folder Size",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;

        if (!RecycleBin.Move(file?.Path ?? folder.Path, new WindowInteropHelper(this).Handle)) return;
        var parent = folder.Parent;
        if (file is not null)
        {
            result.Remove(file);
            FoldersPage.Forget(file.Folder);
        }
        else result.Remove(folder);

        ShowTotals();
        FoldersPage.Refresh();
        if (Treemap.Folder is { } shown && !shown.IsUnder(result.Root)) ShowInTreemap(parent ?? result.Root);
        else Treemap.Refresh();
        LargestPage.Load(result);
        LoadPlaces();
    }
}
