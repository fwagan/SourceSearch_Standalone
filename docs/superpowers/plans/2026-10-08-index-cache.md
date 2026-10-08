# Index Cache Implementation Plan

> Execute inline with superpowers:executing-plans; one fresh read-only final reviewer.

**Goal:** Git仓库按分支复用缓存、增量更新及可用的缓存管理器。
**Architecture:** IndexBuilder负责schema2和不可变增量快照；IndexCache负责身份键、锁、原子发布与删除；SearchController复用替换生命周期；Presentation管理自动准备和手动更新；Desktop增加管理器。
**Tech Stack:** Existing .NET10/WPF/Lucene.NET/xUnit, no new dependencies.
**Spec:** ../specs/2026-10-08-index-cache-design.md

## Global constraints

- 每批100、1000上限、默认包含/全字、后缀与高亮语义不变。
- 查询不扫描文件、不调用Git；普通目录不自动复用缓存。
- 旧快照不可变，原目录只读；取消/失败不得发布部分更新。
- 新格式仍能读schema1；缓存删除只在工具自有路径。
- 缓存缺失不自动全建；已有缓存自动检查更新（用户可调整）。

## Review focus

1. 删除文件导致稀疏Id不能错误映射路径；旧格式只读兼容。
2. HEAD切换或关闭期间迟到缓存回复不能覆盖新目录。
3. 两进程更新/删除和活动reader的互斥边界。
4. 损坏manifest/entry、相对路径逃逸、reparse point及发布失败。
5. 同大小同时间戳的检测限制必须公开，新增/忽略/删除不能只靠时间戳发现。

## Task1 — 增量快照

Files: Core/Indexing/IndexBuilder*.cs, IndexContracts.cs, IndexSnapshot.cs, SearchService.cs, Core.Tests/IncrementalIndexTests.cs.
Interfaces: IndexBuildOptions.PreviousSnapshotPath optional; BuildReport.Update optional统计；schema2 file_key；IndexSnapshot按file_id映射。
- [x] 先加失败测试：未变文件不读取、增改删且结果等于全量、时间只变不分词、取消保留旧快照。
- [x] 复制旧Lucene文件、按file_key删除及替换、稳定Id、完成标记、兼容检查。
- [x] Core全测试通过；记录RED/GREEN。

## Task2 — 缓存注册与生命周期

Files: Core/Caching/IndexCache*.cs, CacheContracts.cs, SearchController.cs, Core.Tests/IndexCacheTests.cs.
Interfaces: IIndexCache.Exists/PrepareAsync/ListAsync/DeleteAsync; CatalogOptions + WorkspaceIdentity; controller.ReplaceWithAsync(factory,ct)。
- [x] 失败测试覆盖身份隔离、无变更重开、增量、取消/身份变化保持指针、损坏、安全删除及租约。
- [x] SHA256键、每键独占锁、snapshot租约、原子entry发布、构建后身份校验、枚举与删除。
- [x] Core全测试与元数据解析检查。

## Task3 — Presentation与真实WPF

Files: Presentation/SearchViewModel.Cache.cs plus existing partials; Desktop/MainWindow/CacheManagerWindow; UiValidation/App/UiSmokeRunner; Presentation.Tests/CacheWorkflowTests.
- [x] 失败测试覆盖初始化复用、换目录不带旧结果、更新失败保留同身份旧快照、取消、无缓存不全建。
- [x] 可选cache依赖使旧单测/验证宿主可控；正式窗口启动与切换启用自动准备；身份变化匹配缓存；按钮更新和完整重建。
- [x] 管理器真实列表/删除所选确认/使用中提示；更新状态和统计、普通目录文案。
- [x] 全测试、可见WPF冒烟与截图检查。

## Task4 — 交付

- [x] README/阶段报告更新，统计复用/增量对比全量（小型fixture及可行真实快照兼容验证）；不重写接受基线。
- [x] Release构建、Core/Presentation测试、UI冒烟、产品发布无Validation。
- [x] 一位fresh final reviewer；身份与损坏manifest问题RED→GREEN，删除租约竞态修复并通过既有删除检查。
- [x] 暂存区commit-gate；交付采用本地提交，提交后确认工作区干净。

## Rulings

复用当前checkout专用分支与PowerShell记录，遵循本会话已采用的环境失败回退；不再次执行失败的worktree/Bash辅助流程。用户“开始推进”和持续执行授权覆盖必要设计与实现，设计文件可随实现核对；不中断要求重复批准相同阶段。
