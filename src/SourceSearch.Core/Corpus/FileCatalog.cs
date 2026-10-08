using System.ComponentModel;
namespace SourceSearch.Core;
public sealed class FileCatalog : IFileCatalog
{
    internal static bool IsWithin(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static readonly HashSet<string> ExcludedDirectories = new([".git", ".vs", "bin", "obj", "node_modules", ".tmp", ".temp", ".worktrees", "artifacts"], StringComparer.OrdinalIgnoreCase);

    public async Task<CatalogResult> EnumerateAsync(CatalogOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("The source root must not be a reparse point.");
        string[] excluded = (options.ExcludedRoots ?? []).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToArray();
        var files = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<CatalogIssue>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedDirectories = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [root] = true };

        bool Within(string path, string parent) => IsWithin(path, parent);
        bool AllowedDirectory(string path, bool allowMissing = false)
        {
            if (allowedDirectories.TryGetValue(path, out bool allowed)) return allowed;
            if (!Within(path, root) || excluded.Any(e => Within(path, e)) || ExcludedDirectories.Contains(Path.GetFileName(path))) return allowedDirectories[path] = false;
            // Check ancestors first: a missing descendant must not mask an excluded or linked parent.
            if (!AllowedDirectory(Path.GetDirectoryName(path)!, allowMissing)) return false;
            try { return allowedDirectories[path] = !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
            catch (Exception e) when (allowMissing && e is FileNotFoundException or DirectoryNotFoundException) { return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(new(path, "UnreadableDirectory")); return false; }
        }
        void AddFile(string path)
        {
            ct.ThrowIfCancellationRequested();
            path = Path.GetFullPath(path);
            if (!Within(path, root) || !options.Extensions.Contains(Path.GetExtension(path)) || excluded.Any(e => Within(path, e)) || !AllowedDirectory(Path.GetDirectoryName(path)!)) return;
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return;
                files[path] = new(path, Path.GetRelativePath(root, path), Path.GetExtension(path).ToLowerInvariant());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(new(path, "UnreadableOrMissing")); }
        }
        void WalkPlain(string directory)
        {
            ct.ThrowIfCancellationRequested();
            if (!AllowedDirectory(directory)) return;
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory)) AddFile(path);
                foreach (var path in Directory.EnumerateDirectories(directory)) WalkPlain(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { issues.Add(new(directory, "UnreadableDirectory")); }
        }
        async Task WalkGit(string directory)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(directory) || !AllowedDirectory(directory)) return;
            var listed = await GitFileEnumerator.ReadAsync(directory, ct).ConfigureAwait(false);
            var modules = new HashSet<string>(listed.Submodules, StringComparer.Ordinal);
            foreach (string path in listed.Files) if (!modules.Contains(path)) AddFile(Path.Combine(directory, path));
            foreach (string module in listed.Submodules)
            {
                string path = Path.GetFullPath(Path.Combine(directory, module));
                if (!Within(path, root) || !AllowedDirectory(path, allowMissing: true)) continue;
                if (!Directory.Exists(path)) { issues.Add(new(path, "SubmoduleNotInitialized")); continue; }
                if (Directory.Exists(path) && (File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git"))))
                    await WalkGit(path).ConfigureAwait(false);
                else issues.Add(new(path, "SubmoduleNotInitialized"));
            }
        }
        string repo = GitFileEnumerator.FindRepositoryRoot(root);
        if (File.Exists(Path.Combine(repo, ".git")) || Directory.Exists(Path.Combine(repo, ".git")))
        {
            try { await WalkGit(root).ConfigureAwait(false); }
            catch (Win32Exception) { issues.Add(new(root, "GitUnavailableIgnoreRulesNotApplied")); WalkPlain(root); }
        }
        else WalkPlain(root);
        return new(files.Values.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToArray(), issues);
    }
}
