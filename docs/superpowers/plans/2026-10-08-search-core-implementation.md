# Source Search 检索核心与最小 UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在实际 Ftrans 源码上验证默认包含搜索、可选全字匹配和后缀过滤的正确性及首批结果性能，并交付最小 WPF 验证界面。

**Architecture:** Core 负责文件枚举、文本契约、不可变 Lucene 索引快照和分页查询；Validation 负责独立正确性基准和可重复性能报告；Desktop 只调用 Core 并负责异步交互、呈现和端到端计时。先验证检索，再接界面；搜索过程中不扫描文件树，不读取当前源文件生成片段。

**Tech Stack:** .NET SDK 10.0.401、C#、net10.0/net10.0-windows、WPF、Lucene.Net 4.8.0-beta00018、xunit 2.9.3、xunit.runner.visualstudio 3.0.2、Microsoft.NET.Test.Sdk 17.12.0。

**Spec:** [已批准设计](../specs/2026-10-08-search-core-design.md)，Git 基线 `5df1398`；[样本清点](../specs/2026-10-08-corpus-inventory.json)。用户于 2026-10-08 批准提交并执行本计划，执行方式为当前会话顺序实施。

## Global Constraints

- 开发目录：`C:\Coding\Tools\Source Search`。
- 只读样本：`C:\Coding\Ftrans\ftrans.main`。索引、测试夹具和报告均写入工具目录，不修改样本、不执行样本内脚本或构建。
- 本阶段采用 C#、.NET 10、WPF、Lucene.NET。
- 默认逐行的字面量包含匹配，不解释通配符、正则或 Lucene 查询语法；空查询不执行，换行查询提示暂不支持。
- 默认大小写不敏感，对 Unicode 标量逐个执行 `Rune.ToLowerInvariant`，并保留映射到原始 UTF-16 偏移的关系；查询、词索引和 n-gram 索引共用该规则。不删除重音，不做 Unicode 兼容等价替换，不进行多字符展开。n-gram 长度按 Unicode 标量计数。
- 全字匹配要求命中两侧是行边界或非词字符。词字符定义为 Unicode 字母、组合标记、十进制数字和连接标点（包括下划线）。驼峰不是全字边界。
- 编码支持严格 UTF-8（含 BOM）、带 BOM 的 UTF-16/UTF-32；无 BOM 且 UTF-8 无法解码的文件跳过并报告，不静默替换字符。
- 单文件保护上限先设 32 MiB，超限明确报告；此限制必须写入测试报告。
- 首批最多 100 条，后续每批 100 条，原型展示上限 1,000 条，提示已达到上限；正确性校验工具可遍历全部结果，不受 UI 上限影响。
- 每个固定查询组合预热 5 次、计时至少 30 次，输出逐查询 P50/P95/最大耗时，不能只给总体平均值。
- 目标：在此电脑和上述语料下，预热查询从提交到首批最多 100 个真实命中在 UI 呈现的 P95 不超过 100 ms。核心引擎延迟与 UI 延迟分别测量；CLI 结果不能冒充 UI 结果。
- repo + branch 缓存复用、增量更新、文件监听、缓存管理器、模糊搜索和正则均为后续阶段。
- 依赖固定版本并提交 packages.lock.json；生成索引和测试临时数据不纳入 Git。只提交脱离原始源码片段仍可复核的汇总报告。
- 下列命令的工作目录均为本工具目录。Git 子模块只读遍历，不执行 fetch、checkout 或 submodule update。测试 Git 操作只发生在 `.tmp/tests/` 的自建夹具。

## Review Focus

1. Windows 路径含中文、空格、换行，或遇到 junction 循环：准确枚举且不越出目录；任务 2 用 NUL 路径解析及重解析点测试固定行为。
2. 大小写映射和补充平面字符导致偏移错位：返回原始 UTF-16 命中范围；任务 1 用 Deseret 字母、组合标记和 emoji 测试。
3. 高频片段大量共现但真实命中很少：跨越多页候选仍不能漏报；任务 4 用超过 100 个假候选和长查询测试。
4. 超长标识符、超长行和引擎词条/子句上限：不能截断索引、异常退出或改成整库扫描；任务 3/4 测试超长词条及有限候选子句后核验。
5. 重建取消、文件读到一半变化、旧搜索晚到：旧快照保持可用，结果不串批；任务 2/3/7 分别覆盖文件稳定性、快照发布及请求代号。

## 文件结构与接口归属

```text
SourceSearch.slnx
global.json
Directory.Build.props
.gitignore
README.md
src/SourceSearch.Core/
  SourceSearch.Core.csproj
  SearchContracts.cs
  Text/NormalizedText.cs
  Text/TextRules.cs
  Text/LiteralMatcher.cs
  Text/ExtensionFilter.cs
  Corpus/CatalogContracts.cs
  Corpus/FileCatalog.cs
  Corpus/GitFileEnumerator.cs
  Corpus/StrictTextReader.cs
  Corpus/CorpusPresets.cs
  Indexing/IndexContracts.cs
  Indexing/LineTokenStream.cs
  Indexing/IndexBuilder.cs
  Indexing/IndexSnapshot.cs
  Search/CandidateQueryBuilder.cs
  Search/SearchService.cs
  Search/SearchController.cs
src/SourceSearch.Validation/
  SourceSearch.Validation.csproj
  Program.cs
  ReferenceMatcher.cs
  CorrectnessVerifier.cs
  BenchmarkRunner.cs
  BenchmarkReport.cs
  QuerySetBuilder.cs
src/SourceSearch.Desktop/
  SourceSearch.Desktop.csproj
  App.xaml / App.xaml.cs
  MainWindow.xaml / MainWindow.xaml.cs
  SearchViewModel.cs
  PresentationTimer.cs
  UiBenchmarkRunner.cs
tests/SourceSearch.Core.Tests/
  SourceSearch.Core.Tests.csproj
  TextRulesTests.cs
  LiteralMatcherTests.cs
  ExtensionFilterTests.cs
  FileCatalogTests.cs
  StrictTextReaderTests.cs
  IndexBuilderTests.cs
  SearchServiceTests.cs
  SearchControllerTests.cs
  ValidationTests.cs
  TestCorpus.cs
scripts/validate-core.ps1
scripts/validate-ui.ps1
docs/validation/search-core/README.md
```

Core 不引用 Desktop、Validation 或测试框架。Validation 引用 Core；Desktop 引用 Core 和 Validation 的报告/查询集模块；测试引用 Core 与 Validation。测试构造器不引用真实样本路径。每个类型在下面所属任务中定义，后续任务复用相同签名。

## Task 1：可执行的查询语义与测试工程

