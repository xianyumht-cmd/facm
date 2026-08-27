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
- Gate 5：COMPLETE，#196 / PR #197，`main@2e42218685b7cecd49a16676c85bf093bcf1ce1b`。
- Gate 6：COMPLETE，#198 / PR #199，`main@2ca0f98b8c6a7241594ac4037d0c0a6f293593da`。
- Gate 7：IMPLEMENTATION VERIFIED，#200 / PR #201，implementation head `997083eeef5b7385ef473e24d349cea14a752606`；canonical 文档提交后需 latest-head CI 再确认再合入。
- Gate 8～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 legacy 持续作为 rollback baseline。
- single-file 分发使用 `Environment.ProcessPath` 作为 distribution EXE identity；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`，不得作为 settings/cache/log/runtime/PetHost/update/Updater replacement 根目录。
- UI 只能通过 ViewModel -> Core intent/state contract；具体 Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；所有 writer 继续使用最小 capability allowlist。
- production `online/version.json` / `release/request.json` Gate 13 前保持 FACM 3.5.15。

## Gate 1～Gate 3

Gate 1 建立并行 .NET 10 solution、Core/Infrastructure/Platform.Windows/App/Smoke、Performance Contract、legacy settings codec、UI Text foundation、architecture gate。Gate 1 final：Foundation #12 / Windows Build #1300 / UI Text #421 SUCCESS。

Gate 2 建立 Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write target policy；Gate 2 final：Foundation #27 / Windows Build #1307 / UI Text #428 SUCCESS。

Gate 3 迁入 .NET 10 runtime/transport：唯一 `WindowsLeagueTransportSessionSource`、shared `LeagueHttpGateway`、secret-safe session parser、strict update metadata source、distribution-based `RuntimePathLayout`、`FACM.WindowsSmoke`。Gate 3 final：Foundation #34 / Windows Build #1310 / UI Text #431 SUCCESS。

## Gate 4 — Settings 2.0：COMPLETE

- schema version = `2`；typed sections：Environment / Online / Appearance / Pets / League，完整覆盖 3.5.15 15-key INI。
- 新文件：distribution EXE 同目录 `settings.v2.json`；legacy `settings.ini` Gate 13 前只读保留。
- legacy -> v2 deterministic migration；malformed/invalid/future schema fail closed。
- atomic save：same-directory temp -> write -> flush -> flush-to-disk -> replace/move；失败保留旧文件。
- ViewModel 只依赖 `ISettings2Repository`。

Gate 4 final：Foundation #56 / Windows Build #1318 / UI Text #439 SUCCESS；merge `main@31f867f10f2019004695d5a696c1177a079cef20`。

## Gate 5 — Product State + Observability：COMPLETE

- `ProductStateStore` 聚合 Application / League / Environment / Services；snapshot 带 revision + UTC timestamp；相同状态不发无意义 event，subscriber 在 lock 外通知。
- League state vocabulary：`NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`。
- `DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。
- `DiagnosticRedactor` 对 token/password/passwd/cookie/authorization/secret/credential/auth 等敏感 key 和自由文本 assignment 做 `[redacted]`。
- `BoundedJsonLinesDiagnosticSink` 默认 4 MiB、`.1` rotation、并发串行写、落盘前二次 redaction。
- Product State / diagnostics 只观察与发布事实，不拥有 League runtime/writer。

Gate 5 final latest head `9883a70c...`：Foundation #73 / Windows Build #1323 / UI Text #444 SUCCESS；随后 squash merge 到 `main@2e42218685b7cecd49a16676c85bf093bcf1ce1b`。

## Gate 6 — WinUI 3 Design System + Shell：COMPLETE

