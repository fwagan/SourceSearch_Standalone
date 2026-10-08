using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SourceSearch.Core;
using SourceSearch.Validation;
namespace SourceSearch.Desktop;
public partial class MainWindow : Window
{
    private readonly SearchController controller = new(new IndexBuilder(new FileCatalog(), new StrictTextReader()), new SearchService());
    private SearchCursor? cursor;
    private bool closed, closing;
    private string indexedRoot = "";
    public SearchViewModel Model { get; } = new();
    public MainWindow(string? root = null)
    {
        InitializeComponent(); DataContext = Model;
        if (root is not null) Model.RootPath = root;
        Model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(Model.QueryText) or nameof(Model.WholeWord) or nameof(Model.IncludeEnabled) or nameof(Model.IncludeText) or nameof(Model.ExcludeEnabled) or nameof(Model.ExcludeText))
            { controller.CancelQuery(); cursor = null; Model.CanMore = false; }
        };
    }
    public static string WorkspaceRoot
    {
        get
        {
            string directory = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(directory, "SourceSearch.slnx")))
                directory = Directory.GetParent(directory)?.FullName ?? throw new DirectoryNotFoundException("SourceSearch.slnx not found above application.");
            return directory;
        }
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) Model.RootPath = dialog.FolderName;
    }
    private async void Build_Click(object sender, RoutedEventArgs e) => await BuildIndexAsync();
    public async Task BuildIndexAsync()
    {
        string root = Model.RootPath;
        Model.CanEdit = false; Model.CanSearch = false; Model.CanMore = false;
        Model.StatusText = "建立索引中…"; cursor = null;
        try
        {
            var progress = new Progress<BuildProgress>(value => Model.StatusText = $"建立中：已处理 {value.FilesProcessed:N0}，已索引 {value.FilesIndexed:N0}，跳过 {value.FilesSkipped:N0}");
            await controller.RebuildAsync(new(new(root, CorpusPresets.Mixed), Path.Combine(WorkspaceRoot, "artifacts", "indexes", "ui")), progress, default);
            indexedRoot = root; Model.Results.Clear(); ShowReady();
        }
        catch (OperationCanceledException) { Model.StatusText = "建立已取消。" + PreviousSnapshotText(); }
        catch (Exception error) { Model.StatusText = "建立失败：" + error.Message + PreviousSnapshotText(); }
        finally { Model.CanEdit = true; Model.CanSearch = controller.CurrentReport is not null; }
    }
    private string PreviousSnapshotText() => controller.CurrentReport is null ? "尚无可搜索索引。" : $"仍可搜索上次成功建立的快照：{indexedRoot}。";
    private void ShowReady()
    {
        var report = controller.CurrentReport!;
        Model.StatusText = $"索引就绪：{indexedRoot}；{report.Files.Count:N0} 文件 / {report.IndexedLines:N0} 行，跳过 {report.Issues.Count:N0} 项；建立于 {report.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}。显示上次建立索引时的内容。";
        Model.CanSearch = true;
    }
    public async Task OpenBenchmarkSnapshotAsync(string path, CancellationToken ct)
    { await controller.OpenSnapshotAsync(path, ct); indexedRoot = "验证快照 " + path; Model.CanEdit = false; ShowReady(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { controller.Cancel(); Model.CanMore = false; }
    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchFromControlsAsync(false);
    private async void More_Click(object sender, RoutedEventArgs e) => await SearchFromControlsAsync(true);
    private async void Query_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && Model.CanSearch) { e.Handled = true; await SearchFromControlsAsync(false); } }
    private async Task SearchFromControlsAsync(bool more)
    {
        try
        {
            var request = Model.CreateRequest();
            if (more) await SubmitAsync(request, true, default);
            else await ExecuteMeasuredSearchAsync(new("interactive", request, "interactive"), default);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Model.TimingText = "搜索失败：" + error.Message; }
    }
    public Task<(SearchPage Page, TimeSpan UiElapsed)> ExecuteMeasuredSearchAsync(QueryCase query, CancellationToken ct)
    {
        Model.QueryText = query.Request.Text; Model.WholeWord = query.Request.Mode == MatchMode.WholeWord;
        Model.IncludeText = string.Join(',', query.Request.Filter.Include); Model.IncludeEnabled = query.Request.Filter.Include.Count > 0;
        Model.ExcludeText = string.Join(',', query.Request.Filter.Exclude); Model.ExcludeEnabled = query.Request.Filter.Exclude.Count > 0;
        return SubmitAsync(query.Request, false, ct);
    }
    private async Task<(SearchPage Page, TimeSpan UiElapsed)> SubmitAsync(SearchRequest request, bool more, CancellationToken ct)
    {
        if (more && (cursor is null || Model.Results.Count >= 1000)) throw new InvalidOperationException("No further UI page available.");
        var after = more ? cursor : null;
        Model.CanMore = false;
        SearchReply? reply = null;
        var elapsed = await PresentationTimer.MeasureAsync(async () =>
        {
            reply = await controller.SearchAsync(request, after, Math.Min(100, more ? 1000 - Model.Results.Count : 100), ct);
            if (reply.RequestId != controller.CurrentRequestId) throw new OperationCanceledException("Superseded query.");
            Model.Results.Publish(reply.Page.Hits.Select(ResultRow.From), more);
            if (!more && Model.Results.Count > 0) ResultsGrid.ScrollIntoView(Model.Results[0]);
        }, ResultsGrid, ct);
        if (reply!.RequestId != controller.CurrentRequestId) throw new OperationCanceledException("Superseded query.");
        cursor = reply.Page.Next;
        Model.CanMore = cursor is not null && Model.Results.Count < 1000;
        Model.TimingText = $"已显示 {Model.Results.Count:N0} 行；核心 {reply.Page.Elapsed.TotalMilliseconds:F1} ms；呈现检查点 {elapsed.TotalMilliseconds:F1} ms。" +
            (Model.Results.Count >= 1000 ? "已达显示上限 1000 行，不代表全部命中数。" : reply.Page.Exhausted ? "已到结果末尾。" : "可加载下一批。" );
        return (reply.Page, elapsed);
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; IsEnabled = false;
        // Let the initial Closing event return before calling Close again.
        await System.Windows.Threading.Dispatcher.Yield();
        try { await controller.DisposeAsync(); }
        catch (Exception error) { Model.StatusText = "关闭时释放索引失败：" + error.Message; }
        finally { closed = true; Close(); }
    }
}
