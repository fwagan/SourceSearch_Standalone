using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using System.Text.Json;
namespace SourceSearch.Core;
public sealed class IndexSnapshot : IDisposable
{
    private readonly FSDirectory directory;
    private bool disposed;
    public Guid Id => Report.SnapshotId;
    public BuildReport Report { get; }
    public string DirectoryPath { get; }
    internal DirectoryReader Reader { get; }
    internal IndexSearcher Searcher { get; }
    internal IndexManifest Manifest { get; }
    private IndexSnapshot(string path, IndexManifest manifest, FSDirectory directory, DirectoryReader reader)
    { DirectoryPath = path; Manifest = manifest; Report = manifest.Report; this.directory = directory; Reader = reader; Searcher = new(reader); }
    public static IndexSnapshot Open(string path)
    {
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
            return new(path, manifest, directory, reader);
        }
        catch { directory.Dispose(); throw; }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; Reader.Dispose(); directory.Dispose(); }
}
