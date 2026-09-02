# FACM 常见陷阱与防回归规则

## WinUI App 不要用 UseWindowsForms 切换桌面 SDK 目标

### 根因

FACM.App 是 WinUI 3 XAML 应用。为托盘接入 WinForms 时直接设置 `UseWindowsForms=true` 会让 .NET 10 WindowsDesktop targets 把 WinUI 的 `App.xaml` 按 WPF 应用定义处理，构建失败并报 `MC6000`，要求 `PresentationCore` / `PresentationFramework`。

### 防回归规则

- WinUI App 保持 `UseWinUI=true`，不要为托盘程序集启用 `UseWindowsForms`；使用 `Microsoft.WindowsDesktop.App.WindowsForms` framework reference 提供托盘 API。
- 保持 `TreatWarningsAsErrors=true`，重新执行 solution build、FoundationSmoke（需要跳过 Gate13 时显式传 `--skip-gate13`）和 WindowsSmoke。
- 这只是构建兼容性处理，不得借机把 WinUI shell 迁移成 WPF 或改变托盘的单实例所有权。

## T1 League 诊断不能先行变成性能修复

### 根因

真实 League 工作台卡顿需要区分 Gameflow 轮询、Workbench 阶段、共享 LCU 请求、超时/取消、session 失效和 UI 回写。没有带 correlation ID 的成对时间线时，提前加入 limiter、cache、debounce、dedup 或新的 timeout 会把症状隐藏，并可能改变 4.0 的既有行为。

### 防回归规则

- T1 只记录共享 Gateway、单一 Gameflow monitor、Workbench refresh/stage 的 start/end、phase、status/outcome、duration、in-flight 和脱敏 endpoint。
- 诊断回调必须 fail-soft，不能创建第二 transport、第二 polling loop 或改变请求顺序。
- 在用户提供真实 League trace 并完成根因排序前，不实施 T2 性能策略修改。

## Bench 自动呈现不能依赖 Workbench 页面生命周期

### 根因

`LeagueWorkbenchViewModel.Live` 只会在 Workbench refresh 入口被更新。若 Compact/Strip 把它当作
自动呈现的唯一事实来源，则 Workbench 未打开、ChampSelect 候选晚于一次性进入刷新到达，或
候选列表在会话中变化时，League 客户端已经显示 Bench 候选而 FACM 仍停留在 Orb。这是事实
观察 owner 与 UI 页面生命周期耦合，不是 portrait 渲染失败。

### 防回归规则

- 自动 Compact/Strip 呈现必须消费进程级 Bench runtime state；它应挂接现有唯一
  `LeagueGameflowMonitor.Observed` 心跳，不得新增第二个 Gameflow/session/gateway/timer/loop。
- 首次可操作候选只在当前 ChampSelect context 内锁存 Strip；零候选或暂时读取失败只能显示
  waiting state，不能因为一次空读回退 Orb。
- 普通 expanded surface 的 outside-click 规则不能套用到锁存 BenchStrip；候选点击和 F 句柄
  简单点击必须保持 Strip，InGame/Lobby 才能结束 context。
- `league.bench.surface-evaluation` 只在决策签名改变时记录 phase、context generation、
  candidate count、current surface、latch、source owner 和 freshness，避免用高频日志掩盖真正
  的生命周期断链。

## 未经腾讯验证的 LCU 可选字段不能提升为自动化硬门槛

### 根因

FACM 3.3.0 首版“自动寻找对局 / 自动接受”在 deterministic fixture 中成立，但腾讯真实客户端两项均无效果。问题不是 POST endpoint 本身，而是 FACM 为了安全又额外加了若干上游/其它环境中常见的字段假设：

- 自动找局强制要求 `partyId` 非空、`queueId > 0`、`allowedStartActivity=true`，并把任意 warnings/restrictions 都当成阻塞；
- 自动接受强制要求 `/lol-matchmaking/v1/search` 同时返回 `lobbyId + queueId + readyCheck.state=InProgress` 后才允许 accept。

腾讯客户端缺少或以不同形态返回其中任一可选字段时，代码会在 POST 前 fail-closed，因此用户只看到“开关没效果”，而本地 fixture 因数据过于完整无法暴露问题。

### 防回归规则

- LCU 写操作的**安全状态门槛**与**可选兼容字段**必须分开建模；不能因为某字段在 Riot/第三方工具样例中常见，就默认腾讯也必须提供。
- 自动找局核心门槛只使用已经腾讯验证的语义：Lobby、客户端明确 `canStartActivity`、本地房主、存在真实成员；`partyId/queueId/allowedStartActivity/warnings/restrictions` 只能在有可靠语义时参与 fingerprint/诊断，不得无证据一刀切阻塞。
- 自动接受以 Gameflow `ReadyCheck` episode 为主触发；search state 可用于明确 Accepted/Declined 时抑制写入，但读取失败或缺少 `lobbyId/queueId` 不能反向阻止本次 ReadyCheck。
- fail-closed 必须有用户可见/日志可查的稳定 skip reason；禁止“安全判断很多但全部静默”，否则实机只剩“没效果”。
- 每个腾讯相关自动化 fixture 至少包含一组**字段缺失/部分返回**样例，而不是只测试最完整 JSON。
- 发布说明必须区分 deterministic 通过与腾讯实机验收；未实机验证时不能把“已发布”写成“国服已可用”。

### 关联

- Issue #118
- PR #119
- Build #1050 / #1059

## 不要在 WinForms UI 线程递归扫描文件系统

### 根因

`GameLocator.ResolveGameRoot` 曾直接在控制中心事件处理器里执行向下 BFS。用户选择盘符根目录或大型上级目录时，同步 `Directory.EnumerateDirectories` 会冻结 UI；只有最大深度而没有时间、目录数量和取消预算，并不能限制同一层级的目录数量。

