using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using SourceSearch.Validation;
namespace SourceSearch.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Option(string name)
        { int index = Array.IndexOf(e.Args, name); return index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null; }
        var window = new MainWindow(Option("--root"));
        MainWindow = window;
        if (e.Args.Contains("--ui-smoke"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.ContentRendered += Smoke;
            async void Smoke(object? sender, EventArgs args)
            {
                window.ContentRendered -= Smoke;
                string output = Option("--output") ?? Path.Combine(SourceSearch.Desktop.MainWindow.WorkspaceRoot, "artifacts", "reports", "ui-smoke.json");
                int exitCode = 1;
                try { var checks = await window.RunSmokeAsync(Path.GetDirectoryName(Path.GetFullPath(output))!); Write(output, new { Passed = true, Checks = checks }); exitCode = 0; }
                catch (Exception error) { Write(output, new { Passed = false, Error = error.ToString() }); }
                finally { window.Close(); Shutdown(exitCode); }
            }
        }
        else if (e.Args.Contains("--ui-benchmark"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.ContentRendered += Benchmark;
            async void Benchmark(object? sender, EventArgs args)
            {
                window.ContentRendered -= Benchmark;
                string? output = Option("--output");
                int exitCode = 1;
                try
                {
                    string snapshot = Option("--snapshot") ?? throw new ArgumentException("--snapshot is required.");
                    string queries = Option("--queries") ?? throw new ArgumentException("--queries is required.");
                    if (output is null) throw new ArgumentException("--output is required.");
                    var open = Stopwatch.StartNew();
                    await window.OpenBenchmarkSnapshotAsync(snapshot, default);
                    double openMs = open.Elapsed.TotalMilliseconds;
                    var cases = QuerySetBuilder.Read(queries);
                    var first = await window.ExecuteMeasuredSearchAsync(cases[0], default);
                    var timings = await new UiBenchmarkRunner(window).RunAsync(cases, 5, 30, default);
                    bool passed = timings.All(t => t.SampleCount >= 30 && t.P95Ms <= 100);
                    Write(output, new { Passed = passed, Valid = true, Snapshot = snapshot, SnapshotOpenMs = openMs, FirstQueryMs = first.UiElapsed.TotalMilliseconds, Timings = timings, Environment = SourceSearch.Validation.Program.EnvironmentReport(), PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 });
                    exitCode = passed ? 0 : 1;
                }
                catch (Exception error)
                {
                    if (output is not null) Write(output, new { Passed = false, Valid = false, Error = error.ToString() });
                    else MessageBox.Show(error.Message, "Source Search");
                }
                finally { window.Close(); Shutdown(exitCode); }
            }
        }
        window.Show();
    }
    private static void Write<T>(string path, T value)
    { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
}
