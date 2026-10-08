using SourceSearch.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace SourceSearch.Validation;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length == 0 || args.Length % 2 != 1 || !new[] { "build", "queries", "verify", "benchmark" }.Contains(args[0]))
                throw new ArgumentException("Usage: build|queries|verify|benchmark --name value ...");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || !options.TryAdd(args[i][2..], args[i + 1])) throw new ArgumentException("Invalid or duplicate option.");
            string Required(string key) => options.TryGetValue(key, out string? value) ? value : throw new ArgumentException("Missing --" + key);
            if (args[0] == "build")
            {
                string preset = Required("preset");
                var types = preset switch { "csharp" => CorpusPresets.CSharp, "mixed" => CorpusPresets.Mixed, _ => throw new ArgumentException("Preset must be csharp or mixed.") };
                var timer = Stopwatch.StartNew();
                long last = -10000;
                var progress = new CallbackProgress<BuildProgress>(p =>
                {
                    long now = timer.ElapsedMilliseconds;
                    if (now - last >= 5000) { Console.Error.WriteLine($"Build: processed={p.FilesProcessed}, indexed={p.FilesIndexed}, skipped={p.FilesSkipped}, elapsed={timer.Elapsed}"); last = now; }
                });
                var result = await new IndexBuilder(new FileCatalog(), new StrictTextReader()).BuildAsync(new(new(Required("root"), types), Required("index-root")), progress, cts.Token).ConfigureAwait(false);
                using var built = result.Snapshot;
                WriteJson(Required("report"), new { SnapshotPath = built.DirectoryPath, Report = result.Report, ActualIndexBytes = DirectoryBytes(built.DirectoryPath), Environment = EnvironmentReport(), PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 });
                Console.WriteLine($"Indexed {result.Report.Files.Count} files / {result.Report.IndexedLines} lines in {timer.Elapsed}.");
                return 0;
            }
            var opened = Stopwatch.StartNew();
            using var snapshot = IndexSnapshot.Open(Required("snapshot"));
            double openMs = opened.Elapsed.TotalMilliseconds;
            if (args[0] == "queries") { QuerySetBuilder.Write(Required("output"), QuerySetBuilder.Create(snapshot)); return 0; }
            var queries = QuerySetBuilder.Read(Required("queries"));
            if (args[0] == "verify")
            {
                var verification = await new CorrectnessVerifier(new SearchService()).VerifyAsync(snapshot, queries, cts.Token).ConfigureAwait(false);
                bool passed = verification.All(v => v.Passed);
                WriteJson(Required("output"), new { SnapshotId = snapshot.Id, Passed = passed, Results = verification });
                return passed ? 0 : 1;
            }
            if (queries.Count == 0) throw new ArgumentException("Query set is empty.");
            var firstTimer = Stopwatch.StartNew();
            await new SearchService().SearchAsync(snapshot, queries[0].Request, null, 100, cts.Token).ConfigureAwait(false);
            double firstMs = firstTimer.Elapsed.TotalMilliseconds;
            int warmups = int.Parse(options.GetValueOrDefault("warmups", "5"));
            int iterations = int.Parse(options.GetValueOrDefault("iterations", "30"));
            var timings = await new BenchmarkRunner(new SearchService()).RunAsync(snapshot, queries, warmups, iterations, cts.Token).ConfigureAwait(false);
            bool withinTarget = timings.All(t => t.P95Ms <= 100);
            WriteJson(Required("output"), new { SnapshotId = snapshot.Id, Layer = "core", Passed = withinTarget, IndexOpenMs = openMs, FirstQueryMs = firstMs, Environment = EnvironmentReport(), PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, Timings = timings });
            return withinTarget ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
        catch (Exception e) when (e is ArgumentException or FormatException) { Console.Error.WriteLine(e.Message); return 2; }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static long DirectoryBytes(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
    public static object EnvironmentReport() => new
    {
        OS = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount,
        CpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
        AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        LuceneVersion = typeof(Lucene.Net.Index.IndexWriter).Assembly.GetName().Version?.ToString(),
        Disk = new DriveInfo(Path.GetPathRoot(Path.GetFullPath("."))!).DriveFormat
    };
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T> { public void Report(T value) => callback(value); }
}
