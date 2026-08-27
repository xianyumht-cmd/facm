# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（生产冻结线）

- 版本：FACM 3.5.15
- GitHub Release：`v3.5.15`
- 在线更新：已启用
- `minimum_version`：3.0.0
- `force_update`：false
- Release FACM.exe SHA-256：`E3B415375E204212EE2D7A36D4A038708DC75694CD9B6FD28F2761BBF1FD01CE`
- `published_at`：2026-08-27T05:28:50.9137418+00:00
<!-- FACM_RELEASE_STATE_END -->

> 生产事实以 GitHub Release 与 `online/version.json` 为准。FACM 4.0 在 Gate 13 release 前不得修改生产更新指向。

## FACM 4.0 总进度

- Gate 0：COMPLETE，#185 / PR #186，合入 `main@4eda40956a8f7394c1f588d441993e7eb9a4e3e3`。
- Gate 1：COMPLETE，#187 / PR #188，合入 `main@22c6f55d5c84ff3b55720653dacbac6d49aa0934`。
- Gate 2：COMPLETE，#189 / PR #190，合入 `main@34578e688cfc85d8934b7dd14dd423e31e098e38`。
- Gate 3：IMPLEMENTATION VERIFIED，#191 / PR #192，implementation head `78d2b00aec71f69796e3bdc7f6bab8c174974b46`；canonical 文档提交后需 latest-head CI 再确认再合入。
- Gate 4～13：按顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，先 x64。
- 并行 solution：`FACM4.sln`；legacy `FACM.sln` 在 Gate 13 前持续可构建。
- single-file 是一个分发 EXE + 首启 self-extract，不是零解包原生单文件。
- `Environment.ProcessPath` 是 distribution EXE；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`。
- settings/cache/logs/runtime/PetHost/update package/Updater replacement target 不得依赖 self-extract BaseDirectory。
- UI 只能通过 ViewModel -> Core intent/state contract；具体 Infrastructure / Platform adapter 只在 composition root 组装。
- exactly one League discovery/auth/session owner；writer 必须窄 capability allowlist。

## Gate 1 — Parallel Foundation

已建立：`FACM.Core / FACM.Infrastructure / FACM.Platform.Windows / FACM.App / FACM.FoundationSmoke`、framework-neutral `FacmHost`、Performance Policy、15 键 `settings.ini` codec、UI Text adapter、WinUI 单 Window/NavigationView/Frame、architecture gate 与 parallel CI。

Gate 1 最终：Foundation #12 / Windows Build #1300 / UI Text #421 全 SUCCESS。

## Gate 2 — Core / UI Decoupling：COMPLETE

- Cleanup：Core `CleanupPlan / CleanupResult / CleanupProgress / CleanupApplicationService`；未确认不能执行。
- League：Core session/read/write contracts；`LeagueWriteCapability -> exact method/path`，调用方不能传任意 LCU URL/path。
- Online：Core manifest/decision/install intent。
- Settings：`ISettingsRepository + IniSettingsRepository` 保持 3.5.15 15 键 INI compatibility。
- WinUI：`ControlCenterViewModel` 只依赖 Core contracts；architecture gate 禁止 ViewModel 直接碰 Infrastructure/Platform/HttpClient/File/Process/Registry/具体 League session/URL。
- settings path 使用 distribution EXE 同目录，禁止 `AppContext.BaseDirectory`。

Gate 2 最终：Foundation #27 / Windows Build #1307 / UI Text #428 全 SUCCESS，随后 squash merge 到 `main@34578e688cfc85d8934b7dd14dd423e31e098e38`。

## Gate 3 — .NET 10 Runtime / Transport：IMPLEMENTATION VERIFIED

Tracking：

- Issue #191：`FACM 4.0 Gate 3：.NET 10 Runtime / Transport 迁移`
- branch：`feat/facm-4-gate3-runtime`
- PR #192
- verified implementation head：`78d2b00aec71f69796e3bdc7f6bab8c174974b46`

### League runtime

- Core 新增 secret-bearing `LeagueTransportSession`，公共 `LeagueSessionDescriptor` 不含 password/token；`ToString()` 不打印 secret。
- `LeagueTransportSessionParser` 迁入 lockfile / command-line parser，保持 3.5.15 字段与参数兼容。
- `WindowsLeagueTransportSessionSource` 是 4.0 唯一真实 discovery/auth/session owner；默认 750ms rediscovery budget。
- `LeagueHttpGateway` 同时实现 read/write gateway，二者共用同一个 session source；401/403/timeout/HTTP failure 会 invalidate 当前匹配 session。
- LCU credential 只允许发送给 loopback HTTP(S)。read 禁止 absolute URL；write target 只能由 `LeagueWriteTargetPolicy.Resolve()` 产生。
- App composition root 只创建一个 `WindowsLeagueTransportSessionSource` 和一个共享 `LeagueHttpGateway`；Page/ViewModel 不持有 transport/session secret。

### Online / Update metadata

- `HttpUpdateManifestSource` 使用 .NET 10 HTTP transport；默认 7s timeout、linked cancellation、128 KiB metadata 上限、no-cache/no-store。
- manifest validation 保持 legacy 3.5.15 语义：version/minimum version 可解析；download URL 必须 HTTPS `github.com` 且路径匹配 `/xianyumht-cmd/facm/releases/download/v{version}/...`；SHA-256 必须 64 hex。
- `UpdateDecisionService` 修正为 legacy 语义：存在新版本时，`force_update=true` **或** 当前版本低于 `minimum_version` 任一条件都要求强制更新。
- Gate 3 只迁 metadata transport；mirror/download/hash receipt/replacement/rollback 继续由后续 Updater gate 保留和验证。

### Runtime paths

`RuntimePathLayout` 只从 `IExecutablePathProvider.ExecutablePath` 推导：distribution dir、`settings.ini`、`ui-text.ini`、logs、runtime、cache、pethost、updates。`BaseDirectory` 只允许作为诊断信息，不参与持久目录计算。

### Gate 3 deterministic evidence

新增 `FACM.WindowsSmoke`，并扩展 FoundationSmoke。verified implementation head `78d2b00a...`：

- `FACM 4.0 Foundation` #32：SUCCESS；其中 architecture / restore / build / FoundationSmoke / WindowsSmoke / WinUI single-file publish 全 SUCCESS。
- `FACM Windows Build` #1309：SUCCESS。
- `FACM UI Text Contract` #430：SUCCESS。
- artifact：`facm4-gate3-x64` id `9637340189`，ZIP `88,204,480` bytes，digest `sha256:84b38f9d2af6b97d659d5987f497b9cbde25dd845e2166919110169c39050786`。

## Gate 4 — NEXT：Settings 2.0

Gate 3 合入后从最新 main 新开 #Issue/branch/PR。目标：

1. 建立 versioned typed settings document，schema version 明确。
2. 3.5.15 `settings.ini` 15 键首次无损导入；合法 theme/pet/hotkey/automation 不静默重置。
3. 新配置使用 validated model + atomic save（temp -> flush/replace/move），写失败保留最后可用版本。
4. migration 必须幂等；旧 INI 在 Gate 13 前保留为 rollback/migration evidence，不因迁移成功立刻删除。
5. Settings module/owner 明确；Page/ViewModel 只读写 typed settings intent，不直接操作 JSON/INI/File。
6. malformed/unknown future schema 不静默覆盖；走 fallback/diagnostic/recovery。
7. deterministic fixtures：默认值、3.5.15 import、round-trip、invalid values、atomic failure/recovery、重复 migration。
8. production release controls 不动；legacy Build/UI Text 继续 green。

## Gate 5 → Gate 13 固定顺序

- Gate 5：Product State + Observability。
- Gate 6：WinUI 3 Design System + Shell。
- Gate 7：Desktop Shell / F 悬浮球 / Theme / Anchor Placement。
- Gate 8：LOL 工作台状态驱动 UX。
- Gate 9：诊断中心与脱敏诊断包。
- Gate 10：DPI / 多屏 / Accessibility。
- Gate 11：Recovery / Feature Flags / 更新保障。
- Gate 12：全量兼容 / 性能 / 发布矩阵。
- Gate 13：legacy 退休与 FACM 4.0 cutover；只有真实 release blockers 全关闭后才允许发布 4.0.0。

## 持续保护的不变量

- exactly one League discovery/auth/session owner；所有 writer 保持最小 capability；Bench 仍为手动动作。
- Mayhem/OP.GG 保留 fallback、timeout、body cancellation、cache、Performance Budget。
- Game Repair 保留 native Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer；不恢复 Fix-LCU runtime。
- Cleanup 保留 preview、explicit confirm、UAC、path allowlist、reparse guard、执行前重验证。
- Updater 保留 size limit、SHA-256、signature/package validation、validated receipt、独立 replacement、失败保旧版。
- Single Instance = Ensure Open；快捷键 = RegisterHotKey；PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## Gate 13 前仍需真实 Windows 证据

普通非管理员 UAC/取消、Defender/SmartScreen、Windows 10 1809/22H2 + Windows 11、100～200% DPI、双屏/负坐标/混合 DPI、keyboard/focus/high contrast/text scaling/screen reader、Updater interrupted replacement/rollback、3.5.15 -> 4.0 settings 真机升级。未关闭时不得声称 release-ready。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate 回复“继续”。生产 release 与 destructive Git 操作仍遵守 `AGENTS.md` 的 fresh safety check。
