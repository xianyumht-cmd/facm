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

> FACM 4.0 Gate 13 前不得修改生产 `online/version.json` / `release/request.json` 指向。

## FACM 4.0 总进度

- Gate 0：COMPLETE，#185 / PR #186。
- Gate 1：COMPLETE，#187 / PR #188。
- Gate 2：COMPLETE，#189 / PR #190。
- Gate 3：COMPLETE，#191 / PR #192。
- Gate 4：COMPLETE，#193 / PR #195。
- Gate 5：COMPLETE，#196 / PR #197。
- Gate 6：COMPLETE，#198 / PR #199。
- Gate 7：COMPLETE，#200 / PR #201。
- Gate 8：COMPLETE，#202 / PR #203，`main@0aebcc6d31cf715b012cf2725deb40b6dacdb25e`。
- Gate 9：COMPLETE，#204 / PR #205，`main@1ad8ddf9365dd60954188f04e630c2eb22e15e5e`。
- Gate 10：IMPLEMENTATION VERIFIED，#206 / PR #207，implementation head `93514fc32f6c4a0c03fd3a2d2bccbd94c3dbf247`；canonical docs 后需 latest-head CI 再确认再合入。
- Gate 11～13：继续顺序推进；不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- .NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 3.5.15 继续作为 rollback baseline。
- single-file 稳定路径只从 `Environment.ProcessPath` 推导；不得把 `%TEMP%/.net/...` self-extract `AppContext.BaseDirectory` 当安装根。
- UI -> ViewModel -> Core intent/state；Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；exactly one gameflow polling owner；writer 只能使用最小 capability。
- Bench 仍为用户显式手动动作。

## Gates 1～6 摘要

- Gate 1：并行 .NET 10 solution、Core/Infrastructure/Platform/App/Smoke、Performance/UI Text/architecture foundation。
- Gate 2：Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write target policy。
- Gate 3：唯一 Windows League session owner、shared `LeagueHttpGateway`、runtime path、bounded update metadata、WindowsSmoke。
- Gate 4：Settings 2.0 schema v2、legacy 15-key deterministic migration、same-directory atomic save；旧 INI Gate 13 前保留。
- Gate 5：`ProductStateStore` + structured observability + bounded redacted JSONL。
- Gate 6：semantic WinUI Design System；one AppTitleBar / NavigationView / Frame；四产品入口；UI Text 驱动 copy。

## Gate 7 — Desktop / F：COMPLETE

- Core `AnchorPlacementService` 处理负坐标、nearest work-area、edge/corner、margin/clamp、off-screen recovery。
- Windows adapter 提供 physical-pixel work-area + monitor DPI facts。
- F surface 只做 Ensure MainWindow；点击 = create-or-activate，不是 toggle；关闭 F 才 shutdown。
- Foundation #120 / Windows Build #1334 / UI Text #455 SUCCESS。

## Gate 8 — League Workbench：COMPLETE

- one `LeagueGameflowMonitor` 复用 shared League gateway/session source。
- phase 一次映射到 `LeagueProductState + LeagueActivityLevel`，同源更新 Product State + Performance。
- cadence：ChampSelect 2s；Matchmaking/ReadyCheck 3s；InGame 10s；connected other 5s；disconnected/error 10s。
- Workbench exactly 3：`比赛 / 攻略 / 自动化`；ViewModel 无 raw LCU/polling/writer。
- Foundation #145 / Windows Build #1341 / UI Text #462 SUCCESS。

## Gate 9 — Diagnostics Center：COMPLETE

- 复用 Gate 5 observability，不建立第二日志系统。
- snapshot 输入 allowlist：内存 Product State + `logs/facm4-events.jsonl` + `.1`；不扫 settings/lockfile/env/Registry/user directories。
- export 再次处理 secret、Basic/Bearer、Windows/UNC paths；malformed JSONL 只计数并丢弃原文。
- ZIP exactly `summary.txt / events.jsonl / manifest.json`，bounded + temp -> final；输出固定 `<distribution>/runtime/diagnostics`。
- Diagnostics Center 支持刷新、复制摘要、导出 bundle；ViewModel 不直接 File/Directory/ZipArchive 或业务 writer。
- latest-head Foundation #168 / Windows Build #1347 / UI Text #468 SUCCESS；merge `main@1ad8ddf9365dd60954188f04e630c2eb22e15e5e`。

## Gate 10 — DPI / 多屏 / Accessibility：IMPLEMENTATION VERIFIED

Tracking：Issue #206，branch `feat/facm-4-gate10-dpi-accessibility`，PR #207。

### DPI contract

