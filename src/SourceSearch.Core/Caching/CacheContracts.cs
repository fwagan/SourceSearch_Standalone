namespace SourceSearch.Core;
public sealed record CacheEntryInfo(string Key, string SourceRoot, string Branch, DateTime? CreatedUtc, long Bytes, string Status);
public interface IIndexCache
{
    bool Exists(CatalogOptions catalog, WorkspaceIdentity identity);
    Task<BuildResult> PrepareAsync(CatalogOptions catalog, WorkspaceIdentity identity, bool forceRebuild, IProgress<BuildProgress>? progress, CancellationToken ct);
    Task<IReadOnlyList<CacheEntryInfo>> ListAsync(CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
internal sealed record CacheRecord(int Version, string Key, string SourceRoot, WorkspaceIdentity Identity, Guid SnapshotId, DateTime UpdatedUtc);
