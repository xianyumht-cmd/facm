# FACM 常见陷阱与防回归规则

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
