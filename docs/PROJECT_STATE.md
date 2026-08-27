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
- Gate 7：COMPLETE，#200 / PR #201，Gate merge `95f2ce66f2d5e058c0bc2052d619ce9473cf1aa9`；其后的透明 NOOP 新增/纠正提交文件差异为 0。
- Gate 8：COMPLETE，#202 / PR #203，`main@0aebcc6d31cf715b012cf2725deb40b6dacdb25e`。
- Gate 9：IMPLEMENTATION VERIFIED，#204 / PR #205；implementation head `26d049bdd99dba20c85039d3a3980aeadd8ae05d` 三线全绿，canonical docs 提交后需 latest-head CI 再确认再合入。
- Gate 10～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- 技术栈：.NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 legacy 持续作为 rollback baseline。
- single-file 分发以 `Environment.ProcessPath` 作为 distribution EXE identity；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`，不得作为 settings/cache/log/runtime/PetHost/update replacement 根目录。
- UI 通过 ViewModel -> Core intent/state contract；Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；exactly one gameflow polling owner；所有 writer 继续使用最小 capability allowlist。
- production `online/version.json` / `release/request.json` Gate 13 前保持 FACM 3.5.15。

## Gates 1～6 摘要

- Gate 1：并行 .NET 10 solution、Core/Infrastructure/Platform.Windows/App/Smoke、Performance Contract、legacy settings codec、UI Text foundation、architecture gate。
- Gate 2：Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write-target policy。
- Gate 3：唯一 `WindowsLeagueTransportSessionSource`、shared `LeagueHttpGateway`、secret-safe session parser、strict update metadata、distribution-based `RuntimePathLayout`、`FACM.WindowsSmoke`。
- Gate 4：Settings 2.0 schema v2，Environment / Online / Appearance / Pets / League typed sections；legacy INI deterministic migration；same-directory atomic save；legacy `settings.ini` Gate 13 前只读保留。
- Gate 5：`ProductStateStore` + structured observability；同状态不增加 revision；subscriber lock 外通知；`DiagnosticRedactor` + bounded JSONL sink 双层脱敏。
- Gate 6：WinUI semantic Design System + one Main Shell：one AppTitleBar / one NavigationView / one Frame / exactly four product entries；用户 copy 走 UI Text contract。

## Gate 7 — Desktop Shell / F 悬浮入口：COMPLETE

- Core `AnchorPlacementService`：纯几何、负坐标、主屏 fallback、nearest work-area、edge/corner anchor、margin/clamp、off-screen recovery。
- `WindowsDesktopWorkAreaProvider`：`EnumDisplayMonitors/GetMonitorInfo` + per-monitor DPI facts；Core 不依赖 Win32。
- `FloatingWindow` 共享 semantic resources，只做 F desktop entry / Ensure MainWindow；不拥有 League/settings/HTTP/diagnostic runtime。
- MainWindow 关闭后 F 保留；F 点击 = create-or-activate，Single Instance 语义仍是 Ensure Open / Activate，不是 toggle。
- Gate 7 final：Foundation #120 / Windows Build #1334 / UI Text #455 SUCCESS。
- hosted runner work-area/DPI 只算工程证据，不替代 Gate 10/12 mixed-DPI 真机矩阵。

## Gate 8 — LOL 工作台状态驱动 UX：COMPLETE

- Core `LeagueGameflowPhaseMapper` 一次映射 LCU phase -> `LeagueProductState + LeagueActivityLevel`。
- Product State：NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError。
- `LeagueGameflowCadence`：ChampSelect 2s；Matchmaking/ReadyCheck 3s；InGame 10s；connected other 5s；disconnected/connecting/error 10s。
- `LeagueGameflowMonitor` 是 4.0 唯一 gameflow polling owner；复用 shared `ILeagueReadGateway + ILeagueSessionAccessor`，同源更新 Product State + Performance Budget。
- Workbench IA exactly 3：`比赛 / 攻略 / 自动化`；ViewModel 不知道 raw `/lol-*` path、HTTP、session、polling 或 writer。
- Bench 仍为用户显式手动动作；Gate 8 未扩大任何后台 writer 权限。
- Gate 8 latest-head：Foundation #145 / Windows Build #1341 / UI Text #462 SUCCESS；squash merge `main@0aebcc6d31cf715b012cf2725deb40b6dacdb25e`。

## Gate 9 — Diagnostics Center / 脱敏诊断包：IMPLEMENTATION VERIFIED

Tracking：Issue #204，branch `feat/facm-4-gate9-diagnostics`，PR #205。

### Read-only diagnostics contract

- 复用 Gate 5 `DiagnosticEvent / DiagnosticRedactor / BoundedJsonLinesDiagnosticSink`，没有第二日志系统。
- Core 新增 `DiagnosticsSnapshot / DiagnosticsExportPolicy / DiagnosticsExportReceipt / IDiagnosticsSnapshotSource / IDiagnosticsBundleExporter`。
- `DiagnosticsSummaryFormatter` 生成 deterministic summary；summary/bundle 出口都再次调用更严格的 `DiagnosticsExportSanitizer`。
- snapshot 输入 allowlist 固定为：内存 `ProductStateSnapshot`、`<distribution>/logs/facm4-events.jsonl`、可选 `.1` rotation。
- 默认禁止 settings、League lockfile、环境变量、Registry、browser cookies、任意目录递归、crash dump/raw memory。

### Sanitizer / bounds

- Gate 5 token/password/passwd/cookie/authorization/secret/credential/auth 脱敏继续保留。
- Gate 9 再处理 Basic/Bearer credentials、Windows absolute path、UNC path；Product State distribution directory 也转换成 `[path]`。
- malformed JSONL 只增加 skipped 计数，不把原始脏行写入 summary/bundle。
- 默认 bounds：500 events；单输入文件 4 MiB；总输入 8 MiB；3 ZIP entries；单 entry 4 MiB；bundle 8 MiB；summary 64 Ki chars。
- ZIP allowlist exactly 3：`summary.txt / events.jsonl / manifest.json`；export 先写 temp，再 move 到 final。
- diagnostics output 只落在 `<distribution>/runtime/diagnostics`，UI 不传任意输出目录。

### Diagnostics Center UI / ownership

- `更多设置` 下新增 Diagnostics Center：刷新摘要、复制摘要、导出脱敏 bundle。
- `DiagnosticsCenterViewModel` 只依赖 Core diagnostics contracts；不直接 File/Directory/ZipArchive，也没有 League/Cleanup/Updater writer。
- Clipboard 是 WinUI code-behind 的窄平台动作；日志读取与 ZIP 写入只在 Infrastructure adapter。
- `scripts/check-facm4-diagnostics.ps1` 自动守只读 ownership、输入/ZIP allowlist、UI boundary、exactly-one snapshot source/exporter composition。

### Gate 9 evidence

implementation head `26d049bdd99dba20c85039d3a3980aeadd8ae05d`：

- `FACM 4.0 Foundation` #162：SUCCESS；architecture / Shell / desktop / League Workbench / Diagnostics source gates、restore/build、FoundationSmoke、WindowsSmoke、single-file publish、output verify、artifact upload 全 SUCCESS。
- `FACM Windows Build` #1344：SUCCESS。
- `FACM UI Text Contract` #465：SUCCESS。
- artifact `facm4-x64` id `9643007237`，ZIP `88,292,608` bytes，digest `sha256:8a85ba1bbe8daf7cf481984aae2a83386aca011b3121f98c44b69012ec98cc7a`。
- 首轮 CI 暴露 `DiagnosticsExportPolicy.Default` target-typed `new(...).Validate()` 的 C# 目标类型错误；已改为显式 `new DiagnosticsExportPolicy(...)`，没有降低 warning/source gate。
- `Gate9Smoke` 覆盖 valid/malformed JSONL、Basic/Bearer + secret + Windows path 二次脱敏、summary determinism、input/event/bundle bounds、ZIP exact allowlist、bundle 无原始 secret/path。

## Gate 10 — NEXT：DPI / 多屏 / Accessibility

Gate 9 合入后从最新 main 新开 Issue/branch/PR。已确认当前 4.0 `app.manifest` 只有 `asInvoker + supportedOS`，尚未显式声明 PerMonitorV2；Gate 10 第一项工程修复是建立明确 DPI-awareness contract，而不是依赖隐式默认。

固定目标：

1. manifest/Windows runtime 明确 PerMonitorV2，继续使用 physical desktop coordinate facts；禁止 UI 再做第二次坐标缩放。
2. automated source/smoke：100/125/150/175/200% scale math、负坐标/左右/上下 work-area、mixed-DPI placement conversion、off-screen recovery。
3. Main Shell/F/Diagnostics/Workbench 交互元素建立 keyboard focus、AutomationProperties Name/HelpText（通过 UI Text）、合理 tab sequence 与可见 focus；不靠 mouse-only path。
4. semantic theme resources 持续负责 Light/Dark/High Contrast；用户可见文字避免固定像素高度裁剪，允许 text scaling/wrap。
5. hosted Windows runner 验证 manifest/source/API 和 synthetic geometry；**真实 mixed-DPI 双屏、keyboard-only、High Contrast、text scaling、basic screen reader 仍是 Gate 10/12 外部证据**。
6. 不因缺少真实硬件证据伪称 release-ready；工程 Gate 可把未完成真机矩阵记录为 Gate 12/13 blocker。

## Gate 11 → Gate 13 固定顺序

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
