using System.Diagnostics;
using System.Text;
namespace SourceSearch.Core.Tests;

public sealed class TestCorpus : IDisposable
{
    private readonly string testRoot;
    public string Root { get; }
    public TestCorpus()
    {
        // Plain-folder fixtures must not inherit this project's .gitignore.
        testRoot = Path.Combine(Path.GetTempPath(), "SourceSearch.Tests");
        Root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }
    public string Write(string relativePath, string text) => WriteBytes(relativePath, new UTF8Encoding(false).GetBytes(text));
    public Task<BuildResult> BuildAsync() => new IndexBuilder(new FileCatalog(), new StrictTextReader()).BuildAsync(
        new(new(Root, CorpusPresets.Mixed), Path.Combine(Root, ".index")), null, default);
    public string WriteBytes(string relativePath, byte[] bytes)
    {
        string path = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Fixture path escapes root.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    public async Task GitAsync(string relativeDirectory, params string[] args)
    {
        string path = Path.Combine(Root, relativeDirectory);
        Directory.CreateDirectory(path);
        var info = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-c", "safe.directory=" + path.Replace('\\', '/'), "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid" }.Concat(args)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException(await stderr);
    }
    public void Dispose()
    {
        if (!Root.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe cleanup.");
        // 不递归清理 reparse point：夹具目录仅在确认没有链接后整体删除。
        if (Directory.Exists(Root)) DeleteOwnedDirectory(Root);
    }
    private static void DeleteOwnedDirectory(string path)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            { if (attributes.HasFlag(FileAttributes.Directory)) Directory.Delete(entry); else File.Delete(entry); }
            else if (attributes.HasFlag(FileAttributes.Directory)) DeleteOwnedDirectory(entry);
            else { File.SetAttributes(entry, FileAttributes.Normal); File.Delete(entry); }
        }
        Directory.Delete(path);
    }
}
