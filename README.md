# Source Search

Windows / .NET 10 的独立源码搜索工具，采用深色紧凑 MaterialDesign WPF 界面。核心使用 Lucene.Net 倒排索引：完整词索引用于全字查询，1/2/3 字符片段索引用于包含查询；候选行再次执行字面量校验。查询不重新遍历源文件。

每个打开的快照具有约 256 MiB 上限的候选行 LRU 内存缓存，复用解压后的原文及归一化文本，不缓存查询结果。关闭快照即释放；这是查询阶段的内存优化，与按仓库/分支持久缓存、增量重建无关。冷查询遇到大型压缩 JS 行时仍可能超过 100 ms。

## 构建和运行

需要 Windows、.NET 10 SDK；Git 仓库输入还需要 Git。首次还原从 nuget.org 下载锁定版本依赖，包保存在工具目录 `.tmp/nuget`。

```powershell
dotnet build SourceSearch.slnx -c Release
dotnet test tests/SourceSearch.Core.Tests -c Release --no-restore
dotnet test tests/SourceSearch.Presentation.Tests -c Release --no-restore
dotnet run --project src/SourceSearch.Desktop -c Release --no-build -- --root 'C:\Coding\Ftrans\ftrans.main'
```

打开目录后，已有 Git 分支缓存会自动检查并复用或增量更新；没有缓存时点击“建立索引”。完成后输入文本并点击搜索或回车；Ctrl+F 聚焦搜索。可选择 Git 根目录、仓库内子目录或普通文件夹，原始源码只读。普通文件夹每次会话手动建索引，不自动复用缓存。

顶部显示目录、分支和索引状态；“详情”显示索引来源、统计及跳过原因。“更新索引”检查文件变化；“完整重建”重新读取全部文件；“索引缓存”管理已登记的分支缓存。窗口变窄时两组后缀条件分行排列。结果路径可悬停查看或右键复制。修改查询条件后保留上次提交的结果与标题，按 Enter 才执行新查询，并在此之前禁用续页。

## 缓存与更新

Git缓存按仓库绝对路径、所选目录、分支及索引设置隔离；游离HEAD按提交SHA隔离。缓存保存工作区内容，包括未提交文件。同一目录重新打开会检查现有缓存并自动更新；窗口激活检测分支/HEAD变化，匹配新的缓存。当前不监听文件修改，同一分支编辑文件后点击“更新索引”。

增量检查重新枚举文件集合，比较各文件上次成功索引的修改时间与长度，处理新增、修改、删除、重命名和忽略规则变化。未变文件不读取正文；元数据变化后读取摘要，相同内容不重新分词。若文件大小与时间戳均被人为保持，快检可能漏掉修改，此时使用“完整重建”。

缓存位于工具 `artifacts/indexes/cache/<缓存键>/snapshots/<快照ID>`，对应 `entry.json` 指向当前快照。更新复制旧索引后再修改，成功后原子切换登记，失败/取消保留旧登记；更新期间需要额外磁盘空间，历史快照暂不自动清理。管理器显示当前快照大小；删除所选将清理该分支缓存及历史快照，使用中的缓存不可删除。

普通目录/身份未知的会话快照仍位于 `artifacts/indexes/ui`。旧版本在此创建的索引没有可靠的分支登记，不自动迁移为新缓存；首次使用新缓存需要建立一次。旧schema1仍可由验证入口只读打开。目录损坏会提示错误，可用完整重建修复。

## 搜索规则

- 默认包含匹配：`User` 匹配 `User`、`GetUser`、`UserService`；勾选“全字匹配”后只匹配完整的 `User`，不匹配 `User_ID` 或 `User2`。
- 两种模式都忽略大小写，使用 Unicode 标量不变小写。单词字符是 Unicode 字母、组合标记、十进制数字和连接标点。单个汉字片段可包含搜索；全字模式遵循同样的连续单词边界。
- 文本按字面量处理，支持 1、2 字符短查询。没有正则、模糊匹配或跨行搜索；空查询不返回结果。
- 后缀以逗号分隔，`cs,js` 与 `.cs,.js` 等价；不区分大小写。未勾选或空列表不限制，排除优先于包含；使用最后一个后缀，不接受 `*.cs` 或 `tar.gz`。
- 每条结果是一行，显示完整路径、原始行号和匹配行；没有类名列。同一行多个命中合并显示，一次取 100 行，界面最多显示 1000 行，不计算全量总数。底层验证器不受显示上限限制。
- 长行只在界面截断为最多 2000 个 UTF-16 字符并标记，避免拆开代理对；索引、匹配和正确性验证保留完整行。显示原始命中高亮，没有语法解析或语法高亮。
- 搜索的是建立/更新时的快照。同目录、同身份更新失败或取消保留之前成功的快照。切换目录清空旧索引与结果；窗口激活及建立前后检查分支/HEAD，检测变化后切换缓存或等待建立。元数据读取失败会明确标示，不能据此证明快照仍与当前 HEAD 一致。新查询会取消旧查询，关闭窗口等待在途查询释放索引。

