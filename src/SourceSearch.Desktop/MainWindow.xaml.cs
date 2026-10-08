using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Model = new(controller, WorkspacePaths.IndexRoot, new WindowPresentation(this)) { RootPath = root ?? @"C:\Coding\Ftrans\ftrans.main" };
        DataContext = Model;
    }
    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true)
            try { await Model.SwitchFolderAsync(dialog.FolderName); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "无法打开文件夹"); }
    }
    private async void Window_Activated(object? sender, EventArgs e)
    {
        if (Model is null || closed || closing is not null) return;
        try { await Model.RefreshIdentityAsync(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "无法读取目录信息"); }
    }
    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e) { QueryInput.Focus(); QueryInput.SelectAll(); }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (FiltersGrid is null) return;
        bool narrow = LayoutRoot.ActualWidth < 1020;
        Grid.SetColumn(ExcludeFilter, narrow ? 0 : 1); Grid.SetRow(ExcludeFilter, narrow ? 1 : 0);
        Grid.SetColumnSpan(IncludeFilter, narrow ? 2 : 1); Grid.SetColumnSpan(ExcludeFilter, narrow ? 2 : 1);
        IncludeFilter.Margin = narrow ? new(0) : new(0, 0, 8, 0);
        ExcludeFilter.Margin = narrow ? new(0, 8, 0, 0) : new(8, 0, 0, 0);
    }
    private void Details_Click(object sender, RoutedEventArgs e) { if (Model.HasDetails) new IndexDetailsWindow(Model) { Owner = this }.ShowDialog(); }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not ResultRow row) return;
        try { Clipboard.SetText(row.FilePath); }
        catch (System.Runtime.InteropServices.COMException error) { MessageBox.Show(this, error.Message, "无法复制路径"); }
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
