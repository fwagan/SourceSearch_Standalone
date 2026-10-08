namespace SourceSearch.Core;
public sealed record CatalogOptions(string RootPath, IReadOnlySet<string> Extensions, long MaxFileBytes = 32L * 1024 * 1024, IReadOnlyList<string>? ExcludedRoots = null);
public sealed record SourceFile(string FullPath, string RelativePath, string Extension);
public sealed record CatalogIssue(string Path, string Reason);
public sealed record CatalogResult(IReadOnlyList<SourceFile> Files, IReadOnlyList<CatalogIssue> Issues);
public sealed record StableText(string Text, string Sha256, long Bytes, DateTime LastWriteUtc);
public sealed record TextReadResult(StableText? Content, string? SkipReason);
public interface IFileCatalog { Task<CatalogResult> EnumerateAsync(CatalogOptions options, CancellationToken ct); }
public interface ITextSource { Task<TextReadResult> ReadAsync(SourceFile file, long maxBytes, CancellationToken ct); }
