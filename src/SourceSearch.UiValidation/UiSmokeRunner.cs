using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SourceSearch.Core;
using SourceSearch.Validation;
using SourceSearch.Desktop;
using SourceSearch.Presentation;
using System.Windows.Input;
using System.Windows.Documents;
using SourceSearch.Desktop.Controls;
namespace SourceSearch.UiValidation;
public sealed class UiSmokeRunner(MainWindow window)
{
    public async Task<IReadOnlyList<string>> RunAsync(string outputDirectory)
    {
        var Model = window.Model;
        var ResultsGrid = (DataGrid)window.FindName("ResultsGrid");
        Task<SearchExecution> ExecuteMeasuredSearchAsync(QueryCase query, CancellationToken ct) => SearchScenario.ExecuteAsync(window, query, ct);
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        QueryCase Query(string text, MatchMode mode = MatchMode.Contains, string include = "", string exclude = "") =>
            new("smoke", new(text, mode, ExtensionFilter.Parse(include.Length > 0, include, exclude.Length > 0, exclude)), "smoke");
        Check(Model.ShowEmpty && !Model.CanSearch, "Unbuilt folder shows empty state and disables search");
        Check(window.FindResource("MaterialDesignOutlinedTextBox") is Style && ((SolidColorBrush)window.FindResource("AppBackground")).Color == Color.FromRgb(18,20,23), "Validation host loads the shared dark MaterialDesign theme");
        await Model.BuildCommand.ExecuteAsync();
        Check(Model.CanSearch, "Fixture index ready");
        Check(!Model.SearchCommand.CanExecute(null), "Empty query disables search command");
        var queryInput = (TextBox)window.FindName("QueryInput");
        var searchButton = (Button)window.FindName("SearchButton");
        Check(ReferenceEquals(searchButton.Command, Model.SearchCommand) && ReferenceEquals(queryInput.InputBindings.OfType<KeyBinding>().Single().Command, Model.SearchCommand), "Search button and Enter bind to the same command");
        int notifications = 0;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => notifications++;
        Model.Results.CollectionChanged += changed;
        var contains = await ExecuteMeasuredSearchAsync(Query("User", include: "txt"), default);
        Model.Results.CollectionChanged -= changed;
        Check(notifications == 1, "A search batch publishes one collection notification");
        Check(contains.Page.Hits.Count == 5, "Default contains matches embedded identifiers");
        var whole = await ExecuteMeasuredSearchAsync(Query("User", MatchMode.WholeWord, "txt"), default);
        Check(whole.Page.Hits.Count == 2, "Whole word excludes identifier fragments, ignores case");
        var filtered = await ExecuteMeasuredSearchAsync(Query("User", include: "cs,js", exclude: "cs"), default);
        Check(filtered.Page.Hits.Count == 2 && filtered.Page.Hits.All(h => h.FilePath.EndsWith(".js")), "Include/exclude filters, exclusion wins");
        var overlap = await ExecuteMeasuredSearchAsync(Query("User", include: "cs", exclude: "cs"), default);
        Check(overlap.Page.Hits.Count == 0, "Overlapping filters produce empty results");
        Check(Model.ShowEmpty && Model.EmptyTitle == "没有匹配结果", "Empty search has a distinct results state");
        Model.IncludeText = "txt"; Model.ExcludeText = "js"; Model.IncludeEnabled = false; Model.ExcludeEnabled = false;
        await Model.SearchCommand.ExecuteAsync();
        Check(Model.IncludeText == "txt" && Model.ExcludeText == "js", "Disabled filter values survive interactive search");
        Model.IncludeEnabled = true;
        await Model.SearchCommand.ExecuteAsync();
        Check(Model.Results.Count == 5, "Re-enabling a stored filter applies its original value");
        await ExecuteMeasuredSearchAsync(Query("User", include: "cs"), default);
        Check(Model.Results.Count == 100 && Model.CanMore, "First batch has 100 rows");
        for (int i = 0; i < 9; i++) await Model.MoreCommand.ExecuteAsync();
        Check(Model.Results.Count == 1000 && !Model.CanMore, "UI cap stops at 1000 without claiming total");
        int RealizedRows(DependencyObject node)
        {
            int count = node is DataGridRow ? 1 : 0;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) count += RealizedRows(VisualTreeHelper.GetChild(node, i));
            return count;
        }
        Check(RealizedRows(ResultsGrid) < 60, "DataGrid virtualizes the 1000-row result list");
        bool rejected = false;
        try { window.WindowState = WindowState.Minimized; await ExecuteMeasuredSearchAsync(Query("User"), default); }
        catch (InvalidOperationException) { rejected = true; }
        finally { window.WindowState = WindowState.Normal; }
        Check(rejected, "Minimized window invalidates UI measurement");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await ExecuteMeasuredSearchAsync(Query("User", include: "txt"), default);
        Directory.CreateDirectory(outputDirectory);
        IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T found) yield return found;
            for (int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node,i))) yield return child;
        }
        Check(Descendants<HighlightedLine>(ResultsGrid).Any(t => t.Inlines.OfType<Run>().Any(r => r.Background is not null)), "Visible rows highlight original match offsets");
        Model.QueryText = "GetUser";
        Check(Model.HasPendingChanges && Model.AppliedRequest!.Text == "User" && !Model.CanMore, "Editing preserves applied result identity and disables paging");
        ApplicationCommands.Find.Execute(null, window);
        Check(queryInput.IsKeyboardFocused && queryInput.SelectionLength == queryInput.Text.Length, "Find command focuses and selects query text");
        await ExecuteMeasuredSearchAsync(Query("User", include: "txt"), default);
        void Screenshot(FrameworkElement visual, string name)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var bounds = new Rect(visual.RenderSize);
                context.DrawRectangle((Brush)window.FindResource("AppBackground"), null, bounds);
                context.DrawRectangle(new VisualBrush(visual) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds);
            }
            bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(outputDirectory, name)); png.Save(file);
        }
        var layout = (Grid)window.FindName("LayoutRoot");
        Screenshot(layout, "ui-smoke.png");
        var details = new IndexDetailsWindow(Model) { Owner = window }; details.Show();
        await details.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(details.IsVisible && Descendants<DataGrid>(details).Any(), "Index details opens with a virtualized issue table");
        Screenshot(details, "ui-details.png"); details.Close();
        window.Width = 880; window.Height = 600;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Grid.GetRow((Grid)window.FindName("ExcludeFilter")) == 1 && searchButton.IsVisible && ResultsGrid.ActualHeight > 100, "Minimum window stacks filters and preserves usable results");
        Screenshot(layout, "ui-narrow.png");
        foreach (double scale in new[] { 1.25, 1.5 })
        {
            window.Width = Math.Max(1180, 900 * scale); window.Height = Math.Max(780, 640 * scale);
            layout.LayoutTransform = new ScaleTransform(scale, scale);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var bounds = searchButton.TransformToAncestor(window).TransformBounds(new Rect(searchButton.RenderSize));
            Check(bounds.Right <= window.ActualWidth && ResultsGrid.ActualHeight > 100, $"Layout transform {scale:P0} keeps primary controls and results visible (not physical DPI)");
            Screenshot(window, $"ui-scale-{scale * 100:0}.png");
        }
        layout.LayoutTransform = Transform.Identity; window.Width = 1180; window.Height = 780;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        string switched = Path.Combine(outputDirectory, "empty-folder"); Directory.CreateDirectory(switched);
        await Model.SwitchFolderAsync(switched);
        Check(!Model.CanSearch && Model.Report is null && Model.Results.Count == 0, "Changing folders releases the old snapshot and results");
        // Create a new owned Git fixture; never initialize or edit the caller's selected source folder.
        string gitFixture = Path.Combine(Path.GetTempPath(), "SourceSearch.CacheFixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gitFixture); File.WriteAllText(Path.Combine(gitFixture,"a.cs"),"User");
        var git = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = gitFixture, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach(string arg in new[]{"init","-b","cache-smoke"})git.ArgumentList.Add(arg);
        using(var process = System.Diagnostics.Process.Start(git)!)
        {
            var output = process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await output;
            if(process.ExitCode !=0)throw new InvalidOperationException(await error);
        }
        await Model.SwitchFolderAsync(gitFixture);Check(!Model.CanSearch,"A new Git folder does not automatically build a full index");
        await Model.BuildAsync();Guid firstId=Model.Report!.SnapshotId;
        await Model.SwitchFolderAsync(switched);await Model.SwitchFolderAsync(gitFixture);
        Check(Model.CanSearch && Model.Report!.SnapshotId==firstId && Model.CacheSummary.Contains("复用"),"Returning to a Git folder reuses its registered snapshot");
        File.WriteAllText(Path.Combine(gitFixture,"b.cs"),"Second User");await Model.BuildCommand.ExecuteAsync();
        await ExecuteMeasuredSearchAsync(Query("User"),default);
        Check(Model.Results.Count==2 && Model.Report!.Update!.Reused==1,"Incremental update adds changed files and preserves unchanged files");
        var manager = new CacheManagerWindow(window.CacheStore!) { Owner=window };manager.Show();
        await manager.RefreshAsync();await manager.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var entries = await window.CacheStore!.ListAsync(default);
        Check(entries.Any(e=>e.SourceRoot==gitFixture) && manager.IsVisible,"Cache manager displays a real branch cache registration");
        Screenshot(manager,"ui-cache-manager.png");manager.Close();Screenshot(layout,"ui-cache-ready.png");
        string key=IndexCache.KeyFor(new(gitFixture,CorpusPresets.Mixed),Model.Identity);
        bool busyRejected=false;try{await window.CacheStore.DeleteAsync(key,default);}catch(IOException){busyRejected=true;}
        Check(busyRejected,"Deleting a cache in use is rejected");
        await Model.SwitchFolderAsync(switched);await window.CacheStore.DeleteAsync(key,default);
        Check(!(await window.CacheStore.ListAsync(default)).Any(e=>e.Key==key),"An unused selected cache can be deleted safely");
        checks.AddRange(await ResultOpenSmoke.RunAsync(Path.Combine(Path.GetTempPath(), "SourceSearch.OpenFixture", Guid.NewGuid().ToString("N"))));
        return checks;
    }
}