**Files:** 创建根级解决方案、global.json、Directory.Build.props、.gitignore；Core 项目、SearchContracts.cs、Text 下四个文件；测试项目及 TextRulesTests.cs、LiteralMatcherTests.cs、ExtensionFilterTests.cs。

**Interfaces — Produces:**

```csharp
public enum MatchMode { Contains, WholeWord }
public readonly record struct TextMatch(int Start, int Length);
public sealed record ExtensionFilter(
    IReadOnlySet<string> Include, IReadOnlySet<string> Exclude)
{
    public static ExtensionFilter Parse(bool includeEnabled, string includeText,
        bool excludeEnabled, string excludeText);
    public bool Allows(string extension);
}
public sealed record SearchRequest(string Text, MatchMode Mode, ExtensionFilter Filter);
public sealed record SearchHit(string FilePath, int LineNumber, string LineText,
    IReadOnlyList<TextMatch> Matches);
public sealed record SearchCursor(Guid SnapshotId, string RequestKey, int AfterDocId);
public sealed record SearchPage(IReadOnlyList<SearchHit> Hits, SearchCursor? Next,
    bool Exhausted, long CandidatesVisited, TimeSpan Elapsed);
public sealed record NormalizedText(string Value, int[] OriginalStarts, int[] OriginalEnds);
// 映射数组按规范化 UTF-16 索引排列，代理对的两个 code unit 指向同一原始标量范围。
public static class TextRules
{
    public static NormalizedText Normalize(string text);
    public static bool IsWordRune(Rune rune);
    public static IEnumerable<string> EnumerateWords(string normalized);
    public static IEnumerable<string> EnumerateGrams(string normalized, int width);
}
public static class LiteralMatcher
{
    public static IReadOnlyList<TextMatch> Find(string line, string query,
        MatchMode mode, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1：创建项目文件和依赖配置，不编写行为实现。** 建立 net10.0 类库和 net10.0 测试项目，启用 Nullable、ImplicitUsings、RestorePackagesWithLockFile。测试包固定为本文件头部版本，Core 固定引用 Lucene.Net 4.8.0-beta00018。global.json 使用 `{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}`。还原失败时保留实际错误，不通过更改业务设计绕开。

```powershell
dotnet new sln --name SourceSearch --format slnx
dotnet new classlib --name SourceSearch.Core --output src/SourceSearch.Core --framework net10.0 --no-restore
dotnet new classlib --name SourceSearch.Core.Tests --output tests/SourceSearch.Core.Tests --framework net10.0 --no-restore
dotnet sln SourceSearch.slnx add src/SourceSearch.Core/SourceSearch.Core.csproj tests/SourceSearch.Core.Tests/SourceSearch.Core.Tests.csproj
```

删除模板 Class1.cs。测试项目设置 IsTestProject=true、IsPackable=false，添加 Core ProjectReference。`.gitignore` 忽略 `.vs/`、`**/bin/`、`**/obj/`、`.tmp/`、`artifacts/`、`.worktrees/`；不忽略 docs/validation 正式汇总。

- [ ] **Step 2：先加入以下失败测试及完整语义参数表。**

```csharp
[Theory]
[InlineData("User", 1, 1)]
[InlineData("GetUserInfo", 1, 0)]
[InlineData("User_Name", 1, 0)]
[InlineData("User2", 1, 0)]
[InlineData("List<User>", 1, 1)]
public void DistinguishesContainsAndWholeWord(string line, int contains, int whole)
{
    Assert.Equal(contains, LiteralMatcher.Find(line, "User", MatchMode.Contains).Count);
    Assert.Equal(whole, LiteralMatcher.Find(line, "User", MatchMode.WholeWord).Count);
}
[Fact]
public void PreservesOriginalOffsetsAcrossSupplementaryCharacters()
{
    var hits = LiteralMatcher.Find("😀𐐀User", "𐐨u", MatchMode.Contains);
    Assert.Equal(new TextMatch(2, 3), Assert.Single(hits));
}
[Fact]
public void ExclusionWinsAndDisabledConditionsAreIgnored()
{
    var filter = ExtensionFilter.Parse(true, " cs, .JSON,cs, ", true, ".cs");
    Assert.False(filter.Allows(".CS"));
    Assert.True(filter.Allows(".json"));
    Assert.True(ExtensionFilter.Parse(false, "cs", false, "json").Allows(".go"));
}
```

同一文件加入空查询、查询换行拒绝、字面量 `a.b`、组合标记边界、中文、`aaa` 中 `aa` 的重叠两次命中、下划线、数字、空白字符串查询、非法 UTF-16 查询拒绝、启用但空后缀列表等断言。空白查询保留原字符，不能 Trim 后改变含义。

- [ ] **Step 3：运行目标测试，确认未实现方法导致失败。** 命令：`dotnet test tests/SourceSearch.Core.Tests -c Release --filter "FullyQualifiedName~LiteralMatcherTests|FullyQualifiedName~ExtensionFilterTests|FullyQualifiedName~TextRulesTests"`。保存失败原因，不把依赖还原失败当成预期 RED。
- [ ] **Step 4：实现标量映射和字面量核验。** 核心循环规则：

```csharp
int sourceOffset = 0;
foreach (Rune rune in text.EnumerateRunes())
{
    Rune folded = Rune.ToLowerInvariant(rune);
    // 将 folded 写入 builder；为写入的每个 UTF-16 单元保存下面两个值。
    int originalStart = sourceOffset;
    int originalEnd = sourceOffset + rune.Utf16SequenceLength;
    sourceOffset = originalEnd;
}
// 匹配使用 StringComparison.Ordinal；成功后从下一标量位置继续，保留重叠命中。
// WholeWord 检查原文两侧标量，不把代理对拆开。
```

实现词字符 UnicodeCategory 白名单；grams 按标量窗口生成、含空格标点、不跨行。每处理一批标量及每次候选匹配检查取消。`.cs` 和 `cs` 统一成 `.cs`；空扩展名文件可被无过滤查询命中。

- [ ] **Step 5：重新运行 Step 3，要求所有语义测试 PASS。** 手工核对 offset 断言使用原文而非规范化文本。审核此任务 diff；建议提交信息 `feat(core): 定义搜索语义和后缀过滤`，执行提交需复用会话授权或遵循提交技能。

## Task 2：只读语料枚举与严格解码

**Files:** 创建 Corpus/CatalogContracts.cs、FileCatalog.cs、GitFileEnumerator.cs、StrictTextReader.cs、CorpusPresets.cs；测试 FileCatalogTests.cs、StrictTextReaderTests.cs、TestCorpus.cs。

**Interfaces — Produces:**

```csharp
public sealed record CatalogOptions(string RootPath, IReadOnlySet<string> Extensions,
    long MaxFileBytes = 32L * 1024 * 1024,
    IReadOnlyList<string>? ExcludedRoots = null);