- `FacmTokens.xaml` 使用 WinUI semantic theme resources，不在 FACM.App XAML 保存产品硬编码 hex palette；Light/Dark/High Contrast 由平台资源系统负责。
- `FacmControls.xaml` 统一 PageTitle / SectionTitle / CardTitle / Body / Muted / Card / StatusChip / PrimaryButton / NavigationItem styles。
- `MainWindow` 固定 one main Window / one AppTitleBar / one NavigationView / one Frame / exactly four product entries：`清理与修复 / LOL 工作台 / 个性化 / 更多设置`。
- Shell user-visible copy 通过 `IUiTextProvider + UiTextKeys`，`FileUiTextProvider` 支持稳定 `ui-text.ini` override/fallback。
- `scripts/check-facm4-shell.ps1` 守四入口、单 Shell tree、semantic tokens/shared styles、UI Text coverage、无 legacy Form host、FACM.App XAML 无硬编码产品色。

Gate 6 final latest head `5b9b34f2...`：Foundation #97 / Windows Build #1328 / UI Text #449 SUCCESS；随后 squash merge 到 `main@2ca0f98b8c6a7241594ac4037d0c0a6f293593da`。

## Gate 7 — Desktop Shell / F 悬浮入口 / Anchor Placement：IMPLEMENTATION VERIFIED

Tracking：Issue #200，branch `feat/facm-4-gate7-desktop-anchor`，PR #201。

### Core placement contract

`FACM.Core.Desktop` 新增纯几何 contract：

```text
DesktopPoint / DesktopSize / DesktopRect / DesktopWorkArea
DesktopAnchor: Auto / Left / Right / Top / Bottom / four corners
AnchorPlacementRequest / AnchorPlacementResult
AnchorPlacementService
IDesktopWorkAreaProvider
```

- Core 不引用 WinUI/WinForms/Win32。
- 支持主屏 fallback、preferred point、负坐标、左/右/上/下屏、最近 work-area、edge/corner anchor、margin/clamp、off-screen recovery。
- preferred position 使用 physical desktop pixels；不会把负坐标错误截成 0。

### Windows monitor / DPI facts

`WindowsDesktopWorkAreaProvider`：

- `EnumDisplayMonitors + GetMonitorInfo` 获取真实 Windows work-area physical pixel bounds；
- `GetDpiForMonitor` 获取 effective per-monitor DPI，缺失时安全 fallback 到 96 DPI；
- Platform.Windows 只负责 facts，placement 算法仍在 Core。

### F desktop surface / lifecycle

- 新增独立 `FloatingWindow`，64 DIP surface / 56 DIP F button，共用 Gate 6 semantic resources 与 shared button style。
- `FloatingWindow` 只依赖 `IDesktopWorkAreaProvider + IUiTextProvider + EnsureMainWindow callback`；不拥有 League、settings、HTTP、diagnostic runtime。
- `App.xaml.cs` 仍只创建一个 `WindowsLeagueTransportSessionSource` 和一个 shared `LeagueHttpGateway`。
- MainWindow 关闭后 F surface 保留；点击 F = create-or-activate MainWindow，保持 **Ensure Open / Activate**，不是 toggle。
- 关闭 F 才进入真正 shutdown/dispose。
- Settings 2.0 `Pets.BallX/BallY` 只作为 preferred top-left；`int.MinValue` 表示无偏好；无效/越界位置 deterministic recovery。

### Gate 7 validation

implementation head `997083eeef5b7385ef473e24d349cea14a752606`：

- `FACM 4.0 Foundation` #114：SUCCESS；architecture / Shell / desktop source gates / restore / build / FoundationSmoke / WindowsSmoke / WinUI single-file publish / output verify / artifact upload 全 SUCCESS。
- `FACM Windows Build` #1331：SUCCESS。
- `FACM UI Text Contract` #452：SUCCESS。
- artifact `facm4-x64` id `9640719680`，ZIP `88,237,787` bytes，digest `sha256:57b9c54ffdd149f38c7ebf7e1b41a3a9a7a3989ab4da22f6d23bfedc20fcaa81`。
- `Gate7Smoke` 覆盖左侧负坐标屏、上方屏、nearest monitor、四角/edge anchor、margin/clamp、off-screen recovery、空 work-area fail-fast。
- `FACM.WindowsSmoke` 在 hosted Windows runner 上真实枚举 work-area / primary / DPI facts，并验证 placement 保持在工作区内。
- `scripts/check-facm4-desktop.ps1` 禁止 FloatingWindow 创建第二 League runtime、HttpClient、settings/diagnostic file runtime、low-level keyboard hook/polling，并验证共享 Design System 与 Ensure Open/Activate source contract。

