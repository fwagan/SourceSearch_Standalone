using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SourceSearch.Core;
using SourceSearch.Validation;
namespace SourceSearch.Desktop;
public partial class MainWindow
{
    public async Task<IReadOnlyList<string>> RunSmokeAsync(string outputDirectory)
    {
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        QueryCase Query(string text, MatchMode mode = MatchMode.Contains, string include = "", string exclude = "") =>
            new("smoke", new(text, mode, ExtensionFilter.Parse(include.Length > 0, include, exclude.Length > 0, exclude)), "smoke");
        await BuildIndexAsync();
        Check(Model.CanSearch, "Fixture index ready");
        var contains = await ExecuteMeasuredSearchAsync(Query("User", include: "txt"), default);
        Check(contains.Page.Hits.Count == 5, "Default contains matches embedded identifiers");
        var whole = await ExecuteMeasuredSearchAsync(Query("User", MatchMode.WholeWord, "txt"), default);
        Check(whole.Page.Hits.Count == 2, "Whole word excludes identifier fragments, ignores case");
        var filtered = await ExecuteMeasuredSearchAsync(Query("User", include: "cs,js", exclude: "cs"), default);
        Check(filtered.Page.Hits.Count == 2 && filtered.Page.Hits.All(h => h.FilePath.EndsWith(".js")), "Include/exclude filters, exclusion wins");
        var overlap = await ExecuteMeasuredSearchAsync(Query("User", include: "cs", exclude: "cs"), default);
        Check(overlap.Page.Hits.Count == 0, "Overlapping filters produce empty results");
        await ExecuteMeasuredSearchAsync(Query("User", include: "cs"), default);
        Check(Model.Results.Count == 100 && Model.CanMore, "First batch has 100 rows");
        for (int i = 0; i < 9; i++) await SubmitAsync(Model.CreateRequest(), true, default);
        Check(Model.Results.Count == 1000 && !Model.CanMore, "UI cap stops at 1000 without claiming total");
        int RealizedRows(DependencyObject node)
        {
            int count = node is DataGridRow ? 1 : 0;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) count += RealizedRows(VisualTreeHelper.GetChild(node, i));
            return count;
        }
        Check(RealizedRows(ResultsGrid) < 60, "DataGrid virtualizes the 1000-row result list");
        bool rejected = false;
        try { WindowState = WindowState.Minimized; await ExecuteMeasuredSearchAsync(Query("User"), default); }
        catch (InvalidOperationException) { rejected = true; }
        finally { WindowState = WindowState.Normal; }
        Check(rejected, "Minimized window invalidates UI measurement");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await ExecuteMeasuredSearchAsync(Query("User", include: "txt"), default);
        Directory.CreateDirectory(outputDirectory);
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(outputDirectory, "ui-smoke.png"))) png.Save(file);
        return checks;
    }
}
