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

`--mayhem-source-test` 会真实访问 OP.GG、ARAMMayhem.com、Riot Data Dragon / CommunityDragon。它很适合发现第三方页面结构、WAF 或资源路径变化，但第三方 429/5xx/网络抖动与 FACM 自身是否能正确编译、打包没有必然关系。

把这种 live probe 放在 `FACM.csproj` 的 `AfterTargets=Build` 中，会让桌宠、清理器等完全无关的提交因为外部网站临时故障而变红。

### 防回归规则

- `ValidateRuntimeSourcesAfterCiBuild` 只放 deterministic、本地可重复 smoke；禁止把实时公网探测重新塞回核心 MSBuild target。
- 真实海斗数据源健康检查统一放在 `.github/workflows/mayhem-source-probe.yml`。
- PR 上的 live probe 是 advisory；第三方失败不能代替核心 `FACM Windows Build` 的代码回归结论。
- main/定时 live probe 失败需要保留 stdout/stderr/FACM 日志 artifact，先区分外部服务故障与解析器真实回归。
- 修复第三方解析器时仍应尽量增加 deterministic fixture/smoke，不能只依赖“今天网站恰好能访问”。

### 关联

- Issue #19