Windows 路径规范化还有一个容易忽略的语义差异：`C:\` 是盘符根目录，而 `C:` 是该盘当前工作目录。对根路径无条件 `TrimEnd('\\')` 会改变含义。

### 防回归规则

- 递归/广范围文件系统探测必须离开 WinForms UI 线程运行，同时保留可响应的进度/取消界面。
- 除深度限制外，必须同时有明确的时间预算和已发现/检查目录数量预算。
- 枚举目录时就执行预算检查，不能先把无限量子目录全部塞进队列。
- 跳过 reparse point；从盘符根等宽范围搜索时，不递归进入 Windows、Program Files、Program Files (x86)、ProgramData 等系统目录。
- 路径规范化必须保留盘符/UNC 根路径的根分隔符，不能把 `C:\` 变成 `C:`。
- `FACM.exe --game-locator-test` 必须继续验证：标记目录定位、目录数量预算、取消和根盘规范化。

### 关联

- Issue #16
- PR #22

## Stacked PR gate 不应把祖先生产控制差异误判为本轮修改

### 根因

P2-P7 是 stacked PR。若 source gate 固定比较 `origin/main...HEAD`，祖先阶段已经存在的 `online/version.json`、`release/request.json` 或 evidence matrix 差异会被误报为当前 P7 修改，导致一个未改生产控制的 candidate 无法通过 cutover/architecture/evidence gate。

### 防回归规则

- 生产控制保护必须比较当前 PR 的实际 base；在 merge ref 上使用 PR base，在本地 candidate 上使用 candidate 的直接父提交。
- gate 失败时先输出 changed paths 与比较基线，区分当前提交真实修改和 stacked ancestor 差异；不得删除保护检查或忽略文件。

## .NET 10 WPF/WinForms host 不要继续在 manifest 中声明旧 DPI 节点

### 根因

启用 `UseWPF` + `UseWindowsForms` 的 host 在 .NET 10 会由 Windows Forms analyzer 报 `WFAC010`：旧 `dpiAware` / `dpiAwareness` manifest 节点应移除，改由 `ApplicationHighDpiMode` 或 API 配置。将 warning 当作非错误或降低 warnings-as-errors 会掩盖真实 host 配置问题。

### 防回归规则

- WPF/WinForms host 使用 `ApplicationHighDpiMode=PerMonitorV2`，manifest 只保留仍需要的兼容/长路径声明。
- 修改后必须重新执行 host publish/self-test、solution build 和 WindowsSmoke；不能只让 analyzer 静音。

## Source gate 扫描必须排除构建目录

### 根因

本机在先完成 App publish 后再次执行 shell gate 时，`src/FACM.App/bin/...` 内由 Windows App SDK 生成的 `Microsoft.UI.Xaml` 目录被 `Get-ChildItem -Filter '*.xaml'` 返回，gate 随后对目录调用 `Get-Content` 而失败。干净 checkout 的 CI 顺序不会暴露这个问题，但完整本机验证会。

### 防回归规则

- 扫描源码文件时必须明确使用 `-File`，并在必要时排除 `bin`/`obj`；
- source gate 应能在 source-only 和 build/publish 后重复执行，不能把生成目录当作源码错误；
- 修复 gate 选择器，不要删除检查或清理掉真实源码证据来“让测试绿”。

## `ResponseHeadersRead` 不代表正文也受超时控制

### 根因

海斗外部请求曾使用 `HttpCompletionOption.ResponseHeadersRead`，随后在 .NET Framework 4.8 调用没有 cancellation token 参数的 `ReadAsStringAsync()` / `ReadAsByteArrayAsync()`。一旦响应头已经返回，外层 3/5/7 秒预算可能无法再中止卡住的正文读取。

仅给 `HttpClient.SendAsync` 传 token 不足以证明完整响应受控；必须把取消覆盖到 response body 的每一次读取。

### 防回归规则

- 海斗使用 `ResponseHeadersRead` 后，需要读取正文的代码统一走 `CancelableHttpContentReader`，不要直接调用无 token 的 `ReadAsStringAsync()` / `ReadAsByteArrayAsync()`。
- 正文分块读取必须使用相同 cancellation token；取消时同时 Dispose 正在阻塞的 response stream，防止底层流忽略 token 后永久挂起。
- 因取消导致的 `ObjectDisposedException` / `IOException` / `HttpRequestException` 应规范化为 `OperationCanceledException`，让上层继续区分用户取消和正常网络失败。
- 外部文本/图片正文保持明确大小上限，避免异常响应造成无界内存增长。
- `FACM.exe --mayhem-body-cancellation-test` 必须保留一个故意忽略 token、首字节后阻塞的模拟正文流，证明即使底层读取不合作也能在预算内退出。
- 只检查状态码且不读取正文的 HEAD/存在性探测不需要强行使用正文读取器；LCU 本地接口若使用默认完整缓冲 `GetAsync` 且有独立短超时，也不要误改成同一问题。

### 关联

- Issue #17
- PR #23

## 实时第三方健康检查不能和核心构建绑成同一个门禁

### 根因

`--mayhem-source-test` 会真实访问海斗实时来源。它很适合发现第三方页面结构、WAF 或资源路径变化，但第三方 429/5xx/网络抖动与 FACM 自身是否能正确编译、打包没有必然关系。

把这种 live probe 放在 `FACM.csproj` 的 `AfterTargets=Build` 中，会让桌宠、清理器等完全无关的提交因为外部网站临时故障而变红。

### 防回归规则

- `ValidateRuntimeSourcesAfterCiBuild` 只放 deterministic、本地可重复 smoke；禁止把实时公网探测重新塞回核心 MSBuild target。
- 真实海斗数据源健康检查统一放在 `.github/workflows/mayhem-source-probe.yml`。
- PR 上的 live probe 是 advisory；第三方失败不能代替核心 `FACM Windows Build` 的代码回归结论。
- main/定时 live probe 失败需要保留 stdout/stderr/FACM 日志 artifact，先区分外部服务故障与解析器真实回归。
- 修复第三方解析器时仍应尽量增加 deterministic fixture/smoke，不能只依赖“今天网站恰好能访问”。

### 关联

- Issue #19

## `cancel-in-progress` 的并发键不能包含 commit SHA

### 根因

核心 Windows CI 曾使用 `github.sha` 作为 concurrency group 的一部分。SHA 每个提交都不同，所以同一 PR/分支的旧构建和新构建永远不在同一个组；即使配置了 `cancel-in-progress: true`，也没有旧运行可以被匹配取消。

### 防回归规则

- 核心 CI concurrency 应按稳定的逻辑身份分组，例如 **事件类型 + PR number/ref**，不要按 commit SHA 分组。
- 同一 `pull_request` 的后续提交必须能取消旧 PR run；同一 branch `push` 的后续提交也应取消旧 push run。
- `push` 与 `pull_request` 保持不同 concurrency group，避免 branch push 把 PR required check 对应的运行取消掉。
- 不同 PR/不同分支要保留并行能力。

### 已验证行为

PR #25 首个 HEAD 的 PR Build #442 在后续同 PR 提交触发 #444 后，被 GitHub 明确标记为 `cancelled`；PetHost publish 阶段被终止，后续 Release、资源校验和打包步骤均被跳过。

### 关联

- Issue #21
- PR #25

## 不要在 WinForms 首次绘制后用 Idle + 反射补布局

### 根因

控制中心最初只原生创建 3 个底部按钮，随后 `CompactMenuEnhancer` 等到 `Application.Idle` 才反射创建“桌面宠物/海斗排行榜”并移动 5 个自绘按钮。窗口首帧已经画完后再移动自绘控件会留下旧像素；鼠标 hover 会触发单个按钮 `Invalidate()`，于是用户看到“把鼠标逐个移一遍后排版才正常”。

### 防回归规则

- 能在构造/首次 Show 前确定的控件布局，不要延迟到 `Application.Idle` 才改变几何位置。
- 兼容层暂时无法移入原生构造器时，必须在第一条 `WM_PAINT` 真正分发前完成布局；不能只赌 `WM_SHOWWINDOW` 会经过 `IMessageFilter`。
- 最终布局后执行 `PerformLayout + Invalidate(true)`；Idle 只可作为异常兜底，不再承担正常布局职责。
- 自绘控件的 hover repaint 不能承担“修复布局”的职责；如果只有 hover 后画面才正常，应按首帧布局/无效区域错误处理，而不是增加更多 hover 刷新。
- 后续重构 `CompactMenuForm` 时应最终把正式入口收回原生布局，并删除兼容反射层。

### 关联

- Issue #26
- PR #27

## IPC 打开的弹窗不能只依赖 `Deactivate` 判断外部点击

### 根因

VPet 点击发生在独立 PetHost 前台进程中。FACM 收到 IPC 后调用 `Show/Activate`，但 Windows foreground activation 限制可能不允许 FACM 真正成为前台窗口。若控制中心从未成功激活，用户下一次点桌面空白处就不会产生预期的 `Deactivate`，面板因此一直留在屏幕上。

### 防回归规则

- 跨进程点击打开的轻量 popup 要保留 `Deactivate`，但不能把它作为唯一 outside-click 信号。
- outside-click watcher 必须先等待“打开 popup 的那一次鼠标按键”完全释放，再对下一次按下边沿做命中判断，否则会刚打开就被同一次点击关闭。
- 文件夹选择器、消息框等内部 modal 流程必须有明确抑制条件，不能被全局 outside-click 误关。
- 不安装进程级低级鼠标 hook 来解决普通 popup；优先使用轻量定时物理键状态 + 屏幕 Bounds，降低权限、稳定性和反作弊软件兼容风险。

### 关联

- Issue #26
- PR #27

## 不要在 UI 线程等待辅助进程解包、启动和 IPC

### 根因

`VPetHostClient.Activate` 曾从 WinForms UI 事件同步执行：内嵌 PetHost ZIP 检查/释放 → `Process.Start` → `NamedPipeClientStream.Connect(7000)`。任何磁盘慢、杀软扫描、首次释放或 pipe 启动延迟都可能直接让控制中心假死，最坏等待 7 秒。

### 防回归规则

- 大包校验/解压、辅助进程启动、IPC connect/readiness 等必须离开 WinForms UI 线程。
- UI 线程只提交启动意图和更新显示；失败通过捕获的 `SynchronizationContext` 回到 UI 恢复状态。
- 辅助进程停止也不要在 UI 线程同步 `WaitForExit`；先发 stop，再后台等待/兜底 kill。
- 独立辅助进程如果属于 FACM 产品运行树，应使用 Job Object 的 `KILL_ON_JOB_CLOSE` 管理生命周期，同时保留子进程自己的 parent-pid 守护作为兼容兜底。
- 不为了“单 PID 好看”在发布前把不同 CLR/UI 技术栈硬塞进同一进程；需要整体迁移时作为独立架构版本处理。

### 关联

- Issue #26
- PR #27

## 内嵌子宿主缓存身份必须跟 payload 绑定，缓存命中不要全目录体检

### 根因

PetHost 是一整套 self-contained .NET 8 runtime。旧实现用 `FACM.exe` 的 MVID 作为 `runtime\pethost-host` 缓存目录身份，并在每次缓存命中时递归枚举宿主全部文件、重新统计文件数和总字节。

这有两个问题：

1. FACM 主程序只改业务代码时，MVID 变化也会强制重新释放完全相同的 PetHost；
2. 即使已经有完整缓存，Windows Defender、机械盘或慢 SSD 对几百个 runtime 文件逐个扫描也可能明显推迟 `Process.Start(FACM.PetHost.exe)`，导致用户看到桌宠加载卡几十秒后才出现。

### 防回归规则

- 内嵌辅助运行时的缓存 key 必须来自辅助 payload 本身；PetHost 使用内嵌 ZIP 的 SHA-256，而不是 FACM 主程序集版本/MVID。
- 首次解包完成前做完整统计并写不可变完成标记；后续缓存命中只检查 payload 身份、完成标记和启动关键文件，不要重复全目录枚举。
- **只有配置已经启用桌宠时**，FACM 启动后才应后台预热当前精确 PetHost；默认 FACM Shell 路径不预热 PetHost。用户实际启用时与预热共享同一任务，不能并发重复解包。
- 任何 PetHost payload 改动必须得到新的 SHA 目录，禁止为了启动快而复用不匹配旧宿主。
- 性能实机验收至少测试新 bundle 第一次启动和关闭 FACM 后第二次启动；第二次必须走快速缓存路径。

### 关联

- PR #40

## 不要把可选桌宠的优化提升成默认启动依赖

### 根因

PR #40 排查 PetHost 首次释放慢时，曾把“尽早准备 PetHost”误当成 FACM 全局启动目标，并据此让所有启动都预热 PetHost。这个推导忽略了产品配置语义：桌宠本来就是可选能力。用户随后明确调整产品方向——默认需要一个 FACM 自己的专业 Shell 作为可见入口，但这并不等于默认应该加载 VPet。

这类问题的本质是把“解决一个可选模块的体验问题”错误扩张成“修改整个产品的默认启动链”。

### 防回归规则

- 修改默认启动体验前，必须先审 `AppSettings` 默认值、`MainForm` 启动条件、功能启用开关和实际用户配置，不能从某个测试场景倒推全局默认行为。
- FACM Shell 与桌宠必须是两个概念：Shell 是主程序自己的轻量入口；桌宠是用户选择的桌面形态。
- `AnimalPetEnabled=false` 时显示 FACM Shell，但不得因此读取、解包、扫描或启动 PetHost。
- PetHost 只能在“配置已启用桌宠”或“用户刚主动选择桌宠”后进入准备链。
- 性能修复不得静默改变无关用户的默认资源成本、桌面行为或功能启用状态。
- 讨论产品方案时先分清“默认路径、可选路径、失败回退路径”，再决定哪些阶段需要进度条、预热或替换 UI。

### 关联

- PR #40

## 海斗数据必须按字段降级，不能让单一网站决定整次查询

### 根因

早期查询虽然同时访问 OP.GG 与 ARAMMayhem，但英雄识别、攻略字段和整体等待仍与 OP.GG 强耦合。国内访问 OP.GG 不稳定时，用户会把“一个攻略源不可达”体验成“海斗排行榜坏了”。同时，版本公告是增量日志，直接拿最新公告当完整 Buff 状态会丢掉历史未改字段。

### 防回归规则

- 排行、攻略、完整平衡状态、官方版本校验、静态图标必须是独立职责；任一可选字段失败不应抹掉其它已获得字段。
- 国内可访问的数据源优先承担核心排行；OP.GG 只作为攻略补充，不能重新成为整体成功条件。
- 每个来源有自己的短预算，不能让一个站点耗完整体查询时间。
- 腾讯版本公告只表示“这个版本改了什么”，不能单独推出“这个英雄现在所有修正是什么”。
- 完整状态来源的 Patch 与当前官方 Patch 不一致时，宁可明确显示同步中，也不能把旧数值伪装成最新状态。
- 新 HTML 解析器至少要有一个离线 fixture 回归；真实公网兼容性由独立 source probe 负责。

### 关联

- Issue #26
- PR #27

## 异步方法不代表缓存命中路径一定离开 UI 线程

### 根因

`MayhemImageCache.GetAsync` 以前在方法第一次真正遇到不完整 `await` 之前，会同步读取磁盘缓存并 `Image.FromStream` 解码 Bitmap。`MayhemCardRenderer` 一次最多准备 32 个图片引用，因此缓存全部命中时，反而可能在调用线程连续完成大量磁盘读取/图片解码，造成“缓存越热，点查询越容易顿一下”。

### 防回归规则

- 检查 async 方法的**首个未完成 await 之前**是否仍有磁盘、图片解码、压缩/解压等重工作，不能只看方法签名里有 `async` 就判定不会阻塞 UI。
- 海斗磁盘缓存读取和 Bitmap 解码放到后台任务；两类工作分别限制为最多 4 路并发，避免 20～30 张图片同时争抢磁盘/CPU。
- 内存字节缓存查找可以同步，但图片解码本身仍按 CPU 工作处理。
- 网络下载继续使用真正 async I/O，不要用 `Task.Run` 包一层网络请求。

### 关联

- Issue #26
- PR #27

## 清理预览与删除不能把控制中心消息循环一起锁住

### 根因

`SafeCleanupService.CreatePlan` 会递归统计配置目录的文件数/大小，`Execute` 会逐文件重校验和删除。即使清理规则本身完全安全，大目录、慢盘或杀软扫描都可能让同步调用持续数秒。旧版从控制中心事件直接调用这两个方法，导致窗口在“正在生成清理预览/正在清理”时无法重绘，看起来像程序卡死。

### 防回归规则

- 清理路径白名单、重解析点检查、二次校验逻辑保持在 `SafeCleanupService`，不要为了异步化复制一份删除算法。
- 从 WinForms 消息循环调用时，预览扫描和正式删除由 `BackgroundOperationDialog` 在后台工作线程运行；前台只显示不可误点的进度窗并保持消息泵响应。
- 删除阶段不提供任意中断按钮，避免用户误以为中止是事务回滚；失败继续按现有逐目标记录语义处理。
- 非 UI/测试调用保持同步 core 路径，避免服务层被迫依赖一个正在运行的窗口。

### 关联

- Issue #26
- PR #27

## 同一个启动卡可能覆盖多个加载阶段，先确认用户看到的是哪一阶段

### 根因

PetHost 的启动卡至少覆盖两类不同工作：`VPetAssetBootstrapper` 的资源准备/核对，以及 `VPetMain.LoadALL` 的动作图缓存。两者都会在同一个 FACM 状态卡里出现 `x/N`，但总数、进度语义和可用回调并不相同。

PR #40 第一轮只改了后面的 `LoadALL` 显示，所以源码里虽然已经有“正在编译着色器…”和进度条，用户实机看到的前一段 `60/1995` 仍然是旧“正在缓存高精度动作”。问题不是用户跑错 EXE，而是修改了错误的加载阶段。

### 防回归规则

- 改启动/加载 UI 前，先沿着用户看到的**具体文字、计数和回调**定位实际 emitter，不能只凭“都是加载过程”猜阶段。
- 截图/视频里的 `x/N` 必须映射到具体源回调；同一个视觉卡片不代表同一个底层进度源。
- 产品需要统一视觉时，可以让多个阶段走同一个 renderer，但每个阶段仍保留自己的真实计数和语义。
- 不得把两个阶段的总数拼成一个假的连续百分比。
- 某阶段没有可信递增进度时，用 indeterminate 状态，不显示长期 `0%`。

### 关联

- PR #40

## ToolStrip 下拉菜单的 `Closed` 不是同步 Dispose 的安全边界

### 根因

PR #40 新增统一「主题」下拉菜单时，在 `ContextMenuStrip.Closed` 事件里直接 `menu.Dispose()`。但 WinForms 触发 `Closed` 时，内部 `SetVisibleCore`、`OnItemClicked` 和 `ToolStripManager.ModalMenuFilter` 的当前消息栈仍可能继续访问该对象。

Build #741 实机日志因此出现连续 `ObjectDisposedException`：一次来自 outside-click Timer，一次来自 `OnItemClicked`，最终还能在 `ModalMenuFilter` 中变成未处理异常并终止消息循环。同步打开新的 modal 窗口/选择器也会增加 ToolStrip 点击栈的重入风险。

### 防回归规则

- `ToolStripDropDown/ContextMenuStrip.Closed` 只表示“已进入关闭流程”，不能在事件回调里同步 Dispose 自身。
- 需要主动释放临时下拉菜单时，通过稳定 owner 的 `BeginInvoke` 把 Dispose 推迟到当前 ToolStrip 消息栈完全退出之后。
- 菜单项如果会打开 modal 窗口、切换桌面形态或触发较大 UI 状态变更，也应通过 `BeginInvoke` 推迟到当前 item-click 栈退出后执行。
- 自定义 outside-click Timer 在 Dispose 开始时必须停止并解绑；Tick 入口还要检查 `Disposing/IsDisposed`，因为已经排队的 WM_TIMER 仍可能晚到一次。
- 处理关闭竞态时，`ObjectDisposedException` 可作为“目标已经关闭”的终态，但不能只靠 catch 掩盖同步 Dispose 的错误时序。

### 关联

- PR #40

## OP.GG ARAM 真实页面不能假设版本标签和正负号紧贴数值

### 根因

完整基础 ARAM 状态首次接入 FACM 时，离线 fixture 使用了理想化的 `Patch 16.15`、`Attack Speed +2.5%` 形态。接口项目随后在真实页面验收发现：OP.GG 的 HTML/可见文本可能把版本写成 `Ver: 16.15` / `Version: 16.15`，也可能把正负号和数字拆成 `+ 2.5%`。如果解析器只接受单一形态，会出现两类假降级：有真实 Buff 却显示“完整平衡暂不可用”，或平衡数值正确但版本被标成“未校验”。

平衡卡附近还可能出现广告尺寸、胜率等普通无符号数字。把所有数字都当成“未知平衡字段”会把正常页面误判为不完整。

### 防回归规则

- Patch 提取至少兼容 `Patch`、`Ver`、`Version`、`16.15 版本`、`版本号：16.15` 等明确版本标签；不要从页面任意裸版本数字猜当前平衡版本。
- 平衡字段允许 `+/-` 与数字之间出现空白，解析后统一归一为 `+2.5%` / `-20` 这类稳定值。
- 完整性 fail-closed 只对**未识别的带符号调整值**生效；广告尺寸、场次、胜率等无符号数字不能触发 `unparsed_balance_values`。
- 未识别的 `- 15%` 这类带空格新字段仍必须 fail-closed，不能为了兼容页面噪声而放弃未知修正保护。
- `--aram-base-balance-test` 至少保留亚索（单正向攻速）、库奇（承伤 + 冷却）和萨勒芬妮（多项持续 debuff）三类 deterministic fixture；公网可访问性继续由独立 Mayhem source probe 验证。

### 关联

- PR #36
- PR #37

## 发布工作流不能用静态模板覆盖项目状态

### 根因

FACM 3.1.3 正式发布时，`.github/workflows/publish-release.yml` 的最终在线更新步骤会整份重建 `docs/PROJECT_STATE.md`，其中还残留历史发布的 `Build #495 / Issue #28 / 3.1.0` 固定文字。发布二进制、签名和在线清单本身都正确，但 canonical 项目状态被旧模板覆盖，导致后续 AI/维护者读取到错误的当前版本与验收历史，只能再人工恢复。

