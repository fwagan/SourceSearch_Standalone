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
    public Guid Id => Report.SnapshotId;
    public BuildReport Report { get; }
    public string DirectoryPath { get; }
    internal DirectoryReader Reader { get; }
    internal IndexSearcher Searcher { get; }
    internal IndexManifest Manifest { get; }
    private IndexSnapshot(string path, IndexManifest manifest, FSDirectory directory, DirectoryReader reader, long decodedCacheLimit)
    { DirectoryPath = path; Manifest = manifest; Report = manifest.Report; this.directory = directory; Reader = reader; Searcher = new(reader); decoded = new(decodedCacheLimit); }
    internal long DecodedCacheBytes => decoded.Bytes;
    internal DecodedLine GetLine(int id, CancellationToken ct) => decoded.Get(Searcher, id, ct);
    public static IndexSnapshot Open(string path, long decodedCacheLimit = 256L * 1024 * 1024)
    {
        if (decodedCacheLimit < 0) throw new ArgumentOutOfRangeException(nameof(decodedCacheLimit));
        path = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(path, "completed")) || !File.Exists(Path.Combine(path, "manifest.json")))
            throw new InvalidDataException("Snapshot is incomplete.");
        var manifest = JsonSerializer.Deserialize<IndexManifest>(File.ReadAllText(Path.Combine(path, "manifest.json"))) ?? throw new InvalidDataException("Manifest is empty.");
        if (manifest.SchemaVersion != 1 || File.ReadAllText(Path.Combine(path, "completed")).Trim() != manifest.Report.SnapshotId.ToString("D"))
            throw new InvalidDataException("Snapshot version or identity is invalid.");
        var directory = FSDirectory.Open(new DirectoryInfo(Path.Combine(path, "index")));
        try
        {
            var reader = DirectoryReader.Open(directory);
            if (reader.NumDocs != manifest.Report.IndexedLines) { reader.Dispose(); throw new InvalidDataException("Snapshot line count is invalid."); }
            return new(path, manifest, directory, reader, decodedCacheLimit);
        }
        catch { directory.Dispose(); throw; }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; decoded.Clear(); Reader.Dispose(); directory.Dispose(); }
}
