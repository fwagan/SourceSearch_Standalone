using System.Diagnostics;
using System.Text;
namespace SourceSearch.Core;
public static class GitFileEnumerator
{
    public static string[] ParseNulPaths(byte[] data) => new UTF8Encoding(false, true).GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    internal static async Task<(string[] Files, string[] Submodules)> ReadAsync(string directory, CancellationToken ct)
    {
        string[] files = ParseNulPaths(await RunAsync(directory, ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], ct));
        string[] staged = ParseNulPaths(await RunAsync(directory, ["ls-files", "--stage", "-z"], ct));
        string[] submodules = staged.Where(s => s.StartsWith("160000 ", StringComparison.Ordinal))
            .Select(s => s[(s.IndexOf('\t') + 1)..]).ToArray();
        return (files, submodules);
    }

    private static async Task<byte[]> RunAsync(string directory, string[] arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // 授权只作用于当前明确打开的目录，不改全局 Git 配置。
        foreach (string arg in new[] { "-c", "safe.directory=" + FindRepositoryRoot(directory).Replace('\\', '/'), "-C", directory }.Concat(arguments)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Unable to start Git.");
        using var registration = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, ct);
        var errors = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(copy, process.WaitForExitAsync(ct)).ConfigureAwait(false);
        string error = await errors.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException($"Git enumeration failed ({process.ExitCode}): {error.Trim()}");
        return output.ToArray();
    }

    internal static string FindRepositoryRoot(string directory)
    {
        for (DirectoryInfo? item = new(directory); item != null; item = item.Parent)
            if (File.Exists(Path.Combine(item.FullName, ".git")) || Directory.Exists(Path.Combine(item.FullName, ".git"))) return item.FullName;
        return directory;
    }
}