### 防回归规则

- 发布自动化只能维护 `PROJECT_STATE.md` 中一个有明确 begin/end marker 的机器所有区块，不能整份重建 canonical 状态文档。
- 机器区块只记录本次 workflow 能直接证明的事实：版本、Release tag、online enabled、`minimum_version`、`force_update`、发布基础/元数据 SHA、FACM.exe SHA-256、`published_at` 和 release notes。
- 不得在发布脚本里硬编码 Build 编号、Issue/PR 编号、用户实机验收结论或任何历史版本专属描述；这些信息只能由实际任务/验收流程写入普通项目状态区。
- 更新 release 区块必须幂等：marker 已存在时只替换该区块；marker 不存在时插入，不删除其余开发、验收和后续任务状态。
- 修发布状态写入逻辑时不得通过触发真实 Release 来“测试”；优先静态检查、YAML/PowerShell 语法检查和普通 CI，避免为验证文档逻辑误发版本。

### 关联

- Issue #49
- PR #50

## 外部激活控制中心必须是 Ensure Open，不能复用 Toggle

### 根因

Issue #53 / PR #54 把“第二次启动 FACM.exe”从单纯报“已经在运行”改成唤醒现有实例。控制中心原有 `ToggleMenu()` 语义是“关闭则打开、打开则关闭”，适合用户点击同一个悬浮入口，但不适合作为跨进程/跨实例 activation 回调。

