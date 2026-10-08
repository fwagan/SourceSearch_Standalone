using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using System.Text.Json;
namespace SourceSearch.Core;
public sealed class IndexSnapshot : IDisposable
{
    private readonly FSDirectory directory;
    private bool disposed;
    private readonly DecodedLineCache decoded;
    private readonly FileStream? lease;
    private readonly IReadOnlyDictionary<int, IndexedFile> filesById;
    public Guid Id => Report.SnapshotId;
    public BuildReport Report { get; }
    public string DirectoryPath { get; }
    public string SourceRoot => Manifest.SourceRoot;
    internal DirectoryReader Reader { get; }
    internal IndexSearcher Searcher { get; }
    internal IndexManifest Manifest { get; }
    private IndexSnapshot(string path, IndexManifest manifest, FSDirectory directory, DirectoryReader reader, long decodedCacheLimit, FileStream? lease)
    {
        DirectoryPath = path; Manifest = manifest; Report = manifest.Report; this.directory = directory; Reader = reader; Searcher = new(reader); decoded = new(decodedCacheLimit);
        filesById = Report.Files.ToDictionary(f => f.Id);
        this.lease = lease;
    }
    internal IndexedFile FileById(int id) => filesById[id];
    internal long DecodedCacheBytes => decoded.Bytes;
    internal DecodedLine GetLine(int id, CancellationToken ct) => decoded.Get(Searcher, id, ct);
    public static IndexSnapshot Open(string path, long decodedCacheLimit = 256L * 1024 * 1024)
    {
        if (decodedCacheLimit < 0) throw new ArgumentOutOfRangeException(nameof(decodedCacheLimit));
        path = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(path, "completed")) || !File.Exists(Path.Combine(path, "manifest.json")))
            throw new InvalidDataException("Snapshot is incomplete.");
        var manifest = JsonSerializer.Deserialize<IndexManifest>(File.ReadAllText(Path.Combine(path, "manifest.json"))) ?? throw new InvalidDataException("Manifest is empty.");
        ValidateManifest(manifest);
        if (manifest.SchemaVersion is not (1 or 2) || File.ReadAllText(Path.Combine(path, "completed")).Trim() != manifest.Report.SnapshotId.ToString("D"))
            throw new InvalidDataException("Snapshot version or identity is invalid.");
        FileStream? lease = null;
        FSDirectory? directory = null;
        try
        {
            // Take a reader lease before opening Lucene; deletion holds its conflicting lease until the tree is gone.
            if (manifest.SchemaVersion >= 2) lease = new FileStream(Path.Combine(path,"lease"), FileMode.Open, FileAccess.Read, FileShare.Read);
            directory = FSDirectory.Open(new DirectoryInfo(Path.Combine(path, "index")));
            var reader = DirectoryReader.Open(directory);
            if (reader.NumDocs != manifest.Report.IndexedLines) { reader.Dispose(); throw new InvalidDataException("Snapshot line count is invalid."); }
            try { return new(path, manifest, directory, reader, decodedCacheLimit, lease); }
            catch { reader.Dispose(); throw; }
        }
        catch { directory?.Dispose(); lease?.Dispose(); throw; }
    }
    private static void ValidateManifest(IndexManifest manifest)
    {
        var report = manifest.Report;
        if (string.IsNullOrWhiteSpace(manifest.SourceRoot) || !Path.IsPathFullyQualified(manifest.SourceRoot) || manifest.MaxFileBytes < 0 ||
            manifest.Extensions is null || manifest.Extensions.Any(string.IsNullOrEmpty) || report is null || report.SnapshotId == Guid.Empty ||
            report.Files is null || report.Issues is null || report.IndexedLines < 0 || report.IndexBytes < 0 ||
            report.Files.Any(f => f is null || f.Id < 0 || string.IsNullOrEmpty(f.FullPath) || !Path.IsPathFullyQualified(f.FullPath) ||
                f.Bytes < 0 || f.LineCount < 0 || string.IsNullOrEmpty(f.Extension) || string.IsNullOrEmpty(f.Sha256)) ||
            report.Files.Select(f => f.Id).Distinct().Count() != report.Files.Count ||
            report.Issues.Any(i => i is null || string.IsNullOrEmpty(i.Path) || string.IsNullOrEmpty(i.Reason)))
            throw new InvalidDataException("Snapshot manifest has invalid required fields.");
    }
    public void Dispose()
    { if (disposed) return; disposed = true; decoded.Clear(); Reader.Dispose(); directory.Dispose(); lease?.Dispose(); }
}
