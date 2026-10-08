# Source Search

Windows / .NET 10 的独立源码搜索验证版。核心使用 Lucene.Net 倒排索引：完整词索引用于全字查询，1/2/3 字符片段索引用于包含查询；候选行再次执行字面量校验。查询不重新遍历源文件。

每个打开的快照具有约 256 MiB 上限的候选行 LRU 内存缓存，复用解压后的原文及归一化文本，不缓存查询结果。关闭快照即释放；这是查询阶段的内存优化，与按仓库/分支持久缓存、增量重建无关。冷查询遇到大型压缩 JS 行时仍可能超过 100 ms。

## 构建和运行

需要 Windows、.NET 10 SDK；Git 仓库输入还需要 Git。首次还原从 nuget.org 下载锁定版本依赖，包保存在工具目录 `.tmp/nuget`。

```powershell
dotnet build SourceSearch.slnx -c Release
dotnet test tests/SourceSearch.Core.Tests -c Release --no-restore
dotnet run --project src/SourceSearch.Desktop -c Release --no-build -- --root 'C:\Coding\Ftrans\ftrans.main'
```

打开目录后点击“建立 / 重建索引”，完成后输入文本并点击搜索或回车。默认目录是上述样本，可选择其他 Git 根目录或普通文件夹。普通模式每次启动手动建索引，没有缓存自动复用、分支缓存、增量更新或缓存管理器。原始源码只读；快照位于本工具 `artifacts/indexes/ui`。

## 搜索规则

- 默认包含匹配：`User` 匹配 `User`、`GetUser`、`UserService`；勾选“全字匹配”后只匹配完整的 `User`，不匹配 `User_ID` 或 `User2`。
- 两种模式都忽略大小写，使用 Unicode 标量不变小写。单词字符是 Unicode 字母、组合标记、十进制数字和连接标点。单个汉字片段可包含搜索；全字模式遵循同样的连续单词边界。
- 文本按字面量处理，支持 1、2 字符短查询。没有正则、模糊匹配或跨行搜索；空查询不返回结果。
- 后缀以逗号分隔，`cs,js` 与 `.cs,.js` 等价；不区分大小写。未勾选或空列表不限制，排除优先于包含；使用最后一个后缀，不接受 `*.cs` 或 `tar.gz`。
- 每条结果是一行，显示完整路径、原始行号和匹配行；没有类名列。同一行多个命中合并显示，一次取 100 行，界面最多显示 1000 行，不计算全量总数。底层验证器不受显示上限限制。
- 长行只在界面截断为前 2000 个 UTF-16 字符并标记，索引、匹配和正确性验证保留完整行。没有语法解析或高亮。
- 搜索的是建立索引时的快照。源文件变更后需手动重建；失败或取消保留之前成功的快照。新查询会取消旧查询，关闭窗口等待在途查询释放索引。

## 输入范围

当前 UI 使用 `CorpusPresets.Mixed` 的 36 种文本后缀，覆盖 C#/JS/TS/Vue/HTML/CSS/JSON/XML/YAML/Markdown/SQL/脚本及 C/C++/Go/Python 等。Git 根目录或仓库内子目录按 tracked + 未被忽略的 untracked 文件枚举，保留上级仓库忽略规则，并递归选择范围内已初始化的 gitlink 子模块；仓库外普通目录递归枚举。不会跟随文件或目录符号链接。固定排除 `.git/.vs/bin/obj/node_modules/.tmp/.temp/.worktrees/artifacts`。

支持严格 UTF-8（可带 BOM）和带 BOM 的 UTF-16/UTF-32。无 BOM 的旧 ANSI/GBK、非法字节、无 BOM 的含 NUL 文件会明确跳过。每文件上限 32 MiB，不截断索引内容。读取期间检查修改时间和长度，变化时重试一次；文件 SHA-256 保存在 manifest。已排除的目录不算解码跳过项。

跳过原因、文件清单、快照建立时间在每个快照的 `manifest.json`；构建失败的目录没有 `completed` 标记。快照保存源码行正文，应按原源码的本地访问权限管理。当前没有自动清理，退出程序后可自行移除不再需要的工具 `artifacts/indexes` 下快照。

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

预热 5 次，每查询测量 30 次首批 100 行；P95 目标是每组 ≤100 ms。`core` 测量后台查询；`ui-render-checkpoint` 还包含结果绑定、布局和下一次 WPF rendering 检查点，不等于物理显示器呈现时间。索引打开和首个查询单列；各组另记 `FirstInSequenceMs`（该组预热前的一次，不声称文件系统或共享候选行缓存完全冷）。窗口不可见/最小化、样本不足或渲染超时均视为无效，不能报通过。

报告在 `artifacts/reports/<preset>`，含 build、queries、correctness、timings 和 ui-timings JSON；命令失败返回非零退出码。`-NoBuild` 可复用已构建 Release 程序。UI 脚本额外支持 `-Executable` 指定构建产物。`--snapshot` 只供显式 `--ui-benchmark` 模式打开已有索引，不是产品缓存复用功能。

设计与计划见 `docs/superpowers`；实际结果见 `docs/validation/search-core`。验证目标和实际是否达标以报告为准。

当前实测：两套真实语料各36组的完整结果均一致，核心暖查询最慢P95为38.75ms（C#）/14.15ms（混合）；可见UI仍有4/7组超出100ms目标。详见 [完整验收报告](docs/validation/search-core/ui-results.md) 和 [实施取舍/延期问题](docs/validation/search-core/implementation-decisions.md)。
