# MaterialDesign UI implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline, followed by one fresh read-only final review.

**Goal:** 实现已批准的深色紧凑WPF产品界面及目录/分支身份、查询状态、命中高亮。
**Architecture:** Core提供只读Git身份及清空活动快照；Presentation集中上下文生命周期/结构化状态和展示结果；Desktop通过共用主题字典及轻量TextBlock适配，UiValidation复用同一资源和窗口。
**Tech Stack:** .NET10/WPF/Lucene.NET/xUnit，MaterialDesignThemes固定5.3.2（官方NuGet核实），优先验证MaterialDesign2字典的紧凑控件。
**Spec:** `../specs/2026-10-08-product-ui-design.md`（用户已确认）。当前会话继续采用原执行方式和本地提交授权，不重复询问。

## Global constraints

- 三层结构；结果仅文件全路径/匹配行。默认包含、全字可选、Unicode偏移与搜索语义不变。
- 100行批次、1000行显示上限、虚拟化。查询不能触发Git读取/目录扫描。
- 输入编辑不取消建立；新目录和新分支不得显示/搜索旧来源；同目录同身份失败保留快照。
- 无分支持久缓存、增量、缓存管理器、正则、模糊、编辑器跳转、自动输入搜索。
- 真实语料只读。前后同快照36组×5预热×30样本，不以UI换肤宣称性能通过。
- 主题只在Desktop及验证宿主加载；正式Desktop不依赖验证项目。

## Review focus

1. 元数据延迟返回和目录切换竞争，旧目录结果/身份不能覆盖新目录（Task1/2）。
2. 建立前后HEAD变化、识别失败及取消，旧快照资格不应被错误恢复（Task2）。
3. 改条件后显示的旧结果必须保留其提交条件/高亮，呈现异常禁用续页（Task2/3）。
4. 超长行与代理对、重复/相邻命中，高亮裁剪不能破坏UTF16或产生假命中（Task2/3）。
5. 窄窗口、最小化/恢复、主题资源与验证宿主一致，不能丢失可用操作或虚拟化（Task3/4）。

## Task 1: 身份读取与快照清空

Files: Core/Corpus/WorkspaceIdentity.cs, Core/Search/SearchController.cs, Core.Tests/WorkspaceIdentityTests.cs, SearchControllerTests.cs。
Interfaces: `WorkspaceIdentity(WorkspaceKind Kind,string? RepositoryRoot,string? Branch,string? Commit,string? Error)`；`IWorkspaceIdentityReader.ReadAsync(string root,CancellationToken ct)`；`SearchController.ClearAsync(CancellationToken ct)`。

- [x] 新建接口stub和失败测试：普通目录、Git子目录、未出生分支、detached HEAD、HEAD提交变化；清空后无报告/不可查询且等待在途reader。
```csharp
await controller.ClearAsync(default);
Assert.Null(controller.CurrentReport);
await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SearchAsync(request, null, 100, default));
```
- [x] 执行Core测试观察RED（.tmp/stage3/core-red.log），再实现：元数据只调用symbolic-ref和rev-parse，参数ArgumentList，隐藏进程，超时5秒，取消杀死进程；非Git与不可识别分开。
- [x] ClearAsync复用controller已有串行替换/等待reader路径，成功时状态NotBuilt；运行全部Core测试并检查GREEN。

## Task 2: 上下文状态与展示数据

Files: Presentation/SearchViewModel.cs（查询/建立）、SearchViewModel.State.cs（结构化属性/文案）、SearchViewModel.Workspace.cs（目录与身份）、ResultCollection.cs；Presentation.Tests/WorkspaceStateTests.cs、ResultRowTests.cs、SearchViewModelTests.cs。
Interfaces: 构造函数追加可选`IWorkspaceIdentityReader? identityReader`；RootPath初始赋值；`SwitchFolderAsync(string path,CancellationToken ct=default)`、`RefreshIdentityAsync()`；IndexStatus/QueryStatus、Identity/IndexedIdentity/IndexedRoot、AppliedRequest/HasPendingChanges、Report/Progress/IndexError/QueryError；原Build/Search/More/Cancel命令继续使用。

