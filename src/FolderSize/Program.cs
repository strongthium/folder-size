using System.Security.Principal;
using System.Windows;

namespace Strongthium.FolderSize.App;

/// <summary>
/// The command line: <c>folder-size [folder]</c> scans that folder (or drive) at once. <c>--sample</c> shows
/// made-up data instead of a scan; <c>--theme light|dark</c> sets the look rather than following Windows;
/// <c>--view folders|treemap|largest|types</c> and <c>--expand Users\Alex</c> open on that view and folder (for
/// the README's pictures); <c>--about</c> opens the About window as well. <c>--wait-for pid</c> is how an update
/// starts the new program.
/// </summary>
internal static class Program
{
    public static string? StartPath { get; private set; }
    public static bool Sample { get; private set; }
    public static string? StartView { get; private set; }
    public static string? Expand { get; private set; }
    public static bool ShowAbout { get; private set; }

    /// <summary>Running as administrator: NTFS drives are read from their file table.</summary>
    public static bool IsAdministrator { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [STAThread]
    private static int Main(string[] args)
    {
        string? theme = null;
        int? waitFor = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sample": Sample = true; break;
                case "--about": ShowAbout = true; break;
                case "--theme" when i + 1 < args.Length: theme = args[++i]; break;
                case "--view" when i + 1 < args.Length: StartView = args[++i]; break;
                case "--expand" when i + 1 < args.Length: Expand = args[++i]; break;
                case "--wait-for" when i + 1 < args.Length && int.TryParse(args[i + 1], out var pid):
                    waitFor = pid;
                    i++;
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal)) StartPath = args[i];
                    break;
            }
        }

        // Updated: the copy that started this one is quitting, and its file can go once it has.
        Updater.Tidy(waitFor);

        var app = new App();
        app.InitializeComponent();
        app.ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        return app.Run();
    }
}
