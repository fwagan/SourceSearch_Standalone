using SourceSearch.Core;
namespace SourceSearch.Presentation;
public enum IndexPhase { NotBuilt, Building, Ready, Failed, Cancelled, Stale }
public sealed partial class SearchViewModel
{
    private Task? identityRefresh;
    private string? refreshingRoot;
    public WorkspaceIdentity Identity { get; private set; } = new(WorkspaceKind.Unknown);
    public WorkspaceIdentity? IndexedIdentity { get; private set; }
    public string IndexedRoot { get; private set; } = "";
    public IndexPhase Phase { get; private set; }
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public async Task SwitchFolderAsync(string path, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        string next = NormalizePath(path);
        if (string.Equals(next, RootPath, StringComparison.OrdinalIgnoreCase)) return;
        if (!CanEdit) throw new InvalidOperationException("Workspace is busy.");
        if (!Directory.Exists(next)) throw new DirectoryNotFoundException(next);
        isSwitching = true; workspaceVersion++; identityRefresh = null;
        InvalidateQuery(); ClearResults(); Set(ref rootPath, next, nameof(RootPath));
        Identity = new(WorkspaceKind.Unknown); IndexedRoot = ""; IndexedIdentity = null; stale = false; IndexError = ""; Phase = IndexPhase.NotBuilt;
        NotifyState();
        try { await controller.ClearAsync(ct); await RefreshIdentityAsync(); }
        catch (Exception error) when (error is not OperationCanceledException) { IndexError = "切换目录失败：" + error.Message; Phase = IndexPhase.Failed; throw; }
        finally { isSwitching = false; NotifyState(); }
    }
    public Task RefreshIdentityAsync()
    {
        if (closed || isBuilding) return Task.CompletedTask;
        string root = RootPath;
        if (identityRefresh is { IsCompleted: false } && refreshingRoot == root) return identityRefresh;
        refreshingRoot = root;
        return identityRefresh = ReadIdentityAsync(root, workspaceVersion);
    }
    private async Task ReadIdentityAsync(string root, long generation)
    {
        try
        {
            var identity = await identityReader.ReadAsync(root, lifetime.Token);
            if (!closed && generation == workspaceVersion && !isBuilding) { ApplyIdentity(identity); NotifyState(); }
        }
        catch (OperationCanceledException) when (closed) { }
    }
    private void ApplyIdentity(WorkspaceIdentity identity)
    {
        Identity = identity;
        if (Report is not null && IndexedIdentity is not null && identity.DiffersFrom(IndexedIdentity))
        { stale = true; Phase = IndexPhase.Stale; InvalidateQuery(); ClearResults(); }
    }
}
