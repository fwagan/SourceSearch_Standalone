namespace SourceSearch.Core;
public sealed record IndexBuildOptions(CatalogOptions Catalog, string IndexRoot, string? PreviousSnapshotPath = null);
public sealed record BuildProgress(int FilesProcessed, int FilesIndexed, int FilesSkipped);
public sealed record IndexedFile(int Id, string FullPath, string Extension, long Bytes, DateTime LastWriteUtc, string Sha256, int LineCount);
public sealed record BuildReport(Guid SnapshotId, DateTime CreatedUtc, IReadOnlyList<IndexedFile> Files, IReadOnlyList<CatalogIssue> Issues, long IndexedLines, long IndexBytes, TimeSpan Elapsed, IndexUpdate? Update = null);
public sealed record IndexUpdate(int Reused, int Added, int Changed, int Removed);
public sealed record BuildResult(IndexSnapshot Snapshot, BuildReport Report, bool Reused = false);
public interface IIndexBuilder { Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct); }
internal sealed record IndexManifest(int SchemaVersion, string SourceRoot, string[] Extensions, long MaxFileBytes, BuildReport Report);
