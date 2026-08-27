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
- Gate 4：DELIVERY，#193 / PR #195；Settings 2.0 implementation 已通过完整工程验证。
- Gate 5～13：继续按固定顺序推进，不要求用户逐 Gate 回复“继续”。

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
- `LeagueHttpGateway` 的 read/write 共用同一个 session source；write target 必须来自 `LeagueWriteTargetPolicy`；LCU credential 只允许 loopback。
- `HttpUpdateManifestSource`：默认 7s timeout、linked cancellation、128 KiB metadata cap、严格 GitHub Release URL/version/SHA-256 validation。
- `RuntimePathLayout` 从 distribution executable 推导持久目录。
- `FACM.WindowsSmoke` 加入 4.0 Foundation workflow。

Gate 3 merge 前 latest head：Foundation #34 / Windows Build #1310 / UI Text #431 全 SUCCESS。

## Gate 4 — Settings 2.0

Tracking：Issue #193，branch `feat/facm-4-gate4-settings2`，PR #195。

### Schema / ownership

Settings 2.0 当前 schema version 固定为 `2`，Core typed sections：

```text
Environment -> GamePath
Online      -> AutoUpdateEnabled / LastAnnouncementId
Appearance  -> ThemeId
Pets        -> BallX / BallY / StyleId / Enabled
League      -> AutoApplyRecommended / hotkeys / honor / return-lobby / matchmaking / auto-accept
```

这些 section 合计无损覆盖 3.5.15 的 15 个稳定 INI key。默认主题仍为 `glass-blue`，默认宠物仍为 `greenfly`，自动化默认仍为关闭。

### Migration / rollback

- 新文件：distribution EXE 同目录 `settings.v2.json`。
- legacy 文件：同目录 `settings.ini`，Gate 13 前保留；Settings 2.0 迁移只读它，不删除、不覆盖。
- 无 v2 且有 legacy：`LegacySettingsCodec -> Settings2Migration -> validated v2 -> atomic save`。
- 无 v2 且无 legacy：建立 validated defaults。
- 已有 v2 JSON 损坏、section 缺失、值非法或 schema 不是当前版本：fail closed；禁止静默回退默认值并覆盖用户文件。

### Atomic persistence

`PhysicalSettings2FileStore` 使用同目录临时文件：write -> flush -> flush-to-disk -> replace/move；失败时 best-effort 清理 temp，旧目标文件保持可用。保存前必须通过 `Settings2Validator`。

### UI boundary

`ControlCenterViewModel` 已切到 Core `ISettings2Repository`；`App.xaml.cs` composition root 从 `RuntimePathLayout.Settings2Path + SettingsPath` 注入 `Settings2Repository`。ViewModel/Page 不知道 JSON/INI/File 路径。

### Gate 4 verified implementation evidence

implementation head `183668370e8d84bfa6bd87953b8316e84846585c`：

- `FACM 4.0 Foundation` #48：SUCCESS；architecture / restore / build / FoundationSmoke / WindowsSmoke / WinUI single-file publish 全 SUCCESS。
- `FACM Windows Build` #1314：SUCCESS。
- `FACM UI Text Contract` #435：SUCCESS。
- artifact id `9638082999`，ZIP `88,214,200` bytes，digest `sha256:fe4ec3913ae25487cb34ca334013f8c754e3e4cf079ad30d583cb83ed46fd8a5`。
- smoke 覆盖：15-key migration、legacy preservation、v2 round-trip、corrupt JSON rejection、future schema rejection、invalid-before-write、simulated atomic failure preservation、physical Windows atomic save/temp cleanup。
- workflow artifact 名从 Gate 专用名收束为稳定 `facm4-x64`，后续 Gate 不再为 artifact 名重复改 CI。

## Gate 5 — NEXT：Product State + Observability

Gate 4 合入后从最新 main 新开独立 Issue/branch/PR。固定目标：

1. Core 建统一 Product State：Application / League / Environment / Services。
2. League state 至少覆盖 `NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`，页面订阅 state，不自己重复轮询。
3. 建 framework-neutral state store/snapshot/change notification；具体 League/Windows adapter 只发布事实，不复制 runtime owner。
4. 结构化诊断事件至少包含 `ActionId / Module / Duration / Result / Reason / LeagueState / ClientVersion / Timestamp`。
5. observability 不记录 token/password/cookie，不让 diagnostics 获得新的业务写权限。
6. Infrastructure 提供 bounded structured sink/persistence；Gate 9 诊断中心只消费该 contract。
7. deterministic smoke 覆盖 state transition、subscriber、concurrency/snapshot、structured fields、secret redaction。
8. legacy Build/UI Text/4.0 Foundation 继续 green；生产 release controls 不动。

## Gate 6 → Gate 13 固定顺序

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
