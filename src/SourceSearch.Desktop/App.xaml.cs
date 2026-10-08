using System.Windows;
namespace SourceSearch.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int rootIndex = Array.IndexOf(e.Args, "--root");
        string? root = rootIndex >= 0 && rootIndex + 1 < e.Args.Length ? e.Args[rootIndex + 1] : null;
        MainWindow = new MainWindow(root);
        MainWindow.Show();
    }
}