public sealed record SourceFile(string FullPath, string RelativePath, string Extension);
public sealed record CatalogIssue(string Path, string Reason);
public sealed record CatalogResult(IReadOnlyList<SourceFile> Files,
    IReadOnlyList<CatalogIssue> Issues);
public sealed record StableText(string Text, string Sha256, long Bytes, DateTime LastWriteUtc);
public sealed record TextReadResult(StableText? Content, string? SkipReason);
public interface IFileCatalog
{
    Task<CatalogResult> EnumerateAsync(CatalogOptions options, CancellationToken ct);
}
public interface ITextSource
{
    Task<TextReadResult> ReadAsync(SourceFile file, long maxBytes, CancellationToken ct);
}
// FileCatalog : IFileCatalog；StrictTextReader : ITextSource。
public static class CorpusPresets
{
    public static IReadOnlySet<string> CSharp { get; }
    public static IReadOnlySet<string> Mixed { get; }
}
```

TestCorpus 实现 IDisposable，根目录固定在工作区 `.tmp/tests/<guid>`；提供 `string Root`、`string Write(string relativePath, string text)`、`string WriteBytes(string relativePath, byte[] bytes)`。清理前检查解析后的路径属于本次夹具且不是重解析点，绝不删除真实样本。

- [ ] **Step 1：创建夹具测试，验证下列只读输入。**

```csharp
[Fact]
public async Task NonGitCatalogExcludesBuildOutputs()
{
    using var corpus = new TestCorpus();
    corpus.Write("源码 空格/User.cs", "class User {}");
    corpus.Write("bin/Generated.cs", "class Generated {}");
    corpus.Write("node_modules/lib.js", "const User = 1;");
    var result = await new FileCatalog().EnumerateAsync(
        new CatalogOptions(corpus.Root, CorpusPresets.Mixed), CancellationToken.None);
    Assert.EndsWith("User.cs", Assert.Single(result.Files).FullPath);
}
[Fact]
public async Task InvalidUtf8IsReportedInsteadOfReplaced()
{
    using var corpus = new TestCorpus();
    string path = corpus.WriteBytes("bad.cs", new byte[] { 0xC3, 0x28 });
    var result = await new StrictTextReader().ReadAsync(
        new SourceFile(path, "bad.cs", ".cs"), 32L * 1024 * 1024, CancellationToken.None);
    Assert.Null(result.Content);
    Assert.Equal("InvalidEncoding", result.SkipReason);
}
```

补充：测试专用 Git 父/子仓库 fixture（仅本地 init/add/commit，身份通过每条命令 -c 参数提供），验证父仓库 gitlink 不替代子仓库内容、未跟踪文件和忽略文件、缺失子模块提示。NUL 解析函数用含换行/中文路径的字节流验证；Windows 不支持的文件名不用创建真实文件。junction 自环在系统允许时创建并验证不跟随；不允许创建时记录跳过原因并保留重解析属性判断的独立测试。

- [ ] **Step 2：运行 `dotnet test tests/SourceSearch.Core.Tests -c Release --filter "FullyQualifiedName~FileCatalogTests|FullyQualifiedName~StrictTextReaderTests"`，确认 RED。**
- [ ] **Step 3：实现安全枚举。** Git 使用 ProcessStartInfo.ArgumentList，禁止拼接 shell 命令；读取二进制 stdout、严格解析 UTF-8 NUL 分隔项，同时排空 stderr。参数形式：

```text
git -c safe.directory=<本次明确打开的仓库绝对路径> -C <repo> ls-files --cached --others --exclude-standard -z
git -c safe.directory=<repo> -C <repo> config --file .gitmodules --null --get-regexp ^submodule\..*\.path$
```

对 git config 输出使用其实际 key/newline/value/NUL 格式解析，不能按 ls-files 的路径格式直接复用。只在路径存在且 .git 文件/目录存在时递归；限制子模块路径在打开根目录内、检查所有祖先 reparse 属性。Git 不可用时按普通文件系统规则枚举并明确告知未应用 Git ignore；Git 命令异常不伪装成空目录。退出取消时终止本次启动的 Git 子进程。

- [ ] **Step 4：实现严格解码及稳定读。** 先检查 UTF-32 BOM，再检查 UTF-16 和 UTF-8 BOM；无 BOM 使用 `new UTF8Encoding(false, true)`。有 BOM 时同样启用异常回退，检查非法代理对。读取前后比较长度和 UTC 最后写入时间；不稳定时重试一次，仍不稳定返回 `ChangedDuringRead`。内容 SHA-256 在读到的字节上计算。限制按实际读取字节数执行，避免文件增长绕过上限。

```csharp
Encoding utf8 = new UTF8Encoding(false, true);
Encoding utf16Le = new UnicodeEncoding(false, true, true);
Encoding utf32Le = new UTF32Encoding(false, true, true);
// StringReader.ReadLine 保留一基行号；空文件没有行，末行无换行仍保留。
```

在 StrictTextReader 增加内部可注入读取/元数据适配器供测试模拟两次变化；测 UTF-8 BOM、UTF-16 LE/BE、UTF-32 LE/BE、空文件、超限、拒绝访问、文件删除、取消和变化重试。

- [ ] **Step 5：重跑目标测试，检查读取失败均有明确原因、没有样本写入。** 将 Mixed 后缀精确复制自 corpus-inventory.json，不通过手写另一套不同后缀缩小范围。建议提交 `feat(corpus): 枚举子模块并严格读取源码文本`。

## Task 3：构建不可变行索引及失败恢复

**Files:** 创建 Indexing/IndexContracts.cs、LineTokenStream.cs、IndexBuilder.cs、IndexSnapshot.cs；测试 IndexBuilderTests.cs；复用任务 1/2。

**Interfaces — Consumes:** `IFileCatalog`、`ITextSource`、`TextRules`。

**Interfaces — Produces:**

```csharp
public sealed record IndexBuildOptions(CatalogOptions Catalog, string IndexRoot);
public sealed record BuildProgress(int FilesProcessed, int FilesIndexed, int FilesSkipped);
public sealed record IndexedFile(int Id, string FullPath, string Extension, long Bytes,
    DateTime LastWriteUtc, string Sha256, int LineCount);
public sealed record BuildReport(Guid SnapshotId, DateTime CreatedUtc,
    IReadOnlyList<IndexedFile> Files, IReadOnlyList<CatalogIssue> Issues,
    long IndexedLines, long IndexBytes, TimeSpan Elapsed);
