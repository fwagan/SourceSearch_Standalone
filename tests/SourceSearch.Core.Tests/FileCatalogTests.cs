namespace SourceSearch.Core.Tests;
public class FileCatalogTests
{
    [Fact] public async Task NonGitCatalogExcludesBuildOutputsAndExplicitRoots()
    {
        using var corpus = new TestCorpus();
        corpus.Write("源码 空格/User.cs", "class User {}");
        corpus.Write("bin/Generated.cs", "class Generated {}");
        corpus.Write("node_modules/lib.js", "x");
        corpus.Write(".index/metadata.json", "{}");
        var result = await new FileCatalog().EnumerateAsync(new(corpus.Root, CorpusPresets.Mixed, ExcludedRoots: [Path.Combine(corpus.Root, ".index")]), default);
        Assert.EndsWith("User.cs", Assert.Single(result.Files).FullPath);
    }
    [Fact] public async Task GitCatalogIncludesInitializedSubmoduleAndNonignoredUntrackedFiles()
    {
        using var corpus = new TestCorpus();
        await corpus.GitAsync("", "init");
        corpus.Write(".gitignore", "ignored.cs\n");
        corpus.Write("tracked.cs", "User");
        corpus.Write("ignored.cs", "Hidden");
        await corpus.GitAsync("", "add", "tracked.cs", ".gitignore");
        await corpus.GitAsync("sub", "init");
        corpus.Write("sub/Child.cs", "User");
        await corpus.GitAsync("sub", "add", "Child.cs");
        await corpus.GitAsync("sub", "commit", "-m", "fixture");
        await corpus.GitAsync("", "add", "sub");
        corpus.Write("新文件 空格.cs", "User");
        var result = await new FileCatalog().EnumerateAsync(new(corpus.Root, CorpusPresets.CSharp), default);
        Assert.Equal(3, result.Files.Count);
        Assert.Contains(result.Files, f => f.RelativePath.Replace('\\', '/') == "sub/Child.cs");
        Assert.DoesNotContain(result.Files, f => f.FullPath.EndsWith("ignored.cs"));
    }
    [Fact] public void NullDelimitedPathsPreserveWhitespace() => Assert.Equal(
        new[] { "a b.cs", "中文\n名字.cs" }, GitFileEnumerator.ParseNulPaths(System.Text.Encoding.UTF8.GetBytes("a b.cs\0中文\n名字.cs\0")));
    [Fact] public async Task CatalogDoesNotFollowDirectorySymlink()
    {
        using var corpus = new TestCorpus();
        corpus.Write("real.cs", "x");
        Directory.CreateSymbolicLink(Path.Combine(corpus.Root, "loop"), corpus.Root);
        var result = await new FileCatalog().EnumerateAsync(new(corpus.Root, CorpusPresets.CSharp), default);
        Assert.Single(result.Files);
    }
    [Fact] public async Task CatalogObservesCancellation()
    {
        using var corpus = new TestCorpus();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileCatalog().EnumerateAsync(new(corpus.Root, CorpusPresets.CSharp), new CancellationToken(true)));
    }
}
