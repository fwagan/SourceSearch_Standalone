using System.Text;
namespace SourceSearch.Core.Tests;
public class StrictTextReaderTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task UnstableReadRetriesOnceAndNeverReturnsMixedSnapshot(bool alwaysChanges)
    {
        var access = new ChangingAccess(alwaysChanges);
        var result = await new StrictTextReader(access).ReadAsync(new("source.cs", "source.cs", ".cs"), 100, default);
        if (alwaysChanges) { Assert.Null(result.Content); Assert.Equal("ChangedDuringRead", result.SkipReason); }
        else Assert.Equal("User", result.Content?.Text);
        Assert.Equal(2, access.Reads);
    }
    private sealed class ChangingAccess(bool alwaysChanges) : IFileReadAccess
    {
        public int Reads { get; private set; }
        private int stamps;
        public FileStamp GetStamp(string path)
        {
            stamps++;
            int value = alwaysChanges ? stamps : Math.Min(stamps, 2);
            return new(true, 4, DateTime.UnixEpoch.AddSeconds(value), FileAttributes.Normal);
        }
        public Task<byte[]> ReadBytesAsync(string path, long maxBytes, CancellationToken ct)
        { Reads++; return Task.FromResult(Encoding.UTF8.GetBytes("User")); }
    }
    private static SourceFile Source(string path) => new(path, Path.GetFileName(path), ".cs");
    [Fact] public async Task InvalidUtf8IsReportedInsteadOfReplaced()
    {
        using var corpus = new TestCorpus();
        var result = await new StrictTextReader().ReadAsync(Source(corpus.WriteBytes("bad.cs", [0xC3, 0x28])), 32 * 1024 * 1024, default);
        Assert.Null(result.Content);
        Assert.Equal("InvalidEncoding", result.SkipReason);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task ReadsSupportedEncodingsWithoutBomText(int kind)
    {
        Encoding[] encodings = [new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true)];
        var encoding = encodings[kind];
        using var corpus = new TestCorpus();
        string path = corpus.WriteBytes("编码.cs", encoding.GetPreamble().Concat(encoding.GetBytes("用户\r\n😀User")).ToArray());
        var result = await new StrictTextReader().ReadAsync(Source(path), 1024, default);
        Assert.Equal("用户\r\n😀User", result.Content?.Text);
        Assert.Equal(64, result.Content?.Sha256.Length);
    }
    [Fact] public async Task EnforcesSizeAndReportsMissingFile()
    {
        using var corpus = new TestCorpus();
        var source = Source(corpus.Write("big.cs", "0123456789"));
        Assert.Equal("TooLarge", (await new StrictTextReader().ReadAsync(source, 9, default)).SkipReason);
        File.Delete(source.FullPath);
        Assert.Equal("Missing", (await new StrictTextReader().ReadAsync(source, 1024, default)).SkipReason);
    }
    [Fact] public async Task EmptyFilesAreValid()
    {
        using var corpus = new TestCorpus();
        Assert.Equal("", (await new StrictTextReader().ReadAsync(Source(corpus.Write("empty.cs", "")), 1024, default)).Content?.Text);
    }
    [Fact] public async Task ReadObservesCancellation()
    {
        using var corpus = new TestCorpus();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StrictTextReader().ReadAsync(Source(corpus.Write("a.cs", "x")), 100, new CancellationToken(true)));
    }
}
