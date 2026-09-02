# FACM 4.0 P6 — 更多设置与维护能力等价契约

> 行为基线：production `release/3.5.15-20260827`。P6 只迁 3.5.15 真实存在的维护行为，并保留 4.0 已有 Diagnostics/Recovery 增强；不创造 3.5 没有的“开机启动”用户开关。

## 1. Settings 2.0

3.5.15 `AppSettings` 固定 15 个键；本阶段相关键只有：

- `AutoUpdateEnabled`，默认 `true`；
- `LastAnnouncementId`。

4.0 已对应到 `Settings2Document.Online.AutoUpdateEnabled / LastAnnouncementId`，不得新增与 3.5 语义重复的持久化字段。

恢复边界：从 LKG / recovery defaults 读取时，不因启动或页面加载自动覆盖损坏 primary；只有用户显式修改设置时才保存。

## 2. 更新中心

### 2.1 自动检查

- UI 文案保持“启动时自动检查更新”；
- 开关变化立即持久化；
- 开启时，正常启动允许后台检查；
- 关闭时，不自动联网检查版本；
- **手动“立即检查”不受该开关限制**。

### 2.2 更新决策

页面展示：

- 当前版本；
- 最新版本；
- `up-to-date / update-available / manifest-unavailable / force-update-required` 等用户可理解状态；
- release notes；
- 更新检查失败必须可恢复，不影响 FACM 其它功能。

4.0 现有 `HttpUpdateManifestSource` 已冻结安全条件：

- manifest 仅 HTTPS；
- 下载地址必须为 `github.com/xianyumht-cmd/facm/releases/download/v<version>/...`；
- SHA-256 必须为 64 位十六进制；
- metadata 最大 128 KiB；
- 默认 metadata timeout 7 秒。

P6 不得放宽这些条件。

### 2.3 公告

3.5.15 更新中心同时展示公告标题、正文和“查看详情”。4.0 需要独立只读公告契约；详情链接只能为绝对 HTTPS。公告失败是 best-effort，不能拖死版本检查。

`LastAnnouncementId` 只用于记忆已展示公告，不允许公告响应覆盖其它 Settings2 section。

### 2.4 下载 / 安装

用户点击“立即更新”后：

1. 明确确认；
2. 下载到 `RuntimePathLayout.UpdatesDirectory`；
3. 512 MiB 最大更新包；
4. header/connect 超时与连续无数据超时必须有界；
5. 下载完成重新计算 SHA-256；
6. 只允许与本次 validated receipt 绑定的包进入 replacement；
7. replacement 前再次校验文件 hash / 发布身份；
8. replacement helper 启动成功后，原 FACM 才退出。

WinUI 不直接下载文件、写更新目录、`Process.Start` 或 `runas`；这些全部下沉到 Infrastructure / Platform.Windows。

### 2.5 Force Update

如果当前版本低于 minimum version 或 manifest 明确要求 force update：

- UI 必须清楚显示“需要更新后才能继续使用”；
- 用户只能继续更新或退出；
- 不做静默自动安装；
- 取消下载可以回到 force-update 状态，但不能假装应用已正常解锁；
- CI 只用 fake installer/decision 测试，不启动真实替换器。

## 3. 打开日志

3.5.15 “打开日志”行为：确保日志文件存在，再通过 Windows Shell 打开当前日志。

4.0：

- 当前日志是 `runtime/logs/facm4-events.jsonl`；
- WinUI 只调用 `ILogFileOpener` 类窄平台契约；
- 文件准备 / Shell 打开由 Windows adapter 完成；
- 打开失败只影响该操作；
- Diagnostics Refresh / Copy / Export 继续保留，作为 4.0 增强能力，不替代“打开日志”。

## 4. 单实例 / 二次启动唤回

3.5.15 基线：

- 普通实例互斥名：`Local\\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D`；
- 激活事件：`Local\\FACM-Activate-2C429A53-6710-48BC-A57C-32BEA688B25D`；
- 第二次正常启动在已有主实例时，最多约 1600 ms 有界重试 signal；
- signal 成功：第二实例正常退出，主实例打开 / 唤回控制中心；
- signal 失败：第二实例不得接管或杀死主实例；
- `--cleanup` 使用独立 elevated 边界。

4.0 迁移规则：

- Windows named mutex/event 只存在于 Platform.Windows；
- 不通过进程枚举、窗口标题、端口轮询判定主实例；
- listener 使用 AutoReset 语义，每个 signal 最多触发一次 activation callback；
- App activation callback 只负责打开现有 MainWindow/compact entry，不新建第二 runtime owner；
- cleanup elevated 启动仍可独立存在，不能被正常 instance gate 阻断。

## 5. WinUI “更多设置”页面

P6 在现有 DiagnosticsPanel 上方补维护卡片，不重做 UI 2.0：

- 自动检查更新 Toggle；
- 当前 / 最新版本和状态；
- “立即检查”；
- “立即更新”（仅有可用更新时）；
- 下载进度 / 取消；
- 公告标题 / 正文 / HTTPS 详情；
- “打开日志”；
- 现有 Diagnostics Refresh / Copy / Export 保持原样。

页面不得直接拥有 `HttpClient`、`File/Directory`、`Process.Start`、registry 或任意 URL 安装能力。

## 6. 验证矩阵

### Deterministic smoke

- AutoUpdate 默认 true、显式 toggle 才保存；
- recovery-origin load 不自动写 primary；
- manual check 在 auto-update=false 时仍读取 manifest；
- announcement HTTPS validation；
- force update decision；
- update download size/hash/receipt rules；
- cancellation / progress；
- single-instance activation exactly-once。

### Windows smoke

- named mutex / event 正常 acquire、signal、dispose；
- signal listener 未创建时有界失败；
- cleanup mutex 与 normal mutex 独立；
- 日志 opener 只接收受控日志路径，准备文件与 Shell launch 分离成可测试边界。

### Source gate

- App/ViewModel 无 `Process.Start`、网络下载、任意 URL 安装、直接 File/Directory 维护逻辑；
- update source/installer 保留 HTTPS + GitHub release + SHA-256 + size cap；
- single-instance 只在 Platform.Windows；
- 不新增 `StartupEnabled` / Run registry / Startup folder 用户设置。

## 7. 非目标

- 不增加 3.5.15 不存在的开机启动开关；
- 不在 P6 做 UI 2.0；
- 不重构 P5 League runtime；
- 不 merge P5/P6；
- 不修改 production 3.5.15；
- 不发布 4.0.0 / 不执行 Gate 13 cutover；
- 不提供中间候选包。