如果第二实例收到 Mutex 已占用后直接让第一实例调用 `ToggleMenu()`，那么用户本来已经打开控制中心时再次双击 EXE，会把控制中心关掉，和“把 FACM 叫出来”的产品语义完全相反。

本轮初版还出现过一个独立的 C# 编译错误：`catch (InvalidOperationException)` 写在 `catch (ObjectDisposedException)` 前面。`ObjectDisposedException` 继承 `InvalidOperationException`，因此后一个 catch 永远不可达，Build #794 以 CS0160 失败。该错误与 AutoResetEvent 方案无关，不应因此重写 IPC。

### 防回归规则

- 外部/二次启动 activation 使用 **Ensure Open**：控制中心不存在则创建，已经存在则 `BringToFront/Activate`；禁止直接复用 `ToggleMenu()`。
- 普通 Mutex 继续只负责单实例所有权；命名 AutoResetEvent 只负责无参数 activation，不要为了一个布尔信号引入 socket/HTTP/重型 IPC。
- 第一实例刚启动时允许 pending activation，第二实例只做短时间有限重试；禁止无限等待。
- 第二实例只发信号然后退出，不 kill/restart 第一实例，不修改桌宠状态。
- `--cleanup` 和 smoke/test 模式继续使用各自独立 Mutex，不得误接普通 activation channel。
- C# 捕获存在继承关系的异常时，具体子类必须排在父类前；不要用 catch 顺序错误掩盖实际 UI 生命周期问题。
- `FACM.exe --single-instance-activation-test` 必须继续验证 listener 缺失有限失败、首次激活和重复激活；涉及前台窗口语义的修改仍需要 Windows 实机测试。

