using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
namespace SourceSearch.Core;
internal sealed class LineTokenStream : TokenStream
{
    private readonly IEnumerable<string> terms;
    private readonly CancellationToken cancellationToken;
    private readonly ICharTermAttribute term;
    private IEnumerator<string>? iterator;
    public LineTokenStream(IEnumerable<string> terms, CancellationToken cancellationToken)
    { this.terms = terms; this.cancellationToken = cancellationToken; term = AddAttribute<ICharTermAttribute>(); }
    public override void Reset()
    { base.Reset(); iterator?.Dispose(); iterator = terms.GetEnumerator(); }
    public override bool IncrementToken()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (iterator is null || !iterator.MoveNext()) return false;
        ClearAttributes();
        term.SetEmpty().Append(iterator.Current);
        return true;
    }
    protected override void Dispose(bool disposing)
    { if (disposing) iterator?.Dispose(); base.Dispose(disposing); }
}
