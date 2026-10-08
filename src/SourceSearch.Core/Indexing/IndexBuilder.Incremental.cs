namespace SourceSearch.Core;
public sealed partial class IndexBuilder
{
    internal static bool Compatible(IndexSnapshot snapshot, CatalogOptions catalog) => snapshot.Manifest.SchemaVersion == 2 &&
        string.Equals(Path.TrimEndingDirectorySeparator(snapshot.SourceRoot), Path.TrimEndingDirectorySeparator(Path.GetFullPath(catalog.RootPath)), StringComparison.OrdinalIgnoreCase) &&
        snapshot.Manifest.MaxFileBytes == catalog.MaxFileBytes && catalog.Extensions.SetEquals(snapshot.Manifest.Extensions);
    private static bool SameStamp(string path, IndexedFile old)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.Length == old.Bytes && info.LastWriteTimeUtc == old.LastWriteUtc;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private static void CopyIndex(string previous, string next, CancellationToken ct)
    {
        string destination = Path.Combine(next, "index"); System.IO.Directory.CreateDirectory(destination);
        foreach (string file in System.IO.Directory.EnumerateFiles(Path.Combine(previous, "index")))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Snapshot contains a link.");
            if (Path.GetFileName(file) != "write.lock") File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }
}
