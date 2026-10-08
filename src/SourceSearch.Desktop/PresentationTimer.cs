using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
namespace SourceSearch.Desktop;
public static class PresentationTimer
{
    public static async Task<TimeSpan> MeasureAsync(Func<Task> updateResults, FrameworkElement results, CancellationToken ct)
    {
        results.Dispatcher.VerifyAccess();
        var window = Window.GetWindow(results);
        void ValidateWindow()
        {
            if (window is null || !window.IsVisible || window.WindowState == WindowState.Minimized || !results.IsLoaded)
                throw new InvalidOperationException("UI measurement requires a visible, non-minimized results window.");
        }
        ValidateWindow();
        var timer = Stopwatch.StartNew();
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool updated = false, laidOut = false;
        EventHandler layout = (_, _) => { if (updated) laidOut = true; };
        EventHandler rendering = (_, _) =>
        {
            try { ValidateWindow(); if (updated && laidOut) checkpoint.TrySetResult(); }
            catch (Exception error) { checkpoint.TrySetException(error); }
        };
        results.LayoutUpdated += layout;
        CompositionTarget.Rendering += rendering;
        try
        {
            await updateResults();
            updated = true;
            results.InvalidateMeasure(); results.UpdateLayout();
            await checkpoint.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            ValidateWindow();
            return timer.Elapsed;
        }
        finally
        {
            results.LayoutUpdated -= layout;
            CompositionTarget.Rendering -= rendering;
        }
    }
}
