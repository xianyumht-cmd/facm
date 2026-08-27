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
- Gate 10：COMPLETE，#206 / PR #207，`main@c8eebe414f332cb524069395c3d74b51c12bdaa0`。
- Gate 11：IMPLEMENTATION VERIFIED，#208 / PR #209，implementation head `c98432b287690813f6a82b04db924e67525e4940`；canonical docs 后需 latest-head CI 再确认再合入。
- Gate 12～13：继续顺序推进；不要求用户逐 Gate 回复“继续”。

## 已冻结的 4.0 基线

- .NET 10 LTS + WinUI 3 + Windows App SDK 2.4.0，x64 first。
- `FACM4.sln` 与 legacy `FACM.sln` 并行；Gate 13 前 3.5.15 始终是 production rollback baseline。
- single-file 稳定路径只从 `Environment.ProcessPath` 推导；不得把 `%TEMP%/.net/...` self-extract `AppContext.BaseDirectory` 当安装/配置/更新根。
- UI -> ViewModel -> Core intent/state；Infrastructure / Platform.Windows adapter 只在 App composition root 组装。
- exactly one League discovery/auth/session owner；exactly one gameflow polling owner；writer 只能使用最小 capability。
- Bench 仍为用户显式手动动作。
- Performance Contract、UI Text Contract、deterministic smoke/source gates 不得静默删除。

## Gates 1～9 摘要

- Gate 1：并行 .NET 10 solution、Core/Infrastructure/Platform/App/Smoke foundation。
- Gate 2：Cleanup/League/Online/Settings Core contracts、ViewModel intent boundary、League exact write-target policy。
- Gate 3：唯一 Windows League session owner、shared `LeagueHttpGateway`、distribution runtime path、bounded update metadata、WindowsSmoke。
- Gate 4：Settings 2.0 schema v2、legacy 15-key deterministic migration、same-directory atomic save；旧 INI Gate 13 前保留。
- Gate 5：`ProductStateStore` + structured observability + bounded redacted JSONL。
- Gate 6：semantic WinUI Design System；one AppTitleBar / NavigationView / Frame；四产品入口；UI Text 驱动 copy。
- Gate 7：Core desktop anchor geometry + F Ensure Open / Activate；负坐标/nearest/off-screen recovery。
- Gate 8：one Gameflow owner；Product State + Performance 同源；Workbench exactly `比赛 / 攻略 / 自动化`。
- Gate 9：只读 Diagnostics Center；bounded input/ZIP + 二次 secret/path scrub；无业务 writer。

## Gate 10 — DPI / 多屏 / Accessibility：COMPLETE

- `app.manifest` 显式 `PerMonitorV2, PerMonitor` + legacy `true/pm`，仍 `asInvoker`。
- Core `DesktopDpi` 是 DPI -> scale / DIP -> physical pixel 单一 contract。
- deterministic 覆盖 96/120/144/168/192 DPI = 100/125/150/175/200%，mixed X/Y scale、左/右/上/负坐标、多屏与 off-screen recovery。
- Main navigation、Diagnostics、F entry 有稳定 AutomationId；Name/HelpText 走 UI Text；主要 action 保持 keyboard-capable；正文允许 Wrap；semantic theme 继续依赖 WinUI platform resources。
- latest-head：Foundation #188 / Windows Build #1352 / UI Text #473 SUCCESS；squash merge `main@c8eebe414f332cb524069395c3d74b51c12bdaa0`。
- hosted runner 仍不替代 mixed-DPI / High Contrast / screen reader 等真机 evidence。

## Gate 11 — Recovery / Feature Flags / 更新保障：IMPLEMENTATION VERIFIED

Tracking：Issue #208，branch `feat/facm-4-gate11-recovery-flags`，PR #209。

### Feature policy

- Core `FacmFeatureCapability` 显式白名单：Cleanup execute、Update check/install、Diagnostics export、现有四个 League write capability。
- `FeatureBaseline` 是手写 approved list；禁止 `Enum.GetValues()` 自动放行未来 enum。
- kill switch 数据模型只有 `disabled` set；effective policy = approved baseline 减 disabled。没有 remote/local enable override。
- `FeaturePolicyEvaluator.IsNoMorePermissive` + Gate11Smoke 证明 reduced policy 永远是 baseline 子集。
- `FeatureGatedLeagueWriteGateway / CleanupExecutor / UpdateManifestSource / UpdateInstaller / DiagnosticsBundleExporter` 在调用底层前 fail closed。
- App 已实际让 Update check 与 Diagnostics export 受 feature policy 控制；当前 WinUI 尚未暴露 League/Cleanup/UpdateInstall 新 writer surface。

### Kill switch source

稳定路径：`<distribution>/runtime/recovery/feature-kill-switch.json`。

