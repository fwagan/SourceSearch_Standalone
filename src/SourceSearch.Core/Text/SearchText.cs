using System.Buffers;
using System.Text;
namespace SourceSearch.Core;
internal sealed record SearchText(string Value, NormalizedText? Mapping)
{
    public static SearchText Create(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            string value = string.Create(text.Length, (text, ct), (output, state) =>
            {
                int offset = 0;
                while (offset < state.text.Length)
                {
                    state.ct.ThrowIfCancellationRequested();
                    int count = Math.Min(4096, state.text.Length - offset);
                    Ascii.ToLower(state.text.AsSpan(offset, count), output.Slice(offset, count), out int written);
                    offset += written;
                    if (written == count) continue;
                    if (Rune.DecodeFromUtf16(state.text.AsSpan(offset), out var rune, out int consumed) != OperationStatus.Done)
                        throw new ArgumentException("Text contains an invalid UTF-16 sequence.", nameof(text));
                    var lower = Rune.ToLowerInvariant(rune);
                    if (lower.Utf16SequenceLength != consumed) throw new WidthChangedException();
                    lower.EncodeToUtf16(output[offset..]); offset += consumed;
                }
            });
            return new(value, null);
        }
        catch (WidthChangedException)
        {
            // Preserve offsets if a future Unicode mapping changes UTF-16 width.
            var mapped = TextRules.Normalize(text, ct);
            return new(mapped.Value, mapped);
        }
    }
    private sealed class WidthChangedException : Exception;
}
