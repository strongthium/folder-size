namespace Strongthium.FolderSize.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "folder-size-tests-" + Guid.NewGuid().ToString("N"));

    public FolderScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "photos", "2026"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        Write("photos/beach.jpg", 10_000);
        Write("photos/notes.txt", 100);
        Write("photos/2026/hike.JPG", 5_000);
        Write("readme.md", 3);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void Write(string name, int size) => File.WriteAllBytes(Path.Combine(root, name), new byte[size]);

    private static FolderNode Folder(FolderNode parent, string name) => parent.Folders.Single(f => f.Name == name);

    [Fact]
    public void AddsUpEveryFolder()
    {
        var result = FolderScanner.Scan(root, new ScanProgress(), TestContext.Current.CancellationToken);

        Assert.Equal(ScanMethod.Folders, result.Method);
        Assert.Equal(root, result.Root.Path);
        Assert.Equal(15_103, result.Root.Size);
        Assert.Equal(4, result.Root.TotalFiles);
        Assert.Equal(3, result.Root.TotalFolders);
        Assert.Equal(1, result.Root.FileCount);
        Assert.Equal(3, result.Root.FilesSize);

        var photos = Folder(result.Root, "photos");
        Assert.Equal(15_100, photos.Size);
        Assert.Equal(2, photos.FileCount);
        Assert.Equal(1, photos.TotalFolders);
        Assert.Equal(Path.Combine(root, "photos", "2026"), Folder(photos, "2026").Path);
        Assert.Equal(5_000, Folder(photos, "2026").Size);
        Assert.Equal(0, Folder(result.Root, "empty").Size);

        // NTFS keeps a file of a few bytes in its record, and counts it in 8s; the others take whole clusters.
        var allocated = result.Largest.ToDictionary(f => f.Name, f => f.Allocated);
        Assert.Equal(8, allocated["readme.md"]);
        Assert.Equal(104, allocated["notes.txt"]);
        Assert.True(allocated["beach.jpg"] >= 10_000 && allocated["beach.jpg"] % 512 == 0);
    }

    [Fact]
    public void KeepsTheLargestFilesAndTheTypes()
    {
        var result = FolderScanner.Scan(root, new ScanProgress(), TestContext.Current.CancellationToken);

        Assert.Equal(["beach.jpg", "hike.JPG", "notes.txt", "readme.md"], result.Largest.Select(f => f.Name));
        Assert.Equal(Path.Combine(root, "photos", "2026", "hike.JPG"), result.Largest[1].Path);

        var jpg = result.Types[0];
        Assert.Equal(".jpg", jpg.Extension);
        Assert.Equal(2, jpg.Count);
        Assert.Equal(15_000, jpg.Size);
        Assert.Equal([".jpg", ".txt", ".md"], result.Types.Select(t => t.Extension));
    }

    [Fact]
    public void ReadsAFoldersFilesAgain()
    {
        var result = FolderScanner.Scan(root, new ScanProgress(), TestContext.Current.CancellationToken);
        var files = FolderFiles.Read(Folder(result.Root, "photos"));
        Assert.Equal(["beach.jpg", "notes.txt"], files.Select(f => f.Name));
        Assert.Equal(10_000, files[0].Size);
    }

    [Fact]
    public void CountsProgress()
    {
        var progress = new ScanProgress();
        FolderScanner.Scan(root, progress, TestContext.Current.CancellationToken);
        Assert.Equal(4, progress.Files);
        Assert.Equal(3, progress.Folders);
        Assert.Equal(15_103, progress.Bytes);
    }

    [Fact]
    public void StopsWhenCancelled()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderScanner.Scan(root, new ScanProgress(), cancel.Token));
    }

    [Fact]
    public void FallsBackToFoldersWithoutAVolume()
    {
        // Not a drive's root, so the file table isn't read whatever the rights.
        var result = Scanner.Scan(root, new ScanProgress(), TestContext.Current.CancellationToken);
        Assert.Equal(ScanMethod.Folders, result.Method);
        Assert.Equal(15_103, result.Root.Size);
    }

    [Fact]
    public void TakesOutWhatWasRecycled()
    {
        var result = FolderScanner.Scan(root, new ScanProgress(), TestContext.Current.CancellationToken);
        var photos = Folder(result.Root, "photos");
        result.Remove(Folder(photos, "2026"));

        Assert.Equal(10_103, result.Root.Size);
        Assert.Equal(3, result.Root.TotalFiles);
        Assert.Equal(2, result.Root.TotalFolders);
        Assert.DoesNotContain(result.Largest, f => f.Name == "hike.JPG");

        result.Remove(result.Largest.Single(f => f.Name == "beach.jpg"));
        Assert.Equal(103, result.Root.Size);
        Assert.Equal(1, photos.FileCount);
    }
}