## 输入范围

当前 UI 使用 `CorpusPresets.Mixed` 的 36 种文本后缀，覆盖 C#/JS/TS/Vue/HTML/CSS/JSON/XML/YAML/Markdown/SQL/脚本及 C/C++/Go/Python 等。Git 根目录或仓库内子目录按 tracked + 未被忽略的 untracked 文件枚举，保留上级仓库忽略规则，并递归选择范围内已初始化的 gitlink 子模块；仓库外普通目录递归枚举。不会跟随文件或目录符号链接。固定排除 `.git/.vs/bin/obj/node_modules/.tmp/.temp/.worktrees/artifacts`。

支持严格 UTF-8（可带 BOM）和带 BOM 的 UTF-16/UTF-32。无 BOM 的旧 ANSI/GBK、非法字节、无 BOM 的含 NUL 文件会明确跳过。每文件上限 32 MiB，不截断索引内容。读取期间检查修改时间和长度，变化时重试一次；文件 SHA-256 保存在 manifest。已排除的目录不算解码跳过项。

跳过原因、文件清单、快照建立时间在每个快照的 `manifest.json`；构建失败的目录没有 `completed` 标记。快照保存源码行正文，应按原源码的本地访问权限管理。Git缓存可在管理器删除；旧会话快照在退出程序后可自行移除。

## 可重复验证

```powershell
# 全量结果对比 + 核心首批性能；需要等待真实仓库建索引
pwsh -NoProfile -File scripts/validate-core.ps1 -Preset csharp
pwsh -NoProfile -File scripts/validate-core.ps1 -Preset mixed
# 打开真实可见窗口，串行执行；请勿最小化验证窗口
pwsh -NoProfile -File scripts/validate-ui.ps1 -Preset csharp
pwsh -NoProfile -File scripts/validate-ui.ps1 -Preset mixed
# 自动创建小型夹具、验证界面行为、保存渲染截图
pwsh -NoProfile -File scripts/validate-ui-smoke.ps1
```

预热 5 次，每查询测量 30 次首批 100 行。当前UI按[已接受的性能基线](docs/validation/performance-baseline.md)进行后续比较；原每组P95≤100ms不再作为当前阶段阻断条件。现有基准程序仍保留该旧目标的诊断检查，返回非零不单独代表相对新基线退化。`core` 测量后台查询；`ui-render-checkpoint` 还包含结果绑定、布局和下一次 WPF rendering 检查点，不等于物理显示器呈现时间。索引打开和首个查询单列；各组另记 `FirstInSequenceMs`（该组预热前的一次，不声称文件系统或共享候选行缓存完全冷）。窗口不可见/最小化、样本不足或渲染超时均视为无效，不能报通过。

报告在 `artifacts/reports/<preset>`，含 build、queries、correctness、timings 和 ui-timings JSON；命令失败返回非零退出码。`-NoBuild` 可复用已构建 Release 程序。UI 脚本默认构建并启动独立的 `SourceSearch.UiValidation.exe`，额外支持 `-Executable` 指定验证宿主产物。`--ui-smoke`、`--ui-benchmark`、`--snapshot` 属于验证宿主，正式 `SourceSearch.Desktop.exe` 仅使用 `--root` 指定初始目录。这不是产品缓存复用功能。

## 项目职责

| 项目 | 职责与依赖 |
|---|---|
| Core | 文件枚举、倒排索引、查询、快照和请求生命周期 |
| Presentation | ViewModel、命令、状态、结果批次和呈现接口；仅依赖 Core，可在无 WPF 环境下测试 |
| Desktop | 正式 WPF 窗口、目录对话框、命令绑定、呈现适配器及关闭；依赖 Core/Presentation |
| Validation | 独立命令行正确性和核心性能验证 |
| UiValidation | 独立 WPF 冒烟/基准入口，复用正式窗口和 ViewModel；依赖 Desktop/Validation |

正式程序不引用两个验证项目。第一阶段整理、第三阶段 MaterialDesign 接入及第四阶段缓存/增量已实现；正式窗口与验证宿主共用主题。验证记录见 [第一阶段](docs/validation/product-stage1.md)、[第三阶段](docs/validation/product-stage3.md) 和 [第四阶段](docs/validation/product-stage4.md)。

设计与计划见 `docs/superpowers`；实际结果见 `docs/validation/search-core`。验证目标和实际是否达标以报告为准。

当前已接受的UI基线：每批100条、开启高亮，C#/混合语料最慢组P95分别为782.46/604.34ms。用户于2026-10-08接受本次退化，后续与这份[性能基线](docs/validation/performance-baseline.md)比较，可继续缓存与增量阶段。

MVP历史实测：两套真实语料各36组的完整结果均一致，核心暖查询最慢P95为38.75ms（C#）/14.15ms（混合）；当时可见UI仍有4/7组超出100ms目标。详见 [MVP 验收报告](docs/validation/search-core/ui-results.md)；换肤前后测量见 [第三阶段报告](docs/validation/product-stage3.md)。