public sealed record BuildResult(IndexSnapshot Snapshot, BuildReport Report);
public interface IIndexBuilder
{
    Task<BuildResult> BuildAsync(IndexBuildOptions options,
        IProgress<BuildProgress>? progress, CancellationToken ct);
}
// IndexBuilder(IFileCatalog catalog, ITextSource source) : IIndexBuilder。
// IndexSnapshot : IDisposable；拥有 FSDirectory、DirectoryReader、IndexSearcher。
// public Guid Id { get; }; public BuildReport Report { get; };
// public string DirectoryPath { get; };
// public static IndexSnapshot Open(string snapshotPath);
// 引擎对象仅 internal 供 SearchService 使用；UI 不能直接依赖 Lucene 类型。
```

- [ ] **Step 1：先写索引测试。**

```csharp
[Fact]
public async Task IndexPreservesOriginalLineAndShortTerms()
{
    using var corpus = new TestCorpus();
    corpus.Write("User.cs", "// header\r\nGetUser(Id);\r\n");
    var builder = new IndexBuilder(new FileCatalog(), new StrictTextReader());
    var result = await builder.BuildAsync(
        new IndexBuildOptions(new CatalogOptions(corpus.Root, CorpusPresets.CSharp),
            Path.Combine(corpus.Root, ".index")), null, CancellationToken.None);
    using var snapshot = result.Snapshot;
    Assert.Equal(2, result.Report.IndexedLines);
    Assert.Equal(2, Assert.Single(result.Report.Files).LineCount);
}
```

另用 internal 检查或 InternalsVisibleTo 断言 gram1/2/3 字段包含 `i`、`id`、`use`，words 有 `getuser`，原文为 `GetUser(Id);`。加入 40,000 字符单标识符与超长行、空行、多次重复 gram、无可索引文件的测试。建立第二份索引时用故障 ITextSource/IFileCatalog 注入不可恢复失败，验证旧快照仍能打开，失败版本无 completed 标记。

- [ ] **Step 2：运行 `dotnet test tests/SourceSearch.Core.Tests -c Release --filter FullyQualifiedName~IndexBuilderTests`，确认 RED。**
- [ ] **Step 3：实现自定义 TokenStream 和文档字段。** 每个 TokenStream 按共享 Rune 规则产生 terms，不依赖引擎自带分词器的 Unicode 版本。包含以下固定字段：

```text
file_id       StoredField (int)
line_number   StoredField (int)
text          StoredField (原始行)
extension     StringField (精确过滤，不存储)
words         Field + 自定义词 TokenStream，IndexOptions.DOCS_ONLY
gram1         Field + 1 标量 TokenStream，IndexOptions.DOCS_ONLY
gram2         Field + 2 标量 TokenStream，IndexOptions.DOCS_ONLY
gram3         Field + 3 标量 TokenStream，IndexOptions.DOCS_ONLY
```

所有文本索引字段 IsTokenized=true、OmitNorms=true，不保存词频、位置或 term vectors；行内容提供最终连续匹配核验和偏移。IndexWriterConfig 以 256 MiB RAMBufferSizeMB 起步，测试记录实际峰值，不执行强制 ForceMerge。TokenStream 可去重同一行的重复 term，但不能为整个语料常驻一个无界去重集合。

完整词的 UTF-8 编码长度超过 `IndexWriter.MAX_TERM_LENGTH` 时仅不写 words 项；该行的 n-gram 和原文必须完整保留，任务 4 对相同超长查询走 n-gram。这不是跳过文件或文本。

- [ ] **Step 4：实现构建发布协议。** 将 IndexRoot 加入 CatalogOptions.ExcludedRoots 后枚举，防止索引输入包含自身。每轮生成唯一 SnapshotId，在 IndexRoot 下的新目录写 index 和 manifest；读文件失败归类为显式 skip issue，可继续；索引引擎/磁盘失败则整轮失败。Commit/Close writer、验证 reader 可打开后，原子写入 completed 标记，才返回 BuildResult。加载时校验 schemaVersion=1、manifest 存在和 completed 标记；不加载半成品。取消必须停止写入且不发布。只清理由此次创建且不含 reparse point 的目录；无法安全清理则保留失败目录并报告，禁止递归删除用户输入目录。
- [ ] **Step 5：重跑任务测试，并增加持久化重新打开测试。** 原型默认每次手动建立新快照；Open 只供当前进程/验证工具重新打开已完成的实验快照，不实现自动分支缓存复用。建议提交 `feat(index): 建立完整词与短字符片段倒排索引`。

## Task 4：包含与全字查询、候选核验和续页

**Files:** 创建 Search/CandidateQueryBuilder.cs、Search/SearchService.cs；测试 SearchServiceTests.cs。

**Interfaces — Consumes:** 任务 1 查询契约、任务 3 IndexSnapshot。

**Interfaces — Produces:**

```csharp
public interface ISearchService
{
    Task<SearchPage> SearchAsync(IndexSnapshot snapshot, SearchRequest request,
        SearchCursor? cursor, int pageSize, CancellationToken ct);
}
// SearchService : ISearchService。
// CandidateQueryBuilder.Build(IndexSearcher searcher, SearchRequest request) 返回 Query。
```

- [ ] **Step 1：先写真实小索引集成测试，不能用模拟搜索结果代替。** TestCorpus 增加 `Task<BuildResult> BuildAsync()`，内部按任务 3 固定组合建立，输出 `.index` 明确在枚举中排除。

```csharp
[Fact]
public async Task DoesNotLoseMatchesAfterManyFalseCandidates()
{
    using var corpus = new TestCorpus();
    corpus.Write("a.cs", string.Join("\n",
        Enumerable.Repeat("abc---bcd---cde", 250).Append("abcde")));
    var result = await corpus.BuildAsync();
    using var snapshot = result.Snapshot;
    var request = new SearchRequest("abcde", MatchMode.Contains,
        ExtensionFilter.Parse(false, "", false, ""));
    var page = await new SearchService().SearchAsync(snapshot, request, null, 100,
        CancellationToken.None);
    Assert.Equal(251, Assert.Single(page.Hits).LineNumber);
    Assert.True(page.Exhausted);
}
```

追加：1/2 字符、重复片段 `aaaa`、Unicode、含标点查询、无结果、包含/排除同时开启、100/101/1000/1001 行分页、旧 cursor 配新 query 或新 snapshot 被拒绝、40,000 字符词以及超过 1,024 个不同 gram 的长查询。完整遍历不得重复或丢行。

- [ ] **Step 2：运行 `dotnet test tests/SourceSearch.Core.Tests -c Release --filter FullyQualifiedName~SearchServiceTests`，确认 RED。**
- [ ] **Step 3：构造候选查询。** WholeWord 且查询全由词字符组成、UTF-8 长度不超过词条上限时使用 words TermQuery；否则按标量长度选择 gram1/2/3。长查询最多取 32 个不同、文档频率最低的 gram 做 MUST 交集，剩余字符由核验保证；不存在的 gram 可以直接给空结果。限制子句数量不能靠提高全局 BooleanQuery.MaxClauseCount。

```csharp
var candidate = new BooleanQuery();
foreach (string gram in selectedDistinctGrams)
    candidate.Add(new TermQuery(new Term(fieldName, gram)), Occur.MUST);
