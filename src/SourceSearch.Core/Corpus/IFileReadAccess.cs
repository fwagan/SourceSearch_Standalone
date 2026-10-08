namespace SourceSearch.Core;
internal readonly record struct FileStamp(bool Exists, long Length, DateTime LastWriteUtc, FileAttributes Attributes);
internal interface IFileReadAccess
{
    FileStamp GetStamp(string path);
    Task<byte[]> ReadBytesAsync(string path, long maxBytes, CancellationToken ct);
}