### 关联

- Issue #53
- PR #54
- Build #794（catch 顺序编译失败）
- Build #797（修复后 CI + 用户实机验收通过）

## WinForms 项目不要用 `FACM.Application` 作为根级业务 namespace

### 根因

FACM 3.2 modular-host Phase 1 最初把新宿主放进 `namespace FACM.Application`。由于大量旧文件本身位于根 `namespace FACM`，其中原本正常使用的未限定类型名 `Application` 被 C# 名称解析优先绑定到新建的 `FACM.Application` namespace，而不再是 `System.Windows.Forms.Application`。

Build #821 因此一次出现大量 CS0234，包括 `Application.Run`、`OpenForms`、`MessageLoop`、`EnableVisualStyles`、`SetCompatibleTextRenderingDefault`、`ExecutablePath` 等“在 FACM.Application 中不存在”。PetHost publish/self-test 当时仍成功，说明故障只来自 net48 主项目的新 namespace 污染，不是 Host 模块化设计或 PetHost 行为回归。

### 防回归规则

- FACM modular host 的稳定 namespace 使用 `FACM.AppHost` / `FACM.AppHost.Modules`；文件目录可以叫 `Application`，但 namespace 不要改回 `FACM.Application`。
- 在 WinForms 根 namespace 附近新增 `Application`、`Form`、`Timer`、`Control` 等常见框架类型同名 namespace/type 前，先搜索项目内未限定引用，避免全项目名称遮蔽。
- 遇到这种批量“框架成员突然不存在”的编译错误，先检查名称解析/namespace collision，不要逐个旧文件加 fully-qualified 名称掩盖根因。
- 修复 namespace collision 时优先改新命名空间本身，保持已验证旧业务文件不动。
- `--facm-host-test` 和核心 Windows Build 必须继续作为 Host 架构变更的 deterministic 门禁。

### 关联

- Issue #55
- PR #56
- Build #821（namespace collision）
- Build #832（改为 `FACM.AppHost` 后通过）

## 把构造依赖显式化时，不能只搜索产品入口而漏掉 deterministic test

### 根因

FACM 3.2 Phase 2 把 `MainForm` 从内部 `AppSettings.Load()` / `UiTextCatalog.Load()` 改为构造函数显式接收依赖。正常产品入口 `ShellModule` 和 `Program` 都已经迁移，但 `FloatingBallSmokeTest` 仍保留旧的 `new MainForm(false)`。

Build #845 因此在 FACM 编译阶段以 CS7036 失败；PetHost publish/self-test 在此之前保持成功。这不是 Settings ownership 方案失败，而是构造契约改变后漏掉一个 deterministic test 实例化点。

### 防回归规则

- 修改构造函数、接口或 service ownership 前，先全仓搜索所有 `new TypeName(...)`、factory、reflection/test helper 调用点，不要只看正常产品启动链。
- deterministic smoke 也必须遵守新的显式依赖契约；不要为了让旧测试继续编译而恢复隐式 global load 或增加“方便测试”的旧行为重载。
- 测试如果不需要磁盘真实 settings，应传入明确的 test/default `AppSettings` 对象；需要 UI text 时可以显式加载对应测试依赖。
- 构造依赖迁移后的第一轮编译失败如果只指向漏改 call site，应修 call site，不应推翻依赖注入方向。
- `--facm-host-test` 应锁定真实模块 dependency contract，防止后续把 Settings→Shell 又改回隐式加载。

### 关联

- Issue #57
- PR #58
- Build #845（旧 `FloatingBallSmokeTest` 构造调用）
- Build #846（显式注入修复后成功）

## IPC 写入超时不能只包裹外层 WaitAsync，Host 也不能在 activate 前 Show

### 根因