```

这里 fieldName 是 `gram1`/`gram2`/`gram3`，selectedDistinctGrams 由 TextRules.EnumerateGrams 生成并按 DocFreq 选取。后缀集合用 TermsFilter/自定义 Filter 构建集合过滤，不生成无限数量 Boolean 子句；若使用 TermsFilter，固定增加 Lucene.Net.Queries 4.8.0-beta00018 包。包含条件为空不限制，排除优先。

- [ ] **Step 4：使用 scorer 逐段遍历，而非收集全部 TopDocs。** 已核对 beta00018 的 API 形态：

```csharp
Weight weight = searcher.CreateNormalizedWeight(query);
foreach (AtomicReaderContext leaf in searcher.IndexReader.Leaves)
{
    Scorer? scorer = weight.GetScorer(leaf, leaf.AtomicReader.LiveDocs);
    if (scorer is null) continue;
    int doc = scorer.Advance(Math.Max(0, afterDocId + 1 - leaf.DocBase));
    while (doc != DocIdSetIterator.NO_MORE_DOCS)
    {
        ct.ThrowIfCancellationRequested();
        int globalDocId = leaf.DocBase + doc;
        // 读取该候选的 file_id/line_number/text，调用 LiteralMatcher.Find。
        // 非空命中才加入结果；达到 pageSize 时返回 lastVisited doc 的续页游标。
        doc = scorer.NextDoc();
    }
}
```

跳过 docBase+MaxDoc <= afterDocId 的段；对每个新 scorer 只调用一次 Advance。SearchCursor 带 SnapshotId、查询文本/模式/规范化过滤条件的 RequestKey，使用不含分隔歧义的序列化加 SHA-256。没有遍历至结尾时 Exhausted=false；不额外扫描全部候选来统计总数。下一页可能为空，这表示实际已耗尽，不能伪报已经存在下一条命中。

- [ ] **Step 5：封装后台执行、取消和诊断。** SearchAsync 在线程池执行同步 Lucene 查询，长期复用 snapshot reader；CandidatesVisited 只计当前页访问量，Elapsed 不包含 UI。取消检查同时进入 LiteralMatcher 处理长行的循环。pageSize 非正数拒绝；请求/游标输入校验在引擎访问前完成。
- [ ] **Step 6：重跑目标测试及全部 Core 测试，要求正确性 PASS。** 建立包含数万重复行的夹具，断言取首批不会访问所有候选，用访问数量断言而非不稳定的毫秒断言。建议提交 `feat(search): 支持索引包含查询全字匹配和续页`。

## Task 5：独立正确性基准与命令行测量工具

**Files:** 创建 Validation 项目及全部七个 C# 文件；创建测试 ValidationTests.cs，修改测试项目添加 Validation 引用；创建 scripts/validate-core.ps1、docs/validation/search-core/README.md。

**Interfaces — Consumes:** IIndexBuilder、ISearchService、BuildReport、SearchRequest、Core 文本契约（不复用 LiteralMatcher 的实现）。

**Interfaces — Produces:**

```csharp
public sealed record QueryCase(string Name, SearchRequest Request, string Group);
public sealed record QueryTiming(string Name, string Layer, int SampleCount,
    double P50Ms, double P95Ms, double MaxMs, int FirstPageRows, long CandidatesVisited);
public sealed record VerificationResult(string Name, bool Passed, long ExpectedLines,
    long ActualLines, string? Difference);
public sealed record BenchmarkReport(string Corpus, Guid SnapshotId,
    string EnvironmentFile, IReadOnlyList<QueryTiming> Timings,
    IReadOnlyList<VerificationResult> Correctness, bool Passed);
