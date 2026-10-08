using System.ComponentModel;
using System.Diagnostics;
namespace SourceSearch.Core;
public enum WorkspaceKind { Plain, Git, Unknown }
public sealed record WorkspaceIdentity(WorkspaceKind Kind, string? RepositoryRoot = null, string? Branch = null, string? Commit = null, string? Error = null)
{
    public string Label => Kind == WorkspaceKind.Plain ? "非 Git 文件夹" : Kind == WorkspaceKind.Unknown ? "分支未识别" : Branch ?? $"游离 {Commit?[..Math.Min(8, Commit.Length)]}";
    public bool DiffersFrom(WorkspaceIdentity other) => Kind != WorkspaceKind.Unknown && other.Kind != WorkspaceKind.Unknown &&
        (Kind != other.Kind || !string.Equals(RepositoryRoot, other.RepositoryRoot, StringComparison.OrdinalIgnoreCase) || Branch != other.Branch || Commit != other.Commit);
}
public interface IWorkspaceIdentityReader { Task<WorkspaceIdentity> ReadAsync(string root, CancellationToken ct); }
public sealed class WorkspaceIdentityReader : IWorkspaceIdentityReader
{
    public async Task<WorkspaceIdentity> ReadAsync(string root, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!Directory.Exists(root)) return new(WorkspaceKind.Unknown, Error: "目录不存在或不可访问。");
            string repository = GitFileEnumerator.FindRepositoryRoot(root);
            if (!File.Exists(Path.Combine(repository, ".git")) && !Directory.Exists(Path.Combine(repository, ".git"))) return new(WorkspaceKind.Plain);
            async Task<(int Exit, string Output)> Git(params string[] args)
            {
                var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { "-c", "safe.directory=" + repository.Replace('\\', '/'), "-C", root }.Concat(args)) info.ArgumentList.Add(arg);
                using var process = Process.Start(info) ?? throw new IOException("无法启动 Git。");
                using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
                return (process.ExitCode, output.Result.Trim());
            }
            var branch = await Git("symbolic-ref", "--quiet", "--short", "HEAD").ConfigureAwait(false);
            var commit = await Git("rev-parse", "--verify", "HEAD").ConfigureAwait(false);
            if (branch.Exit is not (0 or 1) || (commit.Exit != 0 && branch.Exit != 0))
                return new(WorkspaceKind.Unknown, repository, Error: "无法读取 Git HEAD；请检查仓库元数据。");
            return new(WorkspaceKind.Git, repository, branch.Exit == 0 ? branch.Output : null, commit.Exit == 0 ? commit.Output : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(WorkspaceKind.Unknown, Error: "读取 Git 身份超时。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        { return new(WorkspaceKind.Unknown, Error: error.Message); }
    }
}