桌宠 Runtime 旧实现把不可取消的 `StreamWriter.WriteLineAsync`/`FlushAsync` 放进外层 `WaitAsync`。外层任务超时并不代表底层写入结束；随后清理阶段仍可能复用已污染的 writer。与此同时，FlyingHost/PetHost 的 `Program` 在进入 `Application.Run()` 前无条件 `window.Show()`，因此每次切换都可能先显示一个未完成 activate 的 Host。

### 防回归规则

- activate/reset/stop 命令写入必须把取消令牌传到 `WriteLineAsync` 和 `FlushAsync`，并为每次命令保留有界预算。
- activate 写超时或写异常后必须标记 transport poisoned；清理只做 detach/dispose、进程 wait/kill/wait/dispose，不再发送 graceful stop。
- Host 的 `Program` 不得预先 `Show()`；Dispatcher 先运行，`activate` 才允许 `Show()`，随后才进入 `Loaded -> ready`。
- IPC server 连接后直接进入命令 reader，不得预发送会占用命令时序的 `connected` event。
- deterministic Windows smoke 必须覆盖激活顺序、取消写入无 pending task、关闭 transport 的 stop 写失败仍 fail-soft，以及串行 Host 会话。

### 关联

- FACM 4.0 / PR #234 / Batch P
- `artifacts/facm4-win10-targeted-batch-p.zip` targeted candidate

## 2026-08-30：Workbench 后台 PropertyChanged 不能直接读取 WinUI

### 根因

`LeagueWorkbenchViewModel.RefreshAsync` 在 `ConfigureAwait(false)` 后更新快照，并同步触发 `PropertyChanged`。若 MainWindow 订阅器在回调入口读取 `NavigationView.SelectedItem`，该读取发生在后台线程，会抛出 `COMException`，使日志看起来像 Workbench refresh failure，甚至可能被误判为 FACM 崩溃。

### 防回归规则

- ViewModel 可以在后台线程发布数据，但所有 WinUI 导航、控件、窗口状态读取和写入必须先通过 Dispatcher。
- PropertyChanged、Gameflow Changed/Observed 和其它后台 observer 必须逐个隔离异常；一个 observer 失败不能中止共享 monitor 或吞掉后续 observer。
- 真实诊断必须同时检查 FACM PID 是否退出、lifecycle/fatal 事件和 Workbench stage pair；单条 `COMException` 不能直接称为进程崩溃。

## 2026-08-30：验证 App 时必须确认实际输出目录

FACM 工程的 Debug 构建可能输出到 `bin\Debug`，而旧测试启动器仍可能指向历史 `bin\x64\Debug`。启动前必须核对 EXE 的完整路径和修改时间，否则会把旧二进制的失败误判为新修复失败或把旧行为误判为新行为。

## 2026-08-30：Morphing Surface 不能重新引入多个 UI shell 或 League owner

### 根因

将 Orb、控制中心、功能窗口、League 工作台和桌宠入口分别实现为独立常驻窗口，会让一次用户切换同时留下多个宿主；如果每个页面再创建自己的 Gateway、Gameflow monitor、session 或 polling loop，卡顿和生命周期问题会被放大，且无法判断哪个窗口代表当前状态。

### 防回归规则

- 默认只允许一个持久 `MainWindow` 主宿主；其它模式必须在该宿主内 morph，legacy 多窗口只能通过显式 `FACM_SHELL_EXPERIENCE=legacy` 对照启用。
- 新视觉组件只能消费现有 ViewModel/service，不得创建第二个 League transport、Gameflow monitor、session owner、polling loop 或 cache。
- 任何外观切换都必须保持 outside-click、modal suppression、single-instance、tray、桌宠、InGame hide 和 Lobby 回 Orb 契约。
- source gate 如果扫描 `obj` 生成副本，必须先确认扫描的具体路径与当前构建是否已刷新；不要把 stale generated XAML 当成产品源文件，也不要为规避扫描修改平台项目的换行噪声。

## 2026-08-31：BOOT-1 启动验证必须隔离旧候选进程并核对准确路径

BOOT-1 的 native bootstrapper 和 FACM managed app 共享单实例边界。若旧 review 目录的
`FACM.App.exe` 未通过自身窗口关闭流程退出，新候选即使已被 bootstrapper 正确创建，也会因单实例
保护立即退出，表现为“没有 desktop-launcher-ready”。

防回归规则：

- 每次启动前按完整 `ExecutablePath` 核对目标 candidate，不要只按进程名判断；
- 不同候选之间切换时，先正常关闭旧候选并确认进程退出，再启动新候选；
- `Start-Process` 的参数数组测试必须确认实际 child command line；参数传递错误可能把 bootstrap-only
  校验调用误变成默认启动；
- ready、bootstrap correlation、active Core 路径和 shutdown-complete 必须同时作为启动证据，不能只
  看 bootstrapper 的 process-created 日志。

## 2026-08-31：BOOT-2 MakeCAB 分卷和内容摘要必须验证真实语义

BOOT-2 初版 DDF 使用默认磁盘大小，`makecab` 将一个组件拆成多个约 1.44MB 分卷；由于模板名没有卷号，
后续卷覆盖了前一卷，下载包虽能通过局部尺寸/哈希却无法完整 FDI 解包。修复为单 CAB、`MaxDiskSize`
为 512 字节对齐的受控上限，并在 manifest 中记录实际 CAB size/hash。

随后摘要校验出现误报：PowerShell 默认排序通常不等价于 C++ `std::sort` 的 ordinal 大小写排序，导致
同一组文件生成不同 `contentDigest`。组件摘要必须按与 native verifier 完全相同的相对路径、`/` 分隔和
ordinal 顺序计算，并同时校验 file count 与 installed size；不能只看压缩包 hash。

预防规则：生成每个包后先检查 setup/卷数量与实际输出文件数，再用 native extractor 做 round-trip；
ownership、package hash、expanded digest、file count 和 byte sum 必须进入同一份审计报告。失败 staging
只能保留在受控 `.facm\staging`，active 版本不得被清理流程顺手删除。

## 2026-08-31：不能把 Authenticode 当作 JSON/CAB manifest trust

### 根因

仓库已有的 Authenticode 机制面向 PE 可执行文件的签名者、证书链和发布版本身份；它不能天然认证
application/component JSON 的精确字节，也不能把组件 manifest 的 package hash 与 extracted content digest
串进 bootstrapper 的生产信任链。若只增加一个 `signed=true` 配置或沿用 unsigned-local 镜像，签名边界仍可被
配置/传输替换绕过。

### 防回归规则

- 清单签名必须是 detached exact-byte signature；生产 key identity 必须来自 bootstrapper 内嵌 keyring，
  不得来自配置、任意系统根或测试私钥。
- 应用清单认证 component-manifest URL/bytes hash、package hash 和 extracted digest；组件清单要独立签名
  并逐字段匹配，包在验证后才能转正。
- unsigned-local 只能通过显式 loopback HTTP 开发边界进入，任何生产 URL、组件 URL 或 `allow-insecure-local`
  组合都不能把它升级为生产信任。
