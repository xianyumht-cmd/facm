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

- Gate 0：COMPLETE，#185 / PR #186，`main@4eda40956a8f7394c1f588d441993e7eb9a4e3e3`。
- Gate 1：COMPLETE，#187 / PR #188，`main@22c6f55d5c84ff3b55720653dacbac6d49aa0934`。
- Gate 2：COMPLETE，#189 / PR #190，`main@34578e688cfc85d8934b7dd14dd423e31e098e38`。
- Gate 3：COMPLETE，#191 / PR #192，`main@138940ccb1f0c4f72bb3325b64a3b94638ee2891`。
- Gate 4：COMPLETE，#193 / PR #195，`main@31f867f10f2019004695d5a696c1177a079cef20`。
- Gate 5：IMPLEMENTATION VERIFIED，#196 / PR #197，implementation head `6684a7050efefbc4a4ab1e7b16d5e0c193f4d679`；canonical 文档提交后需 latest-head CI 再确认再合入。
- Gate 6～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，先 x64。
- 并行 solution：`FACM4.sln`；legacy `FACM.sln` 在 Gate 13 前持续作为 rollback baseline。
- single-file 是一个分发 EXE + 首启 self-extract；`Environment.ProcessPath` 是 distribution EXE，`AppContext.BaseDirectory` 可能位于 `%TEMP%/.net/...`。
- settings/cache/logs/runtime/PetHost/update package/Updater replacement target 不得依赖 self-extract BaseDirectory。
- UI 只能通过 ViewModel -> Core intent/state contract；具体 Infrastructure / Platform adapter 只在 composition root 组装。
- exactly one League discovery/auth/session owner；writer 必须使用窄 capability allowlist。
- 生产 `online/version.json` / `release/request.json` 在 Gate 13 前保持 FACM 3.5.15。

## Gate 1 — Parallel Foundation

已建立 `FACM.Core / FACM.Infrastructure / FACM.Platform.Windows / FACM.App / FACM.FoundationSmoke`、framework-neutral `FacmHost`、Performance Policy、legacy settings codec、UI Text adapter、WinUI 单 Window/NavigationView/Frame、architecture gate 与 parallel CI。

Gate 1 最终：Foundation #12 / Windows Build #1300 / UI Text #421 全 SUCCESS。

## Gate 2 — Core / UI Decoupling

- Cleanup：Core `CleanupPlan / CleanupResult / CleanupProgress / CleanupApplicationService`；未确认不能执行。
- League：Core session/read/write contracts；`LeagueWriteCapability -> exact method/path`，调用方不能传任意 LCU URL/path。
- Online：Core manifest/decision/install intent。
- Settings：legacy `ISettingsRepository + IniSettingsRepository` 保持 3.5.15 15 键兼容。
- ViewModel 禁止直接碰 Infrastructure/Platform/HttpClient/File/Process/Registry/具体 League session/URL。

Gate 2 最终：Foundation #27 / Windows Build #1307 / UI Text #428 全 SUCCESS。

## Gate 3 — .NET 10 Runtime / Transport

- `LeagueTransportSession` 保存 transport secret；公共 descriptor/诊断不含 password/token。
- `LeagueTransportSessionParser` 支持 lockfile/command-line；`WindowsLeagueTransportSessionSource` 是 4.0 唯一 League discovery/auth/session owner。
- `LeagueHttpGateway` read/write 共用同一 session source；write target 必须来自 `LeagueWriteTargetPolicy`；LCU credential 只允许 loopback。
- `HttpUpdateManifestSource`：默认 7s timeout、linked cancellation、128 KiB metadata cap、严格 GitHub Release URL/version/SHA-256 validation。
- `RuntimePathLayout` 从 distribution executable 推导持久目录。
- `FACM.WindowsSmoke` 加入 4.0 Foundation workflow。

Gate 3 merge 前 latest head：Foundation #34 / Windows Build #1310 / UI Text #431 全 SUCCESS。

## Gate 4 — Settings 2.0：COMPLETE

Settings 2.0 schema version 固定为 `2`，Core typed sections：

```text
Environment -> GamePath
Online      -> AutoUpdateEnabled / LastAnnouncementId
Appearance  -> ThemeId
Pets        -> BallX / BallY / StyleId / Enabled
League      -> AutoApplyRecommended / hotkeys / honor / return-lobby / matchmaking / auto-accept
```