- `app.manifest` 显式声明 `PerMonitorV2, PerMonitor`，同时保留 legacy `true/pm`；执行级别仍 `asInvoker`。
- Core `DesktopDpi` 是 DPI -> scale / DIP -> physical pixel 单一转换 contract，96 DPI 为基准。
- `WindowsDesktopWorkAreaProvider` 与 `FloatingWindow` 都调用 Core helper；F 不再自写 `dip * scale`。
- Gate10Smoke deterministic 覆盖：96/120/144/168/192 DPI = 100/125/150/175/200%，64 DIP = 64/80/96/112/128 physical px；mixed X/Y scale；左/右/上方/负坐标 mixed-DPI work-area；off-screen recovery；非法 DPI fail closed。

### Accessibility contract

- Main navigation、Diagnostics summary/buttons、F entry 使用稳定 AutomationId。
- Accessible Name/HelpText 全部通过 `IUiTextProvider + UiTextKeys`，不另写硬编码辅助文案。
- MainWindow 长正文/状态/说明使用 `TextWrapping=Wrap`；关键正文不使用固定 TextBlock height。
- action 保持 keyboard-capable Button/NavigationView；source gate 禁止 pointer-only handlers 和 `IsTabStop=False` 回退。
- semantic theme 继续 alias WinUI platform resources；High Contrast 不新增硬编码 palette。
- `scripts/check-facm4-accessibility.ps1` 自动验证 manifest、Core DPI ownership、5 档 DPI smoke、AutomationProperties、text scaling/keyboard/source contract。

### Gate 10 implementation evidence

head `93514fc32f6c4a0c03fd3a2d2bccbd94c3dbf247`：

- `FACM 4.0 Foundation` #182：SUCCESS，含全部 source gates、restore/build、Gate10Smoke、WindowsSmoke、single-file publish/output/artifact。
- `FACM Windows Build` #1349：SUCCESS。
- `FACM UI Text Contract` #470：SUCCESS。
- artifact `facm4-x64` id `9643451825`，ZIP `88,294,626` bytes，digest `sha256:3dfd07142c3ee294e545fb51ef614d13e50eabc03c589a3f13cf810767b1dcfb`。

### 仍未完成的真实证据

以下不是 hosted runner 能证明的内容，继续作为 Gate 12/13 release blockers：Win10 1809/22H2 + Win11；100/125/150/175/200% 真机；左右/上下双屏、负坐标、mixed DPI 屏间移动；keyboard-only/focus；High Contrast；text scaling；basic screen reader。**Gate 10 工程门禁通过不等于 release-ready。**

## Gate 11 — NEXT：Recovery / Feature Flags / 更新保障

固定目标：

1. Core typed Feature Flags，默认安全；风险/可选功能没有明确 enable 时不得开启。
2. kill switch **只能 disable/degrade**，不得把 false 变 true、不得增加 `LeagueWriteCapability` 或其它 writer permission。
3. recovery state / last-known-good：Settings 2.0 与更新流程能区分 current/candidate/LKG，坏候选不覆盖可用基线。
4. update recovery contract 保留 size/hash/signature/package/validated receipt/wait-exit/separate replacement/failure keeps old/rollback invariants；Gate 11 不修改 production pointer。
5. deterministic smoke 覆盖 flag monotonicity、unknown flag fail closed、LKG fallback、坏 candidate 不晋升、kill switch 不扩权。
6. source gate + legacy/4.0 latest-head 全绿后进入 Gate 12。

## Gate 12 / Gate 13 release blockers

Gate 12 汇总全量兼容/性能/真机证据。当前已知外部 blockers：non-admin UAC + cancel、Defender/SmartScreen、Win10/11、DPI/multi-monitor/accessibility、Updater interrupted replacement/rollback、3.5.15 -> 4.0 settings 真机升级。

Gate 13 只有在 Gates 0～12 证据闭环且获得 fresh production/destructive authorization 后，才可退休 legacy / 改 production pointer / 发布 4.0.0；否则状态必须是 **release blocked**，不能假完成。

## 持续保护的不变量

- Mayhem/OP.GG fallback/timeout/body cancellation/cache/Performance Budget。
- Game Repair native Win32 + 多屏/负坐标 + 窄 writer；不恢复 Fix-LCU runtime。
- Cleanup preview/explicit confirm/UAC/path allowlist/reparse guard/execution-time revalidation。
- Updater size/SHA-256/signature/package/receipt/separate replacement/failure keeps old/rollback。
- Single Instance = Ensure Open / Activate；Hotkey = RegisterHotKey；PetHost 独立进程。
- Performance/UI Text/deterministic smoke 不得静默删除。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate回复“继续”。生产 release 与 destructive Git 操作仍遵守 fresh safety check。