- 负向 smoke 必须覆盖 altered signed bytes、unknown/test-only key、unsigned downgrade、metadata mismatch、
  package corruption 和 failed-update active preservation；失败时旧 active 必须仍可 resolve/launch。

## 2026-08-31：BOOT3-B 确定性管线必须显式处理 PowerShell 路径和对象语义

### 根因

BOOT3-B 首轮验证暴露了三个容易被脚本表面成功掩盖的问题：`Measure-Object` 不能直接对
`[ordered]` 字典对象的 `size` 属性做可靠求和；`Copy-Item -LiteralPath` 不展开受控的通配符；
确定性比较若从输出根目录开始，会把 `signing-request.json` 等预期外文件混入 bundle 比较。
此外，旧测试输出目录可能仍有被 MSBuild/NuGet 占用的文件，复用它会把环境锁定误报为产品失败。

### 防回归规则

- 对 manifest 的数值字段先显式投影为 `[int64]`，再求和；不要依赖字典对象的动态属性绑定。
- 只有在通配符已被人工限定到受控根目录时使用 `-Path`；需要单文件精确复制时使用 `-LiteralPath`。
- 确定性比较要明确比较边界：artifact bundle、release index 和 signing request 分别比较，不能混用输出根。
- 每轮完整构建使用新的 `D:\project2` 临时根；清理只能针对已确认的本轮目录，不能触碰用户既有输出或安装状态。
- B3 验证必须同时检查 unsigned request、签名后 exact-byte 校验、key rotation/replay/metadata/package/downgrade 负向路径。

## 2026-08-31：production-like HTTPS 测试必须区分 TLS 信任与 release trust

BOOT3-C 本地 origin 使用临时自签名证书，只为让 Windows WinHTTP 走真实 TLS 证书验证路径。该证书不能
被当作 FACM release key，也不能把 `NODE_TLS_REJECT_UNAUTHORIZED=0`、任意第三方根证书或 HTTP fallback
带入生产实现。测试根证书和 private key 必须在 `D:\project2` 临时目录中，运行结束删除；Windows 弹窗中
只确认名称/指纹属于本轮 `FACM BOOT3-C local test` 证书。

## 2026-08-31：mirror fallback 的可用性不能替代 exact-byte rejection

主站不可用可以按签名清单中的固定顺序切换镜像；但镜像仍必须通过 embedded key、detached signature、
metadata、package size/SHA-256 和 extraction digest。WinHTTP 必须显式禁止重定向，否则 HTTPS 到 HTTP 或
未授权主机可能被透明跟随，测试也不能只用一个 HTTP listener 证明 production-like 分发。

## 2026-08-31：低空间正向测试不能贴着当前 free-space 值

引导器本身会创建 correlation/log 文件，使用 `available - 1` 做“应通过”的断言会因为文件系统分配粒度
产生误报。低空间负向断言应使用 `available + 1`，正向断言保留足够余量（当前 harness 使用 256 MiB），并把
真实更新峰值按 package/partial、解包暂存、组合目录和 safety margin 计算。PowerShell 的 `Math.Max` 默认重载
还可能把大于 2 GiB 的磁盘值绑定到 `Int32`；磁盘字节数必须保持 `Int64`/`UInt64`。

## 2026-08-31：免费代理只能是 transport，不能进入 signed metadata 或 trust

### 根因

GitHub Release 公共代理的域名、证书、重定向和 Range 行为可能随时变化。若把代理地址写入签名清单，代理
可用性就会和 release identity、签名查找、回滚判断混在一起；若跟随任意重定向，还可能把 HTTPS 下载降级到
HTTP 或未授权主机。另一个常见错误是代理返回 `200` 或错误的 `206 Content-Range` 时直接拼接 `.partial`。

### 防回归规则

- signed metadata 只写 canonical GitHub Release URL；代理只由 native bootstrapper 对 canonical URL 派生，且
  必须按固定顺序最终回到 direct GitHub。
- WinHTTP 自动 redirect 保持关闭，只允许有界 HTTPS GitHub release/CDN host；拒绝 HTTP、user-info、任意域名
  和超深 redirect chain。
- resume 时验证 `Content-Range` 的起点和总大小；服务器在续传请求上返回 `200` 必须安全重启，不能把完整
  文件追加到 partial；每个候选完成后重新做包 hash 和解包内容校验。
- 每次候选验证都同时保留 canonical source URL；proxy response 不能改变 detached signature URL、key ID、
  downgrade 或 activation policy。
- 免费代理的当前可用性只能作为带日期的 compatibility evidence，不能写成 SLA；发布前后都要重新探测，并
  保留 direct GitHub fallback。

## 2026-09-01：GitHub Release 资产不能直接复用嵌套构建路径

本地 HTTP origin 可以按 `components/<id>/<version>/...` 提供目录树，但 GitHub Release 上传的是独立资产文件，
组件清单若都保留 `component.manifest.json` basename 会冲突，canonical URL 也不能直接假设远程保留本地目录层级。
发布候选必须先映射为唯一扁平文件名，再同步重写所有已签名 URL、release index、签名请求和验证脚本；需要把
“本地目录可服务”与“Release asset 可上传/下载”分开验证。

## 2026-09-01：单启动器测试要区分子进程观察与 bootstrapper 等待

真实启动路径会由 `FACM.exe` 创建并监督 `FACM.App.exe`。对 bootstrapper 使用 `Start-Process -Wait` 会等待
整个子进程树，导致测试在应用已经启动时仍看不到控制权，产生假阴性。专项 harness 必须异步启动 bootstrapper，
按候选安装路径观察真实 `FACM.App.exe`，然后只对本轮确切候选进程执行正常关闭和必要的精确清理。

同样，live transport probe 不能使用尚未发布的候选 Release URL；应使用可确认存在的公共 GitHub 资产验证
四路候选，另用本地签名 HTTPS origin 验证单启动器默认配置和 trust boundary。

## 2026-09-01：完整大小的 `.partial` 也必须可恢复

### 根因

FREE-DIST-4 公网首启中，强制终止恰好落在 CAB 最后一个字节写入之后、临时文件转正之前。此时 `.partial`
大小已经等于 authenticated package size，但当前 `DownloadUrl` 只拒绝 `existing > packageSize`，于是把
`resumeAt == packageSize` 编成 `Range: bytes=<packageSize>-`。公共服务器返回 HTTP 416，所有候选失败，完整的
临时包也不会被重新验证或转正。

### 防回归规则

- `partialSize == packageSize` 必须先做完整 package SHA-256/manifest 校验；校验通过则直接安全转正，不能发
  EOF Range；校验失败才应清理并从零开始下载。
- 中断回归必须覆盖非零前缀和“完整大小但尚未转正”两个窗口，并验证重启后无残留 `.partial`、精确 hash、
  解包 digest、active 提交和真实 Orb 启动。
- 记录 `component-download-resume` 不能单独作为通过证据；必须同时证明 Range 响应或完整包恢复后的最终
  exact-byte 和 activation 结果。

## 2026-09-01：长时间 Windows 回归不能用前台等待上限判定失败

