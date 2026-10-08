namespace SourceSearch.Core;
public sealed partial class IndexCache
{
    private string EntryDirectory(string key)
    {
        if(key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid cache key.",nameof(key));
        return Path.Combine(cacheRoot,key.ToLowerInvariant());
    }
    private FileStream Lock(string key)
    {
        _ = EntryDirectory(key); CheckAncestors(cacheRoot);
        string locks = Path.Combine(cacheRoot,".locks");CheckAncestors(locks);Directory.CreateDirectory(locks);
        string path = Path.Combine(locks,key+".lock");CheckAncestors(path);
        try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException error) { throw new IOException("Cache is busy in another operation. Try again later.",error); }
    }
    private static void CheckAncestors(string path)
    {
        for(string? current=Path.GetFullPath(path);current is not null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Cache paths must not contain links.");
    }
    private static void CheckTree(string directory,CancellationToken ct)
    {
        CheckAncestors(directory);
        if(!Directory.Exists(directory))return;
        foreach(string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            ct.ThrowIfCancellationRequested();var attributes=File.GetAttributes(entry);
            if(attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Cache contains a link.");
            if(attributes.HasFlag(FileAttributes.Directory))CheckTree(entry,ct);
        }
    }
    public Task<IReadOnlyList<CacheEntryInfo>> ListAsync(CancellationToken ct) => Task.Run<IReadOnlyList<CacheEntryInfo>>(() =>
    {
        CheckAncestors(cacheRoot);var entries=new List<CacheEntryInfo>();if(!Directory.Exists(cacheRoot))return entries;
        foreach(string directory in Directory.EnumerateDirectories(cacheRoot))
        {
            ct.ThrowIfCancellationRequested();string key=Path.GetFileName(directory);
            if(key.Length !=64 || key.Any(c=>!char.IsAsciiHexDigit(c)))continue;
            try
            {
                using var operation=Lock(key);CheckTree(directory,ct);var record=ReadRecord(directory,key);
                using var snapshot=IndexSnapshot.Open(SnapshotPath(directory,record.SnapshotId));
                entries.Add(new(key,record.SourceRoot,record.Identity.Label,snapshot.Report.CreatedUtc,snapshot.Report.IndexBytes,"可用"));
            }
            catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or KeyNotFoundException)
            { entries.Add(new(key,directory,"—",null,0,"不可用/损坏/使用中："+error.Message)); }
        }
        return entries.OrderBy(e=>e.SourceRoot,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.Branch).ToArray();
    },ct);
    public Task DeleteAsync(string key,CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();string directory=EntryDirectory(key);
        using var operation=Lock(key);CheckTree(directory,ct);if(!Directory.Exists(directory))return;
        var leases=new List<FileStream>();
        try
        {
            foreach(string lease in Directory.EnumerateFiles(directory,"lease",SearchOption.AllDirectories))
            {ct.ThrowIfCancellationRequested();leases.Add(new FileStream(lease,FileMode.Open,FileAccess.ReadWrite,FileShare.Delete));}
            ct.ThrowIfCancellationRequested();
            // Keep new readers out throughout deletion; Share.Delete permits removing our own leased files.
            Directory.Delete(directory,true);
        }
        catch(IOException error) {throw new IOException("Cache is in use. Switch to another folder or close the other window before deleting.",error);}
        finally {foreach(var lease in leases)lease.Dispose();}
    },ct);
}