- [x] 失败测试覆盖新目录清空/相同目录保留、旧元数据回复失效、建立时编辑条件/取消、身份变化禁止搜索、建立期间切换分支后不误标就绪；空文本/换行不能提交。
```csharp
await model.BuildAsync(); await model.SearchAsync();
await model.SwitchFolderAsync(other.Root);
Assert.False(model.CanSearch); Assert.Empty(model.Results); Assert.Null(model.Report);
```
- [x] 失败测试覆盖AppliedRequest与正在编辑文本分离；取消/旧异常不覆盖新状态。改造原“换目录重建失败保留旧快照”测试为同目录受控构建失败，新目录按已批准设计清空。
- [x] ResultRow保存裁剪文本、行号及裁剪后的原始命中范围，公开DisplayLine保持兼容。失败测试：命中在2000之后、跨裁剪点、代理对边界及重叠命中。
```csharp
var row = ResultRow.From(new("a.cs", 2, new string('a',1999)+"😀User", [new(2001,4)]));
Assert.DoesNotContain('\uD83D', row.Text); Assert.Empty(row.Matches);
```
- [x] 实现并运行全部Presentation测试GREEN。Git读取在打开/刷新/建立前后异步完成，去重在途，版本验证后发布；搜索本身不读取Git。查询取消也取消渲染等待。

## Task 3: MaterialDesign窗口

Files: Desktop/Themes/ProductTheme.xaml、App.xaml、MainWindow.xaml/.cs、Controls/HighlightedLine.cs、IndexDetailsWindow.xaml/.cs；UiValidation/App.xaml、UiSmokeRunner.cs。
Interfaces: 两个App均Merge `pack://application:,,,/SourceSearch.Desktop;component/Themes/ProductTheme.xaml`；HighlightedLine.Row依赖属性消费ResultRow；详情窗口消费Model.Report及索引来源。

- [x] 加入固定NuGet引用，使用BundledTheme Dark及MaterialDesign2.Defaults，定义语义brush与紧凑样式，运行真实窗口资源探针。失败资源加载必须修复，不用HTML代替。
- [x] 三层XAML：1180×780/min880×600，原生标题栏；路径分支状态、建立/取消和详情；搜索+全字+两组后缀（窄窗口换行）；结果Grid保留虚拟化、两列、行号与高亮、计数/续页/空态/错误。
- [x] Enter绑定SearchCommand，Ctrl+F焦点，选择目录await SwitchFolderAsync，Activated刷新身份；右键复制完整路径；详情实际报告、原因翻译和虚拟化列表。
- [x] 高亮仅绑定已实现行，创建Run片段并复用主题brush，不引入编辑器或每行重型控件。额外动画关闭。
- [x] UiSmoke新增主题/空态/高亮/路径来源/详情/窄宽布局和命令检查，运行可见窗口并检查渲染截图。布局配置不编写镜像XAML测试，交互状态用Task2测试覆盖。

## Task 4: 验证、审核与交付

Files: scripts/validate-ui*.ps1（按需扩展报告输出以保留基线），docs/validation/product-stage3.md，README.md。

- [x] 保存改造前的两套真实语料UI基线；相同已存在快照/queries写入artifacts/reports/stage3/before，后测写after，不重建源仓库。
- [x] Release build、Core/Presentation全测试、可见WPF冒烟；干净Desktop publish检查无Validation。窗口125%/150%布局使用应用内缩放检查并说明与真实显示DPI区别；真实输入若工具受阻保留限制。
- [x] UI基准串行运行，与编译/其他基准错开；报告各组原始样本、P95/失败组、前后比较，不重复跑到偶然通过。
- [x] 一次fresh只读review（gpt-6-astra），按影响修复Important并RED→GREEN、全部测试；不派实现子代理。
- [x] 暂存区commit-gate后本地提交；记录结果、剩余限制和取舍；工作区干净。

## Execution record

分支codex/materialdesign-ui，起点f549a5a；复用现有checkout及PowerShell流程记录。任务1接口供任务2使用；任务2ResultRow和状态供任务3使用；主题字典两个App使用相同URI。接口无冲突。

最终：Release零警告/错误，Core93/93、Presentation21/21，可见WPF冒烟25/25，干净Desktop发布无验证文件。一次独立review的重要问题（渲染检查点前状态通知缺失）已RED→GREEN修复。前后各两套36组测量均有有效报告；后测性能退化，未达到100ms目标，建议缓存前先优化UI。证据与物理输入/DPI限制见 `../../validation/product-stage3.md`。本地提交用于保存UI功能阶段，不能作为性能通过结论。

后续决定（2026-10-08）：用户接受当前性能退化，将 `ede78ce` 的100条批次、高亮开启版本设为[新基线](../../validation/performance-baseline.md)。上述缓存前优化建议不再阻断后续阶段；原始测量和旧目标未通过事实保留，阶段性能取舍已接受。
