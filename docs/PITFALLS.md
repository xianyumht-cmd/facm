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

PR #40 排查 PetHost 首次释放慢时，曾把“尽早准备 PetHost”误当成 FACM 全局启动目标，并据此让所有启动都预热 PetHost。这个推导忽略了产品配置语义：桌宠本来就是可选能力。用户后来又明确调整了产品方向——默认需要一个 FACM 自己的专业 Shell 作为可见入口，但这并不等于默认应该加载 VPet。

这类问题的本质是把“解决一个可选模块的体验问题”错误扩张成“修改整个产品的默认启动链”。

### 防回归规则

- 修改默认启动体验前，必须先审 `AppSettings` 默认值、`MainForm` 启动条件、功能启用开关和实际用户配置，不能从某个测试场景倒推全局默认行为。
- FACM Shell 与桌宠必须是两个概念：Shell 是主程序自己的轻量入口；桌宠是用户选择的桌面形态。
- `AnimalPetEnabled=false` 时可以显示 FACM Shell，但不得因此读取、解包、扫描或启动 PetHost。
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
