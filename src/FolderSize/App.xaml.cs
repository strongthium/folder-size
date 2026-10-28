using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Strongthium.FolderSize.App;

public sealed partial class App : Application
{
    public static ImageSource Icon { get; } = BitmapFrame.Create(new Uri("pack://application:,,,/folder-size;component/Assets/folder-size.png"));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
