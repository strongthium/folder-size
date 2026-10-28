using System.Globalization;

namespace Strongthium.FolderSize.Tests;

public sealed class TreemapTests
{
    [Fact]
    public void GivesEachItsShareOfTheArea()
    {
        // The example in Bruls, Huizing and van Wijk's paper.
        double[] weights = [6, 6, 4, 3, 2, 2, 1];
        var bounds = new Box(0, 0, 6, 4);
        var boxes = Treemap.Squarify(weights, bounds);

        for (var i = 0; i < weights.Length; i++)
        {
            Assert.Equal(weights[i], boxes[i].Area, 6);
            Assert.True(boxes[i].X >= -1e-9 && boxes[i].Y >= -1e-9 && boxes[i].Right <= 6 + 1e-9 && boxes[i].Bottom <= 4 + 1e-9);
        }
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
                Assert.True(Overlap(boxes[i], boxes[j]) < 1e-9, $"{i} and {j} overlap");
        // Rows are kept near square: the paper's worst is 9/4... here no box is longer than 3 to 1.
        Assert.All(boxes, box => Assert.True(Math.Max(box.Width / box.Height, box.Height / box.Width) <= 3));
    }

    [Fact]
    public void LeavesOutWhatWeighsNothing()
    {
        var boxes = Treemap.Squarify([10, 0, 5], new Box(10, 20, 300, 100));
        Assert.Equal(default, boxes[1]);
        Assert.Equal(30_000, boxes[0].Area + boxes[2].Area, 6);
        Assert.Empty(Treemap.Squarify([], new Box(0, 0, 10, 10)));
    }

    private static double Overlap(Box a, Box b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
}

public sealed class SizesTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1,023 bytes")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1_048_550, "1.0 MB")]
    [InlineData(6_871_947_674, "6.4 GB")]
    [InlineData(2_000_381_014_016, "1.8 TB")]
    public void WritesSizesAsWindowsDoes(long bytes, string text) => Assert.Equal(text, Sizes.Format(bytes, English));

    [Fact]
    public void WritesTimesAndShares()
    {
        Assert.Equal("40 ms", Sizes.Duration(TimeSpan.FromMilliseconds(40.2), English));
        Assert.Equal("2.3 s", Sizes.Duration(TimeSpan.FromMilliseconds(2_340), English));
        Assert.Equal("2 min 05 s", Sizes.Duration(TimeSpan.FromSeconds(125), English));
        Assert.Equal("13.2 %", Sizes.Percent(0.1321, English));
    }
}

public sealed class SampleDataTests
{
    [Fact]
    public void AddsUp()
    {
        var sample = SampleData.Create();
        var root = sample.Root;
        Assert.Equal(ScanMethod.Sample, sample.Method);
        Assert.Equal(@"C:\", root.Path);
        Assert.Equal(root.FilesSize + root.Folders.Sum(f => f.Size), root.Size);
        Assert.Equal(root.TotalFiles, sample.Types.Sum(t => t.Count));
        Assert.InRange(root.Size, 250L << 30, 420L << 30);

        Assert.Equal(ScanResult.LargestKept, sample.Largest.Count);
        Assert.Equal(sample.Largest.OrderByDescending(f => f.Size).Select(f => f.Size), sample.Largest.Select(f => f.Size));
        Assert.Contains(sample.Largest, f => f.Name == "Dev box.vhdx");

        var alex = root.Folders.Single(f => f.Name == "Users").Folders.Single(f => f.Name == "Alex");
        Assert.Equal(@"C:\Users\Alex", alex.Path);
        Assert.Equal(alex.FileCount, FolderFiles.Read(alex).Count);
    }

    [Fact]
    public void IsTheSameEveryTime() => Assert.Equal(SampleData.Create().Root.Size, SampleData.Create().Root.Size);
}
