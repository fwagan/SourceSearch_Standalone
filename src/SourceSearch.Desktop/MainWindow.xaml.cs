using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using SourceSearch.Core;
using SourceSearch.Presentation;
namespace SourceSearch.Desktop;
public partial class MainWindow : Window
{
    private bool closed;
    private Task? closing;
    public SearchViewModel Model { get; }
    public MainWindow(string? root = null)
    {
        InitializeComponent();
        var controller = new SearchController(new IndexBuilder(new FileCatalog(), new StrictTextReader()), new SearchService());
        Model = new(controller, WorkspacePaths.IndexRoot, new WindowPresentation(this));
        if (root is not null) Model.RootPath = root;
        DataContext = Model;
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) Model.RootPath = dialog.FolderName;
    }
    public Task CloseAsync() => closing ??= CloseCoreAsync();
    private async Task CloseCoreAsync()
    {
        IsEnabled = false;
        await System.Windows.Threading.Dispatcher.Yield();
        try { await Model.DisposeAsync(); }
        finally { closed = true; Close(); }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        try { await CloseAsync(); }
        catch (Exception error) { MessageBox.Show(error.Message, "关闭窗口失败"); }
    }
    private sealed class WindowPresentation(MainWindow window) : ISearchPresentation
    {
        public Task<TimeSpan> MeasureAsync(Func<Task> updateResults, CancellationToken ct) =>
            PresentationTimer.MeasureAsync(async () =>
            {
                var first = window.Model.Results.FirstOrDefault();
                await updateResults();
                if (window.Model.Results.Count > 0 && !ReferenceEquals(first, window.Model.Results[0]))
                    window.ResultsGrid.ScrollIntoView(window.Model.Results[0]);
            }, window.ResultsGrid, ct);
    }
}
