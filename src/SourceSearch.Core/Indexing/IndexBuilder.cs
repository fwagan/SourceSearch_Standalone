using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
namespace SourceSearch.Core;
public sealed class IndexBuilder(IFileCatalog catalog, ITextSource source) : IIndexBuilder
{
    private static readonly FieldType TokenField = CreateFieldType();
    public Task<BuildResult> BuildAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct) =>
        Task.Run(() => BuildCoreAsync(options, progress, ct), ct);

    private async Task<BuildResult> BuildCoreAsync(IndexBuildOptions options, IProgress<BuildProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        Guid id = Guid.NewGuid();
        string root = Path.GetFullPath(options.IndexRoot);
        string path = Path.Combine(root, id.ToString("N"));
        System.IO.Directory.CreateDirectory(path);
        var input = options.Catalog with { ExcludedRoots = (options.Catalog.ExcludedRoots ?? []).Append(root).ToArray() };
        var list = await catalog.EnumerateAsync(input, ct).ConfigureAwait(false);
        var files = new List<IndexedFile>();
        var issues = new List<CatalogIssue>(list.Issues);
        long lines = 0;
        int processed = 0, skipped = 0;
        using (var directory = FSDirectory.Open(new DirectoryInfo(Path.Combine(path, "index"))))
        using (var analyzer = new ExplicitStreamsAnalyzer())
        {
            var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer) { OpenMode = OpenMode.CREATE, RAMBufferSizeMB = 256 };
            var writer = new IndexWriter(directory, config);
            try
            {
                foreach (var file in list.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    var read = await source.ReadAsync(file, input.MaxFileBytes, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    processed++;
                    if (read.Content is null) { issues.Add(new(file.FullPath, read.SkipReason ?? "Unreadable")); skipped++; }
                    else
                    {
                        int fileId = files.Count, lineNumber = 0;
                        using var reader = new StringReader(read.Content.Text);
                        string? line;
                        while ((line = reader.ReadLine()) is not null)
                        {
                            ct.ThrowIfCancellationRequested();
                            string normalized = TextRules.Normalize(line, ct).Value;
                            var document = new Document
                            {
                                new StoredField("file_id", fileId),
                                new StoredField("line_number", ++lineNumber),
                                new StoredField("text", line),
                                new StringField("extension", file.Extension, Field.Store.NO),
                                new Field("words", new LineTokenStream(TextRules.EnumerateWords(normalized).Where(w => Encoding.UTF8.GetByteCount(w) <= IndexWriter.MAX_TERM_LENGTH), ct), TokenField)
                            };
                            for (int width = 1; width <= 3; width++)
                                document.Add(new Field("gram" + width, new LineTokenStream(TextRules.EnumerateGrams(normalized, width), ct), TokenField));
                            writer.AddDocument(document);
                            lines++;
                        }
                        files.Add(new(fileId, file.FullPath, file.Extension, read.Content.Bytes, read.Content.LastWriteUtc, read.Content.Sha256, lineNumber));
                    }
                    progress?.Report(new(processed, files.Count, skipped));
                }
                ct.ThrowIfCancellationRequested();
                writer.Commit();
                writer.Dispose();
            }
            catch
            {
                writer.Rollback();
                // 半成品保留在本次唯一目录，缺少 completed 标记，因此绝不会当作有效快照打开。
                throw;
            }
            using var check = DirectoryReader.Open(directory);
            if (check.NumDocs != lines) throw new InvalidDataException("Indexed line count is inconsistent.");
        }
        ct.ThrowIfCancellationRequested();
        long bytes = System.IO.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        var report = new BuildReport(id, DateTime.UtcNow, files, issues, lines, bytes, timer.Elapsed);
        var manifest = new IndexManifest(1, Path.GetFullPath(input.RootPath), input.Extensions.Order(StringComparer.Ordinal).ToArray(), input.MaxFileBytes, report);
        await File.WriteAllTextAsync(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(manifest), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(path, "completed.tmp"), id.ToString("D"), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        File.Move(Path.Combine(path, "completed.tmp"), Path.Combine(path, "completed"));
        return new(IndexSnapshot.Open(path), report);
    }

    private static FieldType CreateFieldType()
    {
        var type = new FieldType { IsIndexed = true, IsTokenized = true, OmitNorms = true, IndexOptions = IndexOptions.DOCS_ONLY };
        type.Freeze();
        return type;
    }
    private sealed class ExplicitStreamsAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader) =>
            throw new InvalidOperationException("Tokenized fields must supply explicit streams.");
    }
}
