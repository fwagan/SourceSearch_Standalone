using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SourceSearch.Core;

/// <summary>Owns branch cache registrations. Source trees are never written; snapshots are immutable.</summary>
public sealed partial class IndexCache(string root, IIndexBuilder builder, IWorkspaceIdentityReader identityReader) : IIndexCache
{
    private readonly string cacheRoot = Path.GetFullPath(root);
    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
    public static string KeyFor(CatalogOptions catalog, WorkspaceIdentity identity)
    {
        if (identity.Kind != WorkspaceKind.Git || string.IsNullOrEmpty(identity.RepositoryRoot) ||
            (string.IsNullOrEmpty(identity.Branch) && string.IsNullOrEmpty(identity.Commit)))
            throw new InvalidOperationException("A known Git identity is required for persistent caching.");
        if (!FileCatalog.IsWithin(Path.GetFullPath(catalog.RootPath), Path.GetFullPath(identity.RepositoryRoot)))
            throw new InvalidOperationException("Source root is outside the repository.");
        var value = new { Version = 2, Repo = Normalize(identity.RepositoryRoot), Source = Normalize(catalog.RootPath),
            Branch = identity.Branch, Detached = identity.Branch is null ? identity.Commit : null, catalog.MaxFileBytes,
            Extensions = catalog.Extensions.Select(e => e.ToUpperInvariant()).Order(StringComparer.Ordinal).ToArray(),
            ExcludedRoots = (catalog.ExcludedRoots ?? []).Select(Normalize).Order(StringComparer.Ordinal).ToArray() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    }
    public bool Exists(CatalogOptions catalog, WorkspaceIdentity identity)
    {
        string directory = EntryDirectory(KeyFor(catalog, identity));
        CheckAncestors(directory);
        // A partial/corrupt registration must be reported, not silently treated as a cache miss.
        return Directory.Exists(directory);
    }
    public Task<BuildResult> PrepareAsync(CatalogOptions catalog, WorkspaceIdentity identity, bool forceRebuild, IProgress<BuildProgress>? progress, CancellationToken ct) =>
        Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested(); string key = KeyFor(catalog, identity), directory = EntryDirectory(key);
            using var operation = Lock(key); CheckTree(directory, ct);
            var before = await identityReader.ReadAsync(catalog.RootPath, ct).ConfigureAwait(false);
            RequireIdentity(identity, before);
            string? previous = null;
            if (Directory.Exists(directory) && !forceRebuild)
            {
                var record = ReadRecord(directory, key);
                if (KeyFor(catalog, record.Identity) != key || Normalize(record.SourceRoot) != Normalize(catalog.RootPath))
                    throw new InvalidDataException("Cache identity is inconsistent; use a full rebuild.");
                previous = SnapshotPath(directory, record.SnapshotId);
                try { using var snapshot = IndexSnapshot.Open(previous); if (!IndexBuilder.Compatible(snapshot, catalog)) throw new InvalidDataException("Incompatible cached snapshot; use a full rebuild."); }
                catch (Exception error) when (error is IOException or JsonException or ArgumentException or KeyNotFoundException)
                { throw new InvalidDataException("Cached snapshot is invalid; use a full rebuild.", error); }
            }
            Directory.CreateDirectory(directory);
            var input = catalog with { ExcludedRoots = (catalog.ExcludedRoots ?? []).Append(cacheRoot).ToArray() };
            BuildResult? result = null;
            try
            {
                result = await builder.BuildAsync(new(input, Path.Combine(directory,"snapshots"), previous), progress, ct).ConfigureAwait(false);
                var after = await identityReader.ReadAsync(catalog.RootPath, ct).ConfigureAwait(false);
                RequireIdentity(before, after); ct.ThrowIfCancellationRequested();
                var record = new CacheRecord(1,key,Path.GetFullPath(catalog.RootPath),after,result.Snapshot.Id,DateTime.UtcNow);
                string temporary = Path.Combine(directory,"entry.tmp");
                await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(record),ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary,Path.Combine(directory,"entry.json"),true);
                return result;
            }
            catch { result?.Snapshot.Dispose(); throw; }
        }, ct);
    private static void RequireIdentity(WorkspaceIdentity expected, WorkspaceIdentity actual)
    {
        if (actual.Kind != WorkspaceKind.Git || expected.Kind != WorkspaceKind.Git || actual.DiffersFrom(expected))
            throw new InvalidOperationException("Git identity changed or is unavailable; cache was not published. Retry after the workspace is stable.");
    }
    private static CacheRecord ReadRecord(string directory,string key)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CacheRecord>(File.ReadAllText(Path.Combine(directory,"entry.json")));
            if (entry is null || entry.Version != 1 || entry.Key != key || entry.SnapshotId == Guid.Empty || entry.Identity is null || string.IsNullOrEmpty(entry.SourceRoot))
                throw new InvalidDataException("Invalid cache registration.");
            return entry;
        }
        catch(Exception error) when (error is IOException or JsonException or ArgumentException)
        { throw new InvalidDataException("Cache registration is damaged or incomplete; use a full rebuild.",error); }
    }
    private static string SnapshotPath(string directory,Guid id) => Path.Combine(directory,"snapshots",id.ToString("N"));
}
