using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly Action<ProcessStartInfo> startFile;
    private readonly Action<string> reportFileError;
    private readonly Func<string, string?> resolveDefaultEditor;
    public SearchViewModel Model { get; }
    public IIndexCache? CacheStore { get; }
    public MainWindow(string? root = null, bool enableCache = true, Action<ProcessStartInfo>? startFile = null, Action<string>? reportFileError = null, Func<string, string?>? resolveDefaultEditor = null)
    {
        InitializeComponent();
        this.startFile = startFile ?? (info => { using var process = Process.Start(info); });
        this.reportFileError = reportFileError ?? (message => MessageBox.Show(this, message, "无法打开文件", MessageBoxButton.OK, MessageBoxImage.Warning));
        this.resolveDefaultEditor = resolveDefaultEditor ?? WindowsFileAssociation.FindExecutable;
        var builder = new IndexBuilder(new FileCatalog(), new StrictTextReader());
        var identity = new WorkspaceIdentityReader();
        CacheStore = enableCache ? new IndexCache(WorkspacePaths.CacheRoot,builder,identity) : null;
        var controller = new SearchController(builder, new SearchService());
        Model = new(controller, WorkspacePaths.IndexRoot, new WindowPresentation(this),identity,CacheStore) { RootPath = root ?? @"C:\Coding\Ftrans\ftrans.main" };
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
        try { await Model.ActivateAsync(); }
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
    private void CacheManager_Click(object sender, RoutedEventArgs e)
    { if (CacheStore is not null) new CacheManagerWindow(CacheStore) { Owner = this }.ShowDialog(); }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not ResultRow row) return;
        try { Clipboard.SetText(row.FilePath); }
        catch (System.Runtime.InteropServices.COMException error) { MessageBox.Show(this, error.Message, "无法复制路径"); }
    }
    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ResultsGrid, source) is not DataGridRow { Item: ResultRow row }) return;
        e.Handled = true;
        try
        {
            if (!File.Exists(row.FilePath)) throw new FileNotFoundException("文件不存在，可能已被移动或删除。", row.FilePath);
            OpenResultFile(row);
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { reportFileError($"{row.FilePath}\n{error.Message}"); }
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