- distribution EXE 同目录新文件 `settings.v2.json`；legacy `settings.ini` Gate 13 前保留，只读迁移、不删除、不覆盖。
- legacy 15 键无损映射；默认主题 `glass-blue`、默认宠物 `greenfly`、自动化默认关闭。
- malformed / invalid / future schema fail closed，禁止静默重置覆盖用户配置。
- `PhysicalSettings2FileStore`：same-directory temp -> flush -> flush-to-disk -> replace/move，写失败保持旧文件。
- ViewModel 只依赖 Core `ISettings2Repository`。

Gate 4 final latest head：Foundation #56 / Windows Build #1318 / UI Text #439 全 SUCCESS；随后 squash merge 到 `main@31f867f10f2019004695d5a696c1177a079cef20`。

## Gate 5 — Product State + Observability：IMPLEMENTATION VERIFIED

Tracking：Issue #196，branch `feat/facm-4-gate5-state-observability`，PR #197。

### Product State

Core `ProductStateStore` 是统一 state store，快照覆盖：

- Application：`Starting / Ready / Degraded / ShuttingDown`；
- League：`NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`；
- Environment：distribution directory / elevation / network facts；
- Services：UpdateMetadata / LeagueTransport / PetHost health。

快照带 `Revision + TimestampUtc`；相同状态不产生无意义 revision/event；subscriber 在 state lock 外通知。它只发布事实，不拥有第二套 League runtime、轮询器或 writer。

### Observability

Core `DiagnosticEvent` 固定结构字段：`TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。

`DiagnosticRedactor` 在写入前处理敏感 key 与自由文本 assignment，覆盖 token/password/passwd/cookie/authorization/secret/credential/auth 等；敏感内容替换为 `[redacted]`。Diagnostics contract 没有 League/网络/业务写权限。

Infrastructure `BoundedJsonLinesDiagnosticSink`：

- 默认 4 MiB current file；
- 超限 rotate 到 `.1`；
- `SemaphoreSlim` 串行化并发写；
- JSONL 落盘前再次 redaction；
- 单条事件超过容量时 fail closed。

App composition root 创建唯一 `ProductStateStore` 与 diagnostic sink；`ControlCenterViewModel` 只消费 Core `IProductStateReader`，不直接读取日志文件或创建 runtime。

### Gate 5 verified implementation evidence

implementation head `6684a7050efefbc4a4ab1e7b16d5e0c193f4d679`：

- `FACM 4.0 Foundation` #67：SUCCESS；architecture / restore / build / FoundationSmoke / WindowsSmoke / WinUI single-file publish 全 SUCCESS。
- `FACM Windows Build` #1320：SUCCESS。
- `FACM UI Text Contract` #441：SUCCESS。
- artifact `facm4-x64` id `9639131618`，ZIP `88,219,299` bytes，digest `sha256:031482a2744c8221999b79a3784c12eb3a2db0b578fb65963fe3824ca8e115d0`。
- smoke 覆盖 state transition、duplicate suppression、subscriber lock boundary、parallel revisions/snapshots、required diagnostic fields、key/text redaction、并发 physical JSONL write 与 bounded rotation。

## Gate 6 — NEXT：WinUI 3 Design System + Shell

Gate 5 合入后从最新 main 新开独立 Issue/branch/PR。固定目标：

1. 扩展 `Themes/FacmTokens.xaml` 为 semantic design tokens，Light/Dark/High Contrast 不依赖页面硬编码色值。
2. 建统一 Card / Button / Status / Chip / Section / Navigation visual contract。
3. Shell 继续保持 one Window / one TitleBar owner / one NavigationView / one Frame，不引入 Form-in-Form/Z-order/timer/reflection patching。
4. 控制中心固定四入口：`清理与修复 / LOL 工作台 / 个性化 / 更多设置`；LOL 用户分区仍为 `比赛 / 攻略 / 自动化`。
5. Page/ViewModel 只消费 Core state/intents；Shell 不 new League runtime、HttpClient、settings file、diagnostic file。
6. user-visible text 优先走 UI Text contract；新增 source lint/smoke 防止设计系统回退成散落硬编码。
7. source/deterministic smoke 验 one-shell tree、semantic token presence、四入口、无 legacy Form host。
8. legacy Build/UI Text/4.0 Foundation latest-head 全绿；生产 release controls 不动。

## Gate 7 → Gate 13 固定顺序

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
