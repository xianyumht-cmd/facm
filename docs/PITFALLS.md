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
- 兼容层暂时无法移入原生构造器时，应在 `WM_SHOWWINDOW` 等首次正常 paint 之前完成布局，并在最终布局后 `PerformLayout + Invalidate(true)`。
- 自绘控件的 hover repaint 不能承担“修复布局”的职责；如果只有 hover 后画面才正常，应按首帧布局/无效区域错误处理，而不是增加更多 hover 刷新。
- 后续重构 `CompactMenuForm` 时应最终把 5 个正式入口收回原生布局，并删除兼容反射层。

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