public static class ReferenceMatcher
{
    public static IReadOnlyList<TextMatch> Find(string line, string query, MatchMode mode);
}
// CorrectnessVerifier.VerifyAsync(IndexSnapshot, IReadOnlyList<QueryCase>, CancellationToken)
//     -> Task<IReadOnlyList<VerificationResult>>。
// BenchmarkRunner.RunAsync(IndexSnapshot, IReadOnlyList<QueryCase>, int warmups,
//     int iterations, CancellationToken) -> Task<IReadOnlyList<QueryTiming>>。
// QuerySetBuilder.Create(IndexSnapshot) -> IReadOnlyList<QueryCase>。
```

- [ ] **Step 1：创建 console net10.0 项目，引用 Core，加入解决方案并添加以下测试。**

```csharp
[Fact]
public void ReferenceMatcherDefinesOverlappingMatchesIndependently()
{
    Assert.Equal(new[] { new TextMatch(0, 2), new TextMatch(1, 2) },
        ReferenceMatcher.Find("aaa", "aa", MatchMode.Contains));
    Assert.Empty(ReferenceMatcher.Find("User_Name", "User", MatchMode.WholeWord));
}
[Fact]
public void PercentileUsesNearestRank()
{
    double[] samples = Enumerable.Range(1, 30).Select(i => (double)i).ToArray();
    Assert.Equal(29d, BenchmarkRunner.Percentile(samples, 0.95));
}
```

在 BenchmarkRunner 定义 `public static double Percentile(IReadOnlyList<double> samples, double percentile)`，空集合拒绝；排序后取 `ceil(p*n)-1`，p 在 (0,1]。加入已知错误索引结果必使 Verify 失败、源文件 SHA 与快照不一致时本轮校验返回失败、30 次计时样本数量、查询集不少于 30 个组合的测试。

- [ ] **Step 2：运行 `dotnet test tests/SourceSearch.Core.Tests -c Release --filter FullyQualifiedName~ValidationTests`，确认 RED。**
- [ ] **Step 3：实现独立标量暴力匹配。** ReferenceMatcher 不调用 Core 的 Normalize、Find、IsWordRune；独立枚举原始标量和查询标量，逐一 LowerInvariant 比较；以单独 UnicodeCategory switch 判断边界；保存原始 UTF-16 起止映射。无语义差异时两套算法对同一输入得到相同 TextMatch。两套代码共享语义文档和 DTO，不共享匹配算法。

CorrectnessVerifier 从 manifest 指向的原始文件读取，先核对每个文件 SHA-256；不一致记录 `CorpusChanged` 并使本轮失败，要求重建后重跑。按文件/行进行 oracle 比较，引擎部分遍历所有页面，比较路径、一基行号、全部命中范围及重复情况，不受 1,000 条显示上限限制。默认在本地 `.tmp` 保存明细差异，正式报告只保留数量、路径和偏移，不复制源码片段。

- [ ] **Step 4：建立固定查询集。** 每个样本生成一次 JSON 后重复使用；不得每轮挑选更快的查询。至少 15 个文本，每个 Contains/WholeWord 两种模式：`User`、`Id`、`IO`、`a`、`Service`、`public`、`class`、`return`、`Task`、`async`、`用户`、`权限`、`a.b`、一个从 .cs 中发现的实际长标识符、该标识符中间片段、一个经 oracle 确认不存在的字符串。额外加入 `.cs` 包含、`.cs` 排除、`.js,.json` 包含、包含和排除重叠四组条件。若中文候选均不存在，从实际文本选一个存在的中文片段并记录。按 oracle 命中数打低频/高频/无结果标签。

- [ ] **Step 5：实现命令行入口和脚本。** 参数模式如下，错误参数打印帮助并返回 2，验证不通过返回 1，成功返回 0；取消返回 130。

```powershell
dotnet run --project src/SourceSearch.Validation -c Release -- build --root 'C:\Coding\Ftrans\ftrans.main' --preset csharp --index-root artifacts/indexes/csharp --report artifacts/reports/csharp-build.json
dotnet run --project src/SourceSearch.Validation -c Release -- queries --snapshot '<上一步报告中的SnapshotPath>' --output artifacts/reports/csharp-queries.json
dotnet run --project src/SourceSearch.Validation -c Release -- verify --snapshot '<SnapshotPath>' --queries artifacts/reports/csharp-queries.json --output artifacts/reports/csharp-correctness.json
dotnet run --project src/SourceSearch.Validation -c Release -- benchmark --snapshot '<SnapshotPath>' --queries artifacts/reports/csharp-queries.json --warmups 5 --iterations 30 --output artifacts/reports/csharp-timings.json
```

`<SnapshotPath>` 是运行时变量说明，不是让用户手工编辑的步骤：validate-core.ps1 从 build JSON 读取 SnapshotPath、捕获退出码，自动执行后续命令；任一步失败就结束并保留已经生成的报告。BuildReport 的序列化外层增加 `SnapshotPath`，Core BuildReport 本身保持任务 3 的签名。

```powershell
param([string]$Root = 'C:\Coding\Ftrans\ftrans.main',
      [ValidateSet('csharp','mixed')][string]$Preset = 'csharp')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