BOOT3-C/单启动器脚本会下载并解包约 103 MB 的三个 CAB；在 Codex 前台等待窗口结束时，父 PowerShell 可能被
中断而留下测试子进程或 CurrentUser Root 测试证书。把日志重定向到测试根目录还会与 harness 的清理动作互相
锁定，造成看似无关的失败。

防回归规则：长回归用后台测试进程并把 stdout/stderr 放在测试根目录之外，轮询结果 JSON、精确测试进程和端口；
只终止命令行明确属于本轮测试根的进程。若证书清理遇到系统证书存储等待，不得把它解释为 release trust 失败；
同时保留最终 runtime evidence，并确认 WinHTTP 恢复为原始状态。单启动器的 Orb 关闭后检查要允许短暂进程枚举
时序，不能用一次立即查询制造假阴性。

## 2026-09-01：WinUI P7 的 UI 资产依赖必须停在 Core contract

### 根因

为给 League Workbench 增加 OP.GG 图标而让 WinUI 页面直接引用 Infrastructure，会绕过现有 composition boundary，
并使 source gate 失去对依赖方向的保护。

### 防回归规则

- 在 Core 定义最小的 `ILeagueGuideAssetService` contract，由 App composition root 注入具体实现。
- 远端图标是 decorative enhancement，不得阻塞 OP.GG JSON、改变 League session owner、增加 polling loop 或把
  网络失败变成空白推荐；缓存/路由失败必须保留文字行。
- 对 fixed-host、路径遍历拒绝、缓存和 fail-soft 行为保留 FoundationSmoke 覆盖，并在架构门禁后重跑 Workbench
  与 recommended source gates。

## 2026-09-01：Lobby 不能推断 ChampSelect，静态强化符文目录也不是英雄专属排行

真实 LCU 审计中，`gameflow-phase` 返回 `None`，两个已知 ChampSelect session 端点都返回
`404 expected-unavailable`，而静态 `cherry-augments.json` 仍可返回 657 条目录元数据。若把
目录可读或历史页面数据当作当前选人结果，就会把通用强化符文误报成当前英雄专属排名。

防回归规则：自动攻略必须等待共享 Gameflow owner 的 ChampSelect 状态；只绑定真实 payload
中的本地 action/champion 字段。Lobby、None、空 champion、未识别 action 或不存在的稀有度都
必须 fail-closed，不展示推测的当前英雄或伪造的强化符文排行。脱敏审计只保留 schema 形状、
计数、状态和版本信息，不得落盘 LCU token、账号身份或原始响应。

## 2026-09-01：OP.GG 海斗详细页可能慢且没有可验证的 Runes 表

手动海斗真实读取中，`zh-cn/lol/modes/aram-mayhem` 详细页在本机可能接近 4.5 秒才返回；此前的 1.8 秒预算会把可用的技能和出装误判成不可用。该页面同时没有当前可验证的 `Runes Table`，不能因为旧模型或其他模式数据存在就显示一套“推荐符文”。

防回归规则：详细源保持有界的 4.5 秒预算，并允许降级到已验证的基础结果；只有解析到真实来源字段时才显示技能、召唤师技能、出装或强化符文。缺失的可选 section 必须省略，不得显示内部术语、占位“暂无”或通用数据伪装成英雄专属推荐。手动路径保留为后续自动 ChampSelect 攻略的 fallback/detail，不得据此绕过真实 LCU ChampSelect 证据。

## 2026-09-01：自动攻略的 OP.GG 数字稀有度不能直接当作显示文本

真实用户截图中，Kled 的冠军、技能、召唤师技能和装备图标均已显示，但强化符文区域错误提示“当前数据源未提供
可分级的海克斯图标”。根因不是没有拿到数据：OP.GG 页面里的富数据使用数字 `rarity`，观察到的值为 `1/4/8`；
旧解析器把数字保留成字符串，UI 只筛选 `棱彩/黄金/白银`，于是完整 rows 被全部过滤。修复规则是显式映射
`1=白银`、`4=黄金`、`8=棱彩`，未知值仍保持 fail-closed；完整 rows 不能用 `Take(12)` 截断，分页只能发生在
presentation 层。FoundationSmoke 必须包含嵌套 `self.__next_f` 结构和数字等级 fixture。

## 2026-09-01：Champion summary 不是永远可靠的自动识别唯一来源

ChampSelect 中偶发出现“没有识别到这个英雄”时，不能把用户输入或 pick intent 当作修复。总目录可能在客户端切换、
缓存或版本边界期间缺少当前 ID，或只返回占位名称。保持同一只读 LCU gateway，在 summary 缺少/占位时按已观察的
champion ID 请求 typed champion detail，并对旧请求做取消和 generation 检查。详情仍缺失时应保留等待/手动查询兜底，
不得猜测英雄或展示通用榜单。

## 2026-09-01：旧候选进程不能代表修复版 UI 验收

候选入口切换到新版本目录后，已经运行的 `FACM.App.exe` 仍加载旧程序集。必须让用户正常关闭并重新启动根 launcher
后再验证新 parser/identity fallback；不能在不确认 loaded version 的情况下把旧截图当成修复失败，也不能强制终止用户进程。
本机 screenshot helper 还可能因 `SetIsBorderRequired` `E_NOINTERFACE` 失败；进程存在、窗口标题或源码门禁都不能替代
真实 post-fix UI review。

## 2026-09-02：GUI host 的 League 进程命令行读取不能只依赖 native/动态 COM fallback

真实问题中 `LeagueClient.exe`、`LeagueClientUx.exe`、Riot Client 与 LCU 端口都在运行，失败发生在 HTTP 之前：
WinUI self-contained host 的 `NtQueryInformationProcess` 返回访问/部分复制错误，动态 `WbemScripting.SWbemLocator`
也没有返回命令行，因此 discovery 记录 `command-line-unavailable`，界面显示 `NotRunning`。同机诊断 shell 的 WMI
成功不能代表 GUI host 的动态 COM 路径也成功；旁边的空 `LeagueClient\lockfile` 更不能当作 LCU 凭据。

防回归规则：

- 保留 native query，但在 App composition 中注入强类型 `System.Management` WMI reader；失败时继续使用原有动态
  COM fallback，并把全部异常转换为 fail-closed 的无命令行结果。
- 只记录脱敏的 source/outcome/PID/port；绝不记录命令行或 token。
- 回归必须在真实 GUI self-contained 候选中同时验证进程发现、LCU HTTP 200、`Connected` 状态和候选日志；仅凭
  shell WMI、监听端口、进程存在或历史 ready 状态不能判定修复。

## 2026-09-02：带状态点的托盘图标不能从任务栏大图直接缩放

16 像素托盘图标若沿用居中的完整品牌图，再把状态点贴在右上角，Windows 缩放和托盘裁切会让状态点缺角，
同时压缩双 G 的可读空间。防回归规则是：托盘使用独立的微型图形，主体向左下收；右上角保留约 30% 空间；
状态点向内至少一像素并带深色轮廓；16、20、24、32 像素逐层检查，而不是只检查源 PNG 或 256 像素 ICO。
