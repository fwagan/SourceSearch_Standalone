namespace SourceSearch.Core.Tests;
public class WorkspaceIdentityTests
{
    [Fact]
    public async Task Plain_folder_is_distinct_from_unknown()
    {
        using var corpus = new TestCorpus();
        Assert.Equal(WorkspaceKind.Plain, (await new WorkspaceIdentityReader().ReadAsync(corpus.Root, default)).Kind);
    }
    [Fact]
    public async Task Git_subfolder_unborn_branch_commit_and_detached_head_are_identified()
    {
        using var corpus = new TestCorpus(); await corpus.GitAsync("", "init", "-b", "test-branch");
        corpus.Write("src/a.cs", "User"); var reader = new WorkspaceIdentityReader();
        var unborn = await reader.ReadAsync(Path.Combine(corpus.Root, "src"), default);
        Assert.Equal(WorkspaceKind.Git, unborn.Kind); Assert.Equal("test-branch", unborn.Branch); Assert.Null(unborn.Commit);
        Assert.Equal(corpus.Root, unborn.RepositoryRoot);
        await corpus.GitAsync("", "add", "."); await corpus.GitAsync("", "commit", "-m", "fixture");
        var first = await reader.ReadAsync(corpus.Root, default); Assert.NotNull(first.Commit); Assert.True(first.DiffersFrom(unborn));
        await corpus.GitAsync("", "checkout", "--detach");
        var detached = await reader.ReadAsync(corpus.Root, default);
        Assert.Null(detached.Branch); Assert.Equal(first.Commit, detached.Commit); Assert.StartsWith("游离", detached.Label);
        Assert.True(detached.DiffersFrom(first));
    }
    [Fact]
    public async Task Broken_git_metadata_is_unknown_and_cancellation_is_not_swallowed()
    {
        using var corpus = new TestCorpus(); corpus.Write(".git", "gitdir: missing");
        var reader = new WorkspaceIdentityReader();
        Assert.Equal(WorkspaceKind.Unknown, (await reader.ReadAsync(corpus.Root, default)).Kind);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(corpus.Root, new CancellationToken(true)));
    }
}
