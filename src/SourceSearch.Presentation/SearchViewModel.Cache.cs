using SourceSearch.Core;
namespace SourceSearch.Presentation;
public sealed partial class SearchViewModel
{
    private Task? activation;
    public string CacheSummary { get; private set; } = "";
    public Task ActivateAsync() => activation is { IsCompleted:false } ? activation : activation = ActivateCoreAsync();
    private async Task ActivateCoreAsync()
    {
        long generation = workspaceVersion;
        await RefreshIdentityAsync();
        if (!closed && CanEdit && generation == workspaceVersion) await LoadAvailableCacheAsync();
    }
    private async Task LoadAvailableCacheAsync()
    {
        if (cache is null || !CanEdit || (Report is not null && !stale)) return;
        try
        {
            if (stale)
            {
                isSwitching = true; NotifyState();
                try
                {
                    await controller.ClearAsync(lifetime.Token);
                    IndexedRoot = ""; IndexedIdentity = null; stale = false; Phase = IndexPhase.NotBuilt; CacheSummary = "";
                }
                finally { isSwitching = false; NotifyState(); }
            }
            if (!closed && Identity.Kind == WorkspaceKind.Git && cache.Exists(new(RootPath,CorpusPresets.Mixed),Identity))
                await BuildCoreAsync(false,lifetime.Token);
        }
        catch(OperationCanceledException) when(closed) { }
        catch(Exception error) when(!closed) { IndexError = "加载缓存失败：" + error.Message; Phase = IndexPhase.Failed; NotifyState(); }
    }
}