# 脚本用 Join-Path 定位本工具根目录和 artifacts/reports/<preset>，逐项传递参数。
# 调用 dotnet 后每次检查 LASTEXITCODE；不使用 Invoke-Expression。
```

- [ ] **Step 6：实现计时和资源报告。** 首次打开索引计时独立，第一条查询独立，预热5次后每个查询至少30次；每次只取首批100，不请求全部总数。记录 Stopwatch 时间、命中行数和候选访问数；报告索引库/SDK版本和构建配置。采集 Windows CPU/总内存/OS/磁盘型号（不可取得时说明原因）、进程 PeakWorkingSet64、索引目录累计字节、构建时间、输入文件和行数及跳过原因。硬件信息由本机查询，不发送网络。
- [ ] **Step 7：运行 ValidationTests 和全套 Core 测试。** 在自建夹具跑一次完整 build→queries→verify→benchmark，检查 JSON 可解析且错误退出码生效。建议提交 `feat(validation): 增加独立正确性基准和性能报告`。

## Task 6：实际仓库核心验证及瓶颈处理

**Files:** 执行 scripts/validate-core.ps1；新增 docs/validation/search-core/core-results.md；新增 docs/validation/search-core/csharp-summary.json、mixed-summary.json；必要优化限定于任务 1–5 的所属文件并增加对应回归测试。

**Interfaces — Consumes:** 任务 5 CLI 和报告结构；输出脱离 UI 的实际测量证据。

- [ ] **Step 1：先做 Release 测试并记录 Git revision。** 命令：`dotnet test tests/SourceSearch.Core.Tests -c Release`。失败先定位问题，不启动耗时真实仓库构建。
- [ ] **Step 2：先跑 C# 范围。**

```powershell
pwsh -NoProfile -File scripts/validate-core.ps1 -Root 'C:\Coding\Ftrans\ftrans.main' -Preset csharp
```

检查实际输入与清点的 20,614 个/235.79 MiB 的差异，逐项解释变动、解码失败或明确排除目录。报告用户可见进度；构建长时间运行时不要长时间阻塞主线程等待。

- [ ] **Step 3：跑混合范围。**

```powershell
pwsh -NoProfile -File scripts/validate-core.ps1 -Root 'C:\Coding\Ftrans\ftrans.main' -Preset mixed
```

相同方式对比清点的 35,527 个/547.02 MiB。验证约 8.56 MiB C# 和约5.30 MiB JS 的纳入/跳过情况，不以规模减半的测试代表原样本。

- [ ] **Step 4：判断是否需要优化。** 若核心某组 P95 已超过100ms，UI不可能补救：先根据 CandidatesVisited、读取stored field时间、LiteralMatcher时间和资源占用定位。优先调整选择性更高的 gram、避免无用分配、缩减实际需要读取的字段以及复用 reader；每种优化保留前后相同语料/查询集结果。不能通过关闭全字或短词、只测低频词、减少正确性范围来达标。
- [ ] **Step 5：固定回归证据并写汇总。** 任一代码修改必须增加重现瓶颈或正确性缺陷的有意义测试，重跑受影响查询和完整正确性比较；没有新问题不重复无关全量测量。若结构性更换索引方案才能解决，先记录不达标事实并修订设计，不把未经验证的方案记为成功。核心达到目标只标记 `core-validated`，仍不宣称 UI 验收通过。
- [ ] **Step 6：审核报告没有源码正文、凭据或无关临时内容，建议提交 `docs(validation): 记录真实语料检索核心验证结果`。** 若部分目标未达到，提交信息和报告准确描述未达标。

## Task 7：最小 WPF UI 与请求生命周期

**Files:** 创建 Desktop 项目、App.xaml/App.xaml.cs、MainWindow.xaml/MainWindow.xaml.cs、SearchViewModel.cs；创建 Core/Search/SearchController.cs 和测试 SearchControllerTests.cs；修改解决方案、README.md。

**Interfaces — Consumes:** IIndexBuilder、ISearchService、IndexSnapshot、SearchRequest、SearchPage。

**Interfaces — Produces:**

```csharp
public enum SearchState { NotBuilt, Building, Ready, Failed }
public sealed record SearchReply(long RequestId, SearchPage Page);
public sealed class SearchController : IAsyncDisposable
{
    public SearchController(IIndexBuilder builder, ISearchService search);
    public SearchState State { get; }
    public long CurrentRequestId { get; }
    public BuildReport? CurrentReport { get; }
    public Task RebuildAsync(IndexBuildOptions options,
        IProgress<BuildProgress>? progress, CancellationToken ct);
    public Task OpenSnapshotAsync(string path, CancellationToken ct);
    public Task<SearchReply> SearchAsync(SearchRequest request, SearchCursor? cursor,
        int pageSize, CancellationToken ct);
    public void Cancel();
    public ValueTask DisposeAsync();
}
```

- [ ] **Step 1：先写受控异步测试。** SearchControllerTests 定义 `ControlledBuilder : IIndexBuilder` 和 `ControlledSearch : ISearchService`，以 TaskCompletionSource 暂停/释放请求；注入任务 3 建立的两个真实小快照。

```csharp
// 测试流程：启动请求 A；启动请求 B；释放 B 得到当前结果；再释放 A。
// 断言 B.RequestId == controller.CurrentRequestId，A 的结果不可发布。
// 重建失败/取消：CurrentReport.SnapshotId 不变，旧快照仍可用于再次搜索。
// DisposeAsync 必须等在途查询释放 reader，不能因关闭窗口而访问已释放 reader。
```

加入分页查询取消、连续重建、Building 状态拒绝新查询、Failed 状态旧快照可搜索、无快照时禁用查询的测试。明确过期返回触发 OperationCanceledException；UI 同时核对 RequestId，双重避免旧结果落地。

- [ ] **Step 2：运行 `dotnet test tests/SourceSearch.Core.Tests -c Release --filter FullyQualifiedName~SearchControllerTests`，确认 RED；实现 controller 并重跑至 PASS。** 控制器取消旧任务并异步等待完成后再释放 reader；重建期间保留旧快照，成功后置换；失败保留旧引用。OpenSnapshotAsync 使用同样的置换协议，调用 IndexSnapshot.Open，只供显式验证模式使用。不能在锁内等待查询或同步 `.Wait()`。
- [ ] **Step 3：创建 WPF net10.0-windows 项目，UseWPF=true、OutputType=WinExe，引用 Core 与 Validation。** 默认目录为样本路径但可编辑；索引根固定在工具 artifacts/indexes/ui 下。App 启动解析可选 `--root` 参数，不自动扫描或建立索引。

布局骨架：

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="*"/>
  </Grid.RowDefinitions>
  <StackPanel Grid.Row="0">
    <TextBox Text="{Binding RootPath, UpdateSourceTrigger=PropertyChanged}"/>
    <StackPanel Orientation="Horizontal">
      <Button Content="选择目录" Click="ChooseFolder_Click"/>
      <Button Content="建立 / 重建索引" Click="Build_Click"/>
      <Button Content="取消" Click="Cancel_Click"/>
    </StackPanel>
    <TextBlock Text="{Binding StatusText}"/>
  </StackPanel>
  <StackPanel Grid.Row="1">
    <TextBox Text="{Binding QueryText, UpdateSourceTrigger=PropertyChanged}" KeyDown="Query_KeyDown"/>
    <CheckBox Content="全字匹配" IsChecked="{Binding WholeWord}"/>
    <CheckBox Content="仅包含文件类型" IsChecked="{Binding IncludeEnabled}"/>
    <TextBox Text="{Binding IncludeText}" IsEnabled="{Binding IncludeEnabled}"/>
    <CheckBox Content="排除文件类型" IsChecked="{Binding ExcludeEnabled}"/>
    <TextBox Text="{Binding ExcludeText}" IsEnabled="{Binding ExcludeEnabled}"/>
    <Button Content="搜索" Click="Search_Click"/>
    <Button Content="加载下一批" Click="More_Click"/>
    <TextBlock Text="{Binding TimingText}"/>
  </StackPanel>
  <DataGrid Grid.Row="2" ItemsSource="{Binding Results}" IsReadOnly="True"
            AutoGenerateColumns="False" EnableRowVirtualization="True"
            VirtualizingPanel.VirtualizationMode="Recycling">
    <DataGrid.Columns>
      <DataGridTextColumn Header="文件全路径" Binding="{Binding FilePath}" Width="2*"/>
      <DataGridTextColumn Header="匹配行" Binding="{Binding DisplayLine}" Width="3*"/>
    </DataGrid.Columns>
  </DataGrid>
</Grid>
```

SearchViewModel 实现 INotifyPropertyChanged，提供上述绑定属性及 ObservableCollection<ResultRow>。ResultRow 定义 `string FilePath`、`string DisplayLine`，DisplayLine 为 `行号: 原始行`；超长行只截断显示并标记，不截断底层 SearchHit。使用 OpenFolderDialog 选择目录。事件处理仅做 UI 编排，async void 仅用于 WPF 事件且捕获异常；真正异步流程返回 Task。

- [ ] **Step 4：连接行为。** 建立中禁用目录变更、建立和搜索入口，取消保持可用；搜索中允许新搜索取代旧搜索。进度显示已处理/已索引/跳过数；状态显示快照建立时间及“显示上次建立索引时的内容”。一次只提交一批结果到 Dispatcher；达到1000条提示显示上限并停止 UI 续页，不宣称只有1000条。
- [ ] **Step 5：运行 `dotnet build SourceSearch.slnx -c Release` 和全套 Core 测试。** 启动 `dotnet run --project src/SourceSearch.Desktop -c Release -- --root 'C:\Coding\Ftrans\ftrans.main'`，用自建小目录手工确认建立、默认包含、全字切换、两组过滤、回车搜索、下一批和取消。真实样本性能留给任务8。建议提交 `feat(desktop): 添加最小索引搜索验证界面`。

## Task 8：首屏呈现测量与可复现交付

**Files:** 创建 Desktop/PresentationTimer.cs、UiBenchmarkRunner.cs、scripts/validate-ui.ps1；更新 App.xaml.cs、MainWindow.xaml.cs、README.md、docs/validation/search-core/README.md；新增 docs/validation/search-core/ui-results.md 和两份 ui-summary.json。

**Interfaces — Consumes:** QueryCase、QueryTiming、SearchController、Desktop 的真实搜索入口。

