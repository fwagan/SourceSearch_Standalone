using System.Security.Cryptography;
using System.Text;
namespace SourceSearch.Core;
public sealed class StrictTextReader : ITextSource
{
    private readonly IFileReadAccess access;
    public StrictTextReader() : this(new PhysicalFileReadAccess()) { }
    internal StrictTextReader(IFileReadAccess access) => this.access = access;
    public async Task<TextReadResult> ReadAsync(SourceFile file, long maxBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var before = access.GetStamp(file.FullPath);
                if (!before.Exists) return new(null, "Missing");
                long length = before.Length;
                DateTime stamp = before.LastWriteUtc;
                if (length > maxBytes) return new(null, "TooLarge");
                if (before.Attributes.HasFlag(FileAttributes.ReparsePoint)) return new(null, "ReparsePoint");
                byte[] data = await access.ReadBytesAsync(file.FullPath, maxBytes, ct).ConfigureAwait(false);
                var after = access.GetStamp(file.FullPath);
                if (!after.Exists || after.Length != length || after.LastWriteUtc != stamp || data.LongLength != length) continue;
                var (encoding, bom) = DetectEncoding(data);
                if (bom == 0 && data.Contains((byte)0)) return new(null, "Binary");
                string text = encoding.GetString(data, bom, data.Length - bom);
                return new(new(text, Convert.ToHexString(SHA256.HashData(data)), length, stamp), null);
            }
            catch (DecoderFallbackException) { return new(null, "InvalidEncoding"); }
            catch (FileNotFoundException) { return new(null, "Missing"); }
            catch (DirectoryNotFoundException) { return new(null, "Missing"); }
            catch (UnauthorizedAccessException) { return new(null, "AccessDenied"); }
            catch (FileTooLargeException) { return new(null, "TooLarge"); }
            catch (IOException) { return new(null, "ReadError"); }
        }
        return new(null, "ChangedDuringRead");
    }

    private static (Encoding Encoding, int Bom) DetectEncoding(byte[] data)
    {
        if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) return (new UTF32Encoding(false, true, true), 4);
        if (data.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) return (new UTF32Encoding(true, true, true), 4);
        if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, true, true), 2);
        if (data.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, true, true), 2);
        if (data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(false, true), 3);
        return (new UTF8Encoding(false, true), 0);
    }
}
