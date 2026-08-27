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
- Gate 7：COMPLETE，#200 / PR #201，squash merge `main@95f2ce66f2d5e058c0bc2052d619ce9473cf1aa9`。当前 Gate 8 基线 `main@812a5bdb9136eee7392e4feb3afb5a07f81aa7cb` 与该 Gate 7 merge tree 文件差异为 0。
- Gate 8：IMPLEMENTATION VERIFIED，#202 / PR #203，implementation head `7d108193d2b19d3cc3224f92177edb3b01522b40`；canonical docs 提交后需 latest-head CI 再确认再合入。
- Gate 9～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 legacy 持续作为 rollback baseline。
- single-file 分发以 `Environment.ProcessPath` 作为 distribution EXE identity；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`，不得作为 settings/cache/log/runtime/PetHost/update replacement 根目录。
- UI 通过 ViewModel -> Core intent/state contract；Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；所有 writer 继续使用最小 capability allowlist。
- production `online/version.json` / `release/request.json` Gate 13 前保持 FACM 3.5.15。

## Gate 1～Gate 6 摘要

Gate 1：并行 .NET 10 solution、Core/Infrastructure/Platform.Windows/App/Smoke、Performance Contract、legacy settings codec、UI Text foundation、architecture gate。

Gate 2：Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write-target policy。

Gate 3：唯一 `WindowsLeagueTransportSessionSource`、shared `LeagueHttpGateway`、secret-safe session parser、strict update metadata、distribution-based `RuntimePathLayout`、`FACM.WindowsSmoke`。

Gate 4：Settings 2.0 schema v2，Environment / Online / Appearance / Pets / League typed sections；legacy INI deterministic migration；same-directory atomic save；legacy `settings.ini` Gate 13 前只读保留。

Gate 5：`ProductStateStore` + structured observability；同状态不增加 revision；subscriber lock 外通知；`DiagnosticRedactor` + bounded JSONL sink 双层脱敏。

Gate 6：WinUI semantic Design System + one Main Shell：one AppTitleBar / one NavigationView / one Frame / exactly four product entries `清理与修复 / LOL 工作台 / 个性化 / 更多设置`；用户 copy 全部走 UI Text contract。

## Gate 7 — Desktop Shell / F 悬浮入口：COMPLETE

- Core `AnchorPlacementService`：纯几何、负坐标、主屏 fallback、nearest work-area、edge/corner anchor、margin/clamp、off-screen recovery。
- `WindowsDesktopWorkAreaProvider`：EnumDisplayMonitors/GetMonitorInfo + per-monitor DPI fact；Core 不依赖 Win32。
- 独立 `FloatingWindow` 共享 Gate 6 semantic resources，只做 F desktop entry / Ensure MainWindow；不拥有 League/settings/HTTP/diagnostic runtime。
- MainWindow 关闭后 F 保留；F 点击 = create-or-activate，Single Instance 语义仍是 Ensure Open / Activate，不是 toggle。
- Settings 2.0 Pets BallX/BallY 仅作为 preferred top-left；invalid/off-screen deterministic recovery。
- Gate 7 final latest-head：Foundation #120 / Windows Build #1334 / UI Text #455 SUCCESS；squash merge `main@95f2ce66f2d5e058c0bc2052d619ce9473cf1aa9`。
- hosted runner work-area/DPI 只算工程证据，不替代 Gate 10/12 mixed-DPI 真机矩阵。

## Gate 8 — LOL 工作台状态驱动 UX：IMPLEMENTATION VERIFIED

Tracking：Issue #202，branch `feat/facm-4-gate8-league-workbench`，PR #203。

### Gameflow / ownership

- Core `LeagueGameflowPhaseMapper` 一次映射 LCU phase -> `LeagueProductState + LeagueActivityLevel`。
- Product State 覆盖：NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError。
- legacy phase 基线保持：Matchmaking、ReadyCheck、ChampSelect、InProgress、WatchInProgress、Reconnect、GameStart、WaitingForStats、PreEndOfGame、EndOfGame。
- `LeagueGameflowCadence`：ChampSelect 2s；Matchmaking/ReadyCheck 3s；InGame 10s；connected other 5s；disconnected/connecting/error 10s。
- `LeagueGameflowMonitor` 是 4.0 **唯一 gameflow polling owner**；只依赖 shared `ILeagueReadGateway + ILeagueSessionAccessor`，不创建第二 HttpClient/session source。
- 同一个 mapping 同源更新 `ProductStateStore` 和 `PerformanceBudgetProvider`；UI 不维护第二份 phase/poll cache。

### Workbench UX

- 用户 IA exactly 3：`比赛 / 攻略 / 自动化`，不照搬 legacy 8-tab tree。
- `LeagueWorkbenchViewModel` 只订阅 `IProductStateReader + PerformanceBudgetProvider`；不知道 raw `/lol-*` path、HTTP、session discovery、polling 或 writer。
- `MainWindow` 仅在 LOL 入口显示三分区 panel；后台 state 变化通过 WinUI DispatcherQueue 回 UI thread。
- 主 Shell 关闭/重开只重建轻量 ViewModel；唯一 League session/gameflow owner 持续由 App composition 管理。
- Gate 8 不增加后台抢英雄权限；Bench 仍为用户显式手动动作；writer 仍走 Core capability contract。

### Gate 8 deterministic evidence

implementation head `7d108193d2b19d3cc3224f92177edb3b01522b40`：

- `FACM 4.0 Foundation` #139：SUCCESS；architecture / Shell / desktop / League Workbench source gates、restore/build、FoundationSmoke、WindowsSmoke、single-file publish、output verify、artifact upload 全 SUCCESS。
- `FACM Windows Build` #1338：SUCCESS。
- `FACM UI Text Contract` #459：SUCCESS。
- artifact `facm4-x64` id `9642227564`，ZIP `88,253,721` bytes，digest `sha256:4ab3219658deb6263484dc772563c4835dbd08ddb55b2cea9db8df3d0c04965a`。
- `Gate8Smoke` 覆盖完整 phase mapping、legacy cadence、同状态 monitor event suppression、Product State revision suppression、Performance Budget 同源更新、Workbench exactly-three IA 与 UI Text defaults。
- `scripts/check-facm4-league-workbench.ps1` 自动守 exactly-one session/gameflow/performance composition，并拒绝 Workbench/ViewModel raw LCU polling、HttpClient、第二 runtime/monitor 或 writer 越层。

## Gate 9 — NEXT：Diagnostics Center / 脱敏诊断包

Gate 8 合入后从最新 main 新开 Issue/branch/PR。固定目标：

1. 复用 Gate 5 `DiagnosticEvent / DiagnosticRedactor / BoundedJsonLinesDiagnosticSink`，不建立第二日志系统。
2. Core 建 diagnostics snapshot/summary/export contracts：当前 Product State、版本、runtime facts、近期结构化事件摘要；不包含 credential/raw lockfile secret。
3. Infrastructure 建 bounded reader/exporter：只读允许的 FACM 诊断文件，逐行/逐字段再次 redaction，限制总文件数/单文件/总包大小。
4. 导出 ZIP 至 distribution 下稳定 diagnostics/export 或用户显式目标；文件名无用户名/机器名等隐私字段。
5. WinUI `更多设置` 下增加 Diagnostics Center：可复制 summary、导出脱敏 bundle；UI 只调用 Core intent/service contract，不直接读文件。
6. summary 与 bundle 不得包含 token/password/cookie/authorization/secret/credential/auth、LCU Basic auth、lockfile password；用户路径按策略收敛/替换。
7. deterministic smoke 覆盖 secret redaction、malformed JSONL、size/file-count bounds、zip entries allowlist、summary determinism；source gate 禁止 diagnostics 获得业务写权限。
8. legacy Build/UI Text/4.0 Foundation latest-head 全绿；production release controls 不动。

## Gate 10 → Gate 13 固定顺序

- Gate 10：DPI / 多屏 / Accessibility。
- Gate 11：Recovery / Feature Flags / 更新保障。
- Gate 12：全量兼容 / 性能 / 发布矩阵。
- Gate 13：legacy 退休与 FACM 4.0 cutover；真实 release blockers 未关闭时不得发布 4.0.0。

## 持续保护的不变量

- exactly one League discovery/auth/session owner；所有 writer 保持最小 capability；Bench 手动。
- Mayhem/OP.GG 保留 fallback、timeout、body cancellation、cache、Performance Budget。
- Game Repair 保留 native Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer；不恢复 Fix-LCU runtime。
- Cleanup 保留 preview、explicit confirm、UAC、path allowlist、reparse guard、执行前重验证。
- Updater 保留 size limit、SHA-256、signature/package validation、validated receipt、独立 replacement、失败保旧版、rollback。
- Single Instance = Ensure Open；快捷键 = RegisterHotKey；PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## Gate 13 前仍需真实 Windows 证据

普通非管理员 UAC/取消、Defender/SmartScreen、Windows 10 1809/22H2 + Windows 11、100/125/150/175/200% DPI、双屏/负坐标/混合 DPI、keyboard/focus/high contrast/text scaling/basic screen reader、Updater interrupted replacement/rollback、3.5.15 -> 4.0 settings 真机升级。未关闭时不得声称 Gate 12/13 release-ready。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate 回复“继续”。生产 release 与 destructive Git 操作仍遵守 `AGENTS.md` 的 fresh safety check。