Hosted runner 证据只证明 Win32 adapter/API 与 deterministic placement 在 CI 环境可工作；**不替代** Gate 10/12 的真实双屏、负坐标、100～200% mixed-DPI 硬件矩阵。

## Gate 8 — NEXT：LOL 工作台状态驱动 UX

Gate 7 合入后从最新 main 新开 Issue/branch/PR。固定目标：

1. 迁移 legacy `LeagueGameflowMonitor` 的**唯一循环 owner 职责**到 4.0，不允许 Page/ViewModel 再建独立 polling loop。
2. 复用 Gate 3 的同一个 `ILeagueReadGateway / LeagueHttpGateway / WindowsLeagueTransportSessionSource`；不创建第二 LCU session/HttpClient runtime。
3. Core 建 deterministic gameflow phase -> `LeagueProductState` + `LeagueActivityLevel` mapping，至少覆盖 Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / disconnected/error。
4. 唯一 gameflow owner 将事实发布到 Gate 5 `ProductStateStore`，Performance Budget 同源更新；UI 只订阅 state。
5. LOL Workbench 面向用户固定三分区：`比赛 / 攻略 / 自动化`；旧 8 个 novice-facing view 收口为三层 IA，不直接照搬 legacy tab tree。
6. action 只通过 Core capability/intent 暴露，禁止 ViewModel/Page 传 raw LCU path；Bench 仍为用户显式手动动作，不做后台自动抢英雄。
7. deterministic smoke 覆盖 phase mapping、poll cadence、duplicate state suppression、Performance Budget、Workbench IA、单 owner source gate。
8. legacy Build/UI Text/4.0 Foundation latest-head 全绿；production release controls 不动。

## Gate 9 → Gate 13 固定顺序

- Gate 9：诊断中心与脱敏诊断包。
- Gate 10：DPI / 多屏 / Accessibility。
- Gate 11：Recovery / Feature Flags / 更新保障。
- Gate 12：全量兼容 / 性能 / 发布矩阵。
- Gate 13：legacy 退休与 FACM 4.0 cutover；真实 release blockers 未关闭时不得发布 4.0.0。

## 持续保护的不变量

- exactly one League discovery/auth/session owner；所有 writer 保持最小 capability；Bench 仍为手动动作。
- Mayhem/OP.GG 保留 fallback、timeout、body cancellation、cache、Performance Budget。
- Game Repair 保留 native Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer；不恢复 Fix-LCU runtime。
- Cleanup 保留 preview、explicit confirm、UAC、path allowlist、reparse guard、执行前重验证。
- Updater 保留 size limit、SHA-256、signature/package validation、validated receipt、独立 replacement、失败保旧版。
- Single Instance = Ensure Open；快捷键 = RegisterHotKey；PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## Gate 13 前仍需真实 Windows 证据

普通非管理员 UAC/取消、Defender/SmartScreen、Windows 10 1809/22H2 + Windows 11、100/125/150/175/200% DPI、双屏/负坐标/混合 DPI、keyboard/focus/high contrast/text scaling/basic screen reader、Updater interrupted replacement/rollback、3.5.15 -> 4.0 settings 真机升级。未关闭时不得声称 Gate 12/13 release-ready。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate 回复“继续”。生产 release 与 destructive Git 操作仍遵守 `AGENTS.md` 的 fresh safety check。