- 32 KiB bounded；schema v1。
- JSON 只允许 `schemaVersion` 与 `disabled`。
- unknown property / unknown capability / bad JSON / read failure => `FeatureKillSwitch.DisableAllApproved()`。
- 不枚举目录、不访问网络、不获得业务 writer。

### Recovery / Settings LKG

稳定路径：

```text
<distribution>/runtime/recovery/state.json
<distribution>/runtime/recovery/settings.v2.lkg.json
```

- Recovery phase：Clean / Starting / Running / Failed / Recovering。
- 上一轮停在 `Starting` 会识别为 `previous-start-incomplete`；成功 Running 后刷新 last-known-good app version 并清 failure count。
- recovery metadata 64 KiB bounded，same-directory temp + WriteThrough + flush-to-disk + replace；malformed/oversized metadata 回安全 initial state。
- 严格 `Settings2Repository` 没放宽：corrupt/future schema 仍抛 `InvalidDataException`。
- 外层 `RecoveringSettings2Repository` 仅在 strict load 失败后读取 validator-backed LKG；有 LKG -> `RecoveredLastKnownGood`；无 LKG -> `RecoveryDefaults`，并强制 `AutoUpdateEnabled=false`。
- corrupt primary 不自动覆盖，保留给 Diagnostics/人工判断；有效 primary/load/save 才 best-effort 刷新 LKG。

### Update recovery contract

- `UpdateRecoveryPolicy` 不替代正式 updater，只约束 recovery 决策。
- candidate 没有 `ValidatedReceipt` 时禁止 replacement。
- replacement 前必须显式考虑 `OldVersionPreserved`；replacement failed 时 `KeepCurrentVersion=true`，有 rollback evidence 时要求 rollback path。
- 原 updater 的 size limit、SHA-256、signature/package validation、wait-exit、separate replacement、failure keeps old、rollback 不得被 Gate 11 绕过。
- production update pointer 完全未改。

### Gate 11 implementation evidence

implementation head `c98432b287690813f6a82b04db924e67525e4940`：

- `FACM 4.0 Foundation` #204：SUCCESS；architecture / Shell / desktop / Workbench / Diagnostics / DPI-Accessibility / Recovery source gates、restore/build、Gate11Smoke、WindowsSmoke、single-file publish/output/artifact 全 SUCCESS。
- `FACM Windows Build` #1354：SUCCESS。
- `FACM UI Text Contract` #475：SUCCESS。
- artifact `facm4-x64` id `9644291683`，ZIP `88,317,150` bytes，digest `sha256:b2d4e30551ae15ba2fd7345edf61d74cab12ffe4983e6aec109e4492d00547aa`。
- Gate11Smoke 实际覆盖 unknown feature fail-closed、disabled writer zero-call、malformed/oversized recovery metadata、previous-start-incomplete、Settings LKG/default fallback + corrupt primary 保留、unvalidated update block、replacement failure keeps old。

## Gate 12 — NEXT：全量兼容 / 性能 / 发布矩阵

Gate 11 合入后从最新 main 新开 Issue/branch/PR。Gate 12 要把**自动化工程证据**与**真实 Windows 设备证据**拆开记录，不允许用 hosted CI 冒充真机。

自动化工程范围：

1. 聚合 Gates 1～11 所有 source/smoke 为 release candidate matrix，不允许已有 deterministic smoke 静默消失。
2. 建 machine-readable release evidence manifest：OS/DPI/accessibility/UAC/Defender/updater/settings migration 等项目必须有 `passed / blocked / not-run` 与 evidence source。
3. 性能回归：Performance Contract 各 League phase、poll cadence、并发上限、启动/idle/in-game budget 不得比冻结基线更激进。
4. distribution candidate 记录 EXE/hash/size/artifact digest；production pointer 仍冻结 3.5.15。
5. Gate 12 可以把缺少的真实 evidence 标 `BLOCKED`，但不得伪造 PASS。

当前必须保留为真实 external blockers：non-admin UAC + cancel、Defender/SmartScreen、Win10 1809/22H2 + Win11、100/125/150/175/200% DPI、dual/mixed DPI/negative coordinates、keyboard-only/focus、High Contrast、text scaling、basic screen reader、3.5.15 -> 4.0 settings 真机升级、interrupted updater replacement/rollback。

## Gate 13 release boundary

Gate 13 只有在 Gates 0～12 证据闭环并获得 fresh production/destructive authorization 后，才可退休 legacy / 改 production pointer / 发布 4.0.0；否则状态必须保持 **release blocked**。

branch/tag 删除、生产 deploy/restart、production pointer 修改都不自动执行。

## 新对话接续

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 main / 当前 Gate Issue+PR+CI 后直接继续；不要要求用户逐 Gate 回复“继续”。
