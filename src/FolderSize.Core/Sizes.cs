using System.Globalization;

namespace Strongthium.FolderSize;

/// <summary>Sizes the way Windows writes them: in units of 1024, with one decimal, and "bytes" under 1 KB.</summary>
public static class Sizes
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (bytes < 0) return "-" + Format(-bytes, culture);
        if (bytes < 1024) return bytes == 1 ? "1 byte" : bytes.ToString("N0", culture) + " bytes";
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        // 1023.96 KB would round to "1024.0 KB": that's 1.0 MB.
        if (Math.Round(value, 1) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("N1", culture) + " " + Units[unit];
    }

    public static string Count(long count, CultureInfo? culture = null) => count.ToString("N0", culture ?? CultureInfo.CurrentCulture);

    /// <summary>A share, 0 to 1, as a percentage with one decimal.</summary>
    public static string Percent(double share, CultureInfo? culture = null) =>
        (share * 100).ToString("0.0", culture ?? CultureInfo.CurrentCulture) + " %";

    /// <summary>How long a scan took: "40 ms", "1.4 s", "12.3 s", "2 min 05 s".</summary>
    public static string Duration(TimeSpan time, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (time.TotalSeconds < 1) return Math.Max(1, (int)Math.Round(time.TotalMilliseconds)).ToString(culture) + " ms";
        if (time.TotalSeconds < 60) return time.TotalSeconds.ToString("0.0", culture) + " s";
        return $"{(int)time.TotalMinutes} min {time.Seconds:00} s";
    }
}
