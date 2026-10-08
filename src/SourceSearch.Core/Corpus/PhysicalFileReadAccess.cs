namespace SourceSearch.Core;
internal sealed class FileTooLargeException : IOException { }
internal sealed class PhysicalFileReadAccess : IFileReadAccess
{
    public FileStamp GetStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new(true, info.Length, info.LastWriteTimeUtc, info.Attributes) : new(false, 0, default, default);
    }
    public async Task<byte[]> ReadBytesAsync(string path, long maxBytes, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
        using var memory = new MemoryStream((int)Math.Min(stream.Length, 1024 * 1024));
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + count > maxBytes) throw new FileTooLargeException();
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
}
