# Product stage 1 implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 整理现有MVP，保留可运行版本，将界面操作/状态移到可测试的ViewModel，隔离验证入口，修复两个已知问题。

**Architecture:** 新增不依赖WPF的SourceSearch.Presentation（引用Core），包含ViewModel、命令、批次结果和呈现接口。Desktop引用Presentation和Core，只负责窗口、对话框、真实WPF呈现计时。新增WPF验证宿主SourceSearch.UiValidation，引用Desktop及Validation，复用正式窗口/ViewModel，不允许Desktop反向依赖验证项目。

**Tech Stack:** 现有.NET10/WPF/xUnit；不引入新的外部包。

**Spec:** 本会话已确认的产品推进第1阶段，以及原MVP设计 `../specs/2026-10-08-search-core-design.md`。用户已明确要求开始执行第1阶段，沿用当前会话实现方式，不重复申请授权。

## Global constraints

- 本阶段不修改主题或布局，不加入MaterialDesign、缓存管理、增量索引、正则或模糊查询。
- 保持包含/全字/过滤语义、批次100、显示上限1000、虚拟化、异步取消和不可变快照。
- 正式程序不引用Validation或UiValidation，不包含基准/冒烟启动逻辑。
- 仅操作工具和自建夹具，真实语料只读；无需重建真实索引。
- 保留既有性能报告，架构调整不宣称解决UI长尾。

## Review focus

1. 禁用过滤后搜索、再启用，控件原值必须保持（任务1）。
2. 查询变更或新请求到达，旧结果/旧错误不得覆盖新状态（任务1）。
3. 建立中编辑条件不取消建立，失败保留旧快照及清晰状态（任务1）。
4. 分页不能混入不同查询；显示上限仍为1000（任务1/3）。
5. 缺失子模块的诊断准确，不能跟随链接或越出目录（任务2）。

## Task 1: 可测试的界面逻辑和命令

Files: 新增src/SourceSearch.Presentation/{SourceSearch.Presentation.csproj,SearchViewModel.cs,AsyncCommand.cs,ISearchPresentation.cs}及tests/SourceSearch.Presentation.Tests；改造Desktop窗口和XAML。

Interfaces: SearchViewModel(SearchController controller,string indexRoot,ISearchPresentation? presentation=null)，BuildAsync、SearchAsync(bool more=false,CancellationToken ct=default)、OpenSnapshotAsync、DisposeAsync；BuildCommand/SearchCommand/MoreCommand/CancelCommand；ISearchPresentation.MeasureAsync(Func<Task>,CancellationToken)返回Task<TimeSpan>；SearchExecution(SearchPage Page,TimeSpan UiElapsed)。

- [x] 添加真实夹具测试，覆盖过滤保留、建索引/搜索/续页/1000上限、条件改变使续页失效、建立失败保留快照、受控新旧查询竞争、命令CanExecute和关闭。
- [x] 以抛出NotImplementedException的接口stub观察RED，再实现；运行Presentation测试及原Core测试。
- [x] WPF将命令绑定到按钮和回车；MainWindow仅保留目录对话框、呈现适配器及异步关闭。普通查询不得重写条件控件。

## Task 2: 修复缺失子模块诊断

Files: Core/Corpus/FileCatalog.cs，Core.Tests/FileCatalogTests.cs。

- [x] 用update-index --cacheinfo 160000创建不存在目录的gitlink，先观察期望SubmoduleNotInitialized的测试失败。
- [x] 在文件属性检查前检测缺失的子模块目录，同时保留IsWithin范围检查和已存在目录的reparse/exclusion检查。
- [x] 运行全部Core测试。

## Task 3: 独立验证宿主与交付

Files: 新增src/SourceSearch.UiValidation，迁移Desktop/App中的验证分支、UiSmokeRunner、UiBenchmarkRunner；更新解决方案、验证脚本和README。

- [x] 为Desktop程序集不依赖Validation添加架构回归检查，确认迁移前失败；迁移后验证项目依赖方向。
- [x] 验证宿主通过Model设置测试条件，并调用与用户命令相同的SearchAsync；计时、5次预热/30样本/100首批语义不变。
- [x] 冒烟增加禁用过滤值保留及真实命令入口检查，保持原有虚拟化/最小化无效检查；脚本默认启动UiValidation.exe。
- [x] 执行Release全解决方案build、两套单元测试、可见窗口冒烟、夹具基准，确认脚本输出/退出码及正式Desktop无验证依赖。
- [x] 做一次独立只读审核，修复重要问题并验证；记录结果。保留MVP基线提交，按授权范围本地提交本阶段。

## Execution notes

基线fffbc10，工作区干净。已有工作区隔离方式为原checkout中的专用分支，继续复用，不重复尝试之前因所有者检查失败的worktree操作。本轮采用codex/product-stage1分支，MVP分支保留。