**Interfaces — Produces:**

```csharp
// PresentationTimer.MeasureAsync(Func<Task> updateResults, FrameworkElement results,
//     CancellationToken ct) -> Task<TimeSpan>。
// UiBenchmarkRunner.RunAsync(IReadOnlyList<QueryCase> cases, int warmups,
//     int iterations, CancellationToken ct) -> Task<IReadOnlyList<QueryTiming>>。
// MainWindow.ExecuteMeasuredSearchAsync(QueryCase query, CancellationToken ct)
//     -> Task<(SearchPage Page, TimeSpan UiElapsed)>。
```

- [ ] **Step 1：将普通搜索按钮与 UI 基准共用 ExecuteMeasuredSearchAsync。** 计时从提交请求开始，包含后台查询、Dispatcher 排队、结果绑定、布局与下一次 WPF rendering 检查点。PresentationTimer 在 UI 线程监听 LayoutUpdated 与 CompositionTarget.Rendering，等待结果布局更新后的绘制周期；结束/取消/异常必须解绑事件。该值标为 `ui-render-checkpoint`，不声称测得显示器物理呈现时刻。
- [ ] **Step 2：加入可见 UI 自测模式。** 启动参数 `--ui-benchmark --snapshot <path> --queries <path> --output <path>`；仅这个显式模式允许打开任务6已有快照，不构成产品自动缓存复用。必须显示正常窗口，检查窗口非最小化、结果表已加载，逐查询5次预热+30次测量，报告样本不足、超时或窗口失效。普通模式仍由用户手动建索引。
- [ ] **Step 3：实现验证脚本，分别读取 csharp/mixed 的 SnapshotPath 与查询集，串行运行可见 UI 基准。**

```powershell
pwsh -NoProfile -File scripts/validate-ui.ps1 -Preset csharp
pwsh -NoProfile -File scripts/validate-ui.ps1 -Preset mixed
```

脚本通过 Start-Process 启动程序时显式使用 `-WindowStyle Normal`，仅限这个必须显示真实 UI 的验证步骤，并在执行前告知用户将打开验证窗口。其他后台辅助进程使用 Hidden。进程退出码与 JSON 结论同时检查，缺失任一结果均不能判通过。
- [ ] **Step 4：验证计时与取消。** 手工在测量时最小化窗口，确认报告无效而不是得到异常低延迟；恢复后重跑该组。快速连续提交两次不同查询，确认没有旧结果覆盖；大结果集滚动保持表格虚拟化。检查全部30次样本和两种模式、短词、后缀组合均进入报告。
- [ ] **Step 5：汇总实际结果。** 报告并列展示 core 与 ui-render-checkpoint 的逐查询 P50/P95/最大值、候选数、命中数、索引体积、构建时间、峰值内存、冷进程打开和首次查询。逐项标记100ms目标是否满足；窗口无效的样本不混入统计且说明重跑。任一约定组失败，整体结论为未达标，给出具体瓶颈。
- [ ] **Step 6：完成交付检查。**

```powershell
dotnet test tests/SourceSearch.Core.Tests -c Release
dotnet build SourceSearch.slnx -c Release
git diff --check
```

README 给出精确构建/运行/核心校验/UI校验命令、两个搜索模式、后缀规则、首批/显示上限、快照时效、支持编码、32MiB限制和跳过报告位置。按验证技能检查实际输出，审核全部实现 diff；建议提交 `docs(validation): 记录首屏验证结果和运行说明`。无需发布、部署或创建远端仓库。

## 实施节奏与检查点

| 阶段 | 任务 | 离开阶段所需证据 |
|---|---|---|
| 语义和索引 | 1–4 | 确定性测试通过；所有查询模式走索引；游标分页无漏报 |
| 核心实测 | 5–6 | 两套真实语料正确性比较、逐查询延迟和资源报告 |
| 最小 UI | 7 | 可运行界面、请求取消/旧快照生命周期验证 |
| 端到端验收 | 8 | 可见 UI 首屏测量、目标判定及可复现运行入口 |

任务按顺序执行，接口有依赖，不并行写同一模块。每个任务的 RED/GREEN 结果可记录在本文件任务勾选旁；文档和配置的机械修改不另写镜像测试。修改引擎后只重跑相关小测试，真实语料全测在阶段检查点或必要回归时执行，避免每次编辑都重建500MiB语料。

## 计划自检

- Spec 目标/优先级 → 任务1–8；延期功能没有混入。
- 样本与子模块/普通目录/编码/大小限制/文件变化 → 任务2、3、6。
- 包含/全字/Unicode/短词/字面量/后缀/行号/分页 → 任务1、3、4。
- 两条索引路径、候选核验、避免全量收集、长词/长查询 → 任务3、4。
- 构建失败、取消、旧快照、UI旧结果覆盖 → 任务3、7。
- 独立oracle、全量命中比较、真实语料、硬件/资源/冷暖区分 → 任务5、6。
- 首屏100ms、可见UI、两轮语料、失败报告 → 任务8。
- 本文签名以任务1–5/7定义为准；任务中未定义的框架 API 以固定包版本官方文档为准，不能直接复制不同 Lucene 主版本示例。

## 执行方式与当前状态

计划已完成静态自检，尚未创建项目、还原依赖、编译或运行性能测试。建议由当前会话顺序执行：8个任务高度依赖相邻接口，先保持单一实现上下文有利于定位索引语义和性能问题；实现结束后再做独立整体审核。

用户已选择当前会话执行。实施时按 using-git-worktrees 技能建立/确认隔离工作区。提交建议不自动授予超出用户会话指令的 push、发布或部署权限。

## 版本与 API 依据

- [Lucene.Net 4.8.0-beta00018](https://www.nuget.org/packages/Lucene.Net/4.8.0-beta00018)
- [xunit 2.9.3](https://www.nuget.org/packages/xunit/2.9.3)
- [xunit.runner.visualstudio 3.0.2](https://www.nuget.org/packages/xunit.runner.visualstudio/3.0.2)
- [Microsoft.NET.Test.Sdk 17.12.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.12.0)
- [IndexSearcher.CreateNormalizedWeight](https://lucenenet.apache.org/docs/4.8.0-beta00018/api/core/Lucene.Net.Search.IndexSearcher.html)
- [Weight.GetScorer](https://lucenenet.apache.org/docs/4.8.0-beta00018/api/core/Lucene.Net.Search.Weight.html)
- [IndexWriter.MAX_TERM_LENGTH](https://lucenenet.apache.org/docs/4.8.0-beta00018/api/core/Lucene.Net.Index.IndexWriter.html)
