# FACM 架构

## 1. 双轨迁移

FACM 3.5.15 WinForms 仍是 production/rollback baseline；FACM 4.0 使用 .NET 10 + WinUI 3。Gate 13 前不退休 legacy、不修改 production release controls。

```text
FACM.App
├─ MainWindow: one AppTitleBar + NavigationView + Frame
├─ FloatingWindow: narrow F desktop entry
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core
├─ Performance / Product State
├─ Settings 2.0 / UI Text
├─ Observability / Diagnostics
├─ Desktop geometry + DPI conversion
├─ Recovery / Feature policy
└─ Cleanup / League / Online contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/diagnostic IO           ├─ executable/runtime identity
├─ recovery/LKG stores              ├─ League session owner
├─ HTTP/League transport            ├─ monitor/work-area/DPI facts
├─ one Gameflow monitor             └─ Windows integration
└─ update metadata
```

Direction 固定：App -> Core/Infrastructure/Platform.Windows；Infrastructure -> Core；Platform.Windows -> Core；Core 不引用 UI/platform implementation。

## 2. UI 与 runtime ownership

Window/Page -> ViewModel -> Core intent/state。具体 adapter 只在 App composition root 组装。禁止 ViewModel/Page 创建 HttpClient、League runtime、settings/diagnostic/recovery file store、Process/Registry/Win32 implementation 或第二 polling loop。

4.0 exactly one `WindowsLeagueTransportSessionSource`、one shared `LeagueHttpGateway`、one `LeagueGameflowMonitor`、one `PerformanceBudgetProvider`。Workbench 只有 `比赛 / 攻略 / 自动化` 三层 IA；Bench 仍手动；writer 只能走 Core capability allowlist。

## 3. Stable paths / Settings / Diagnostics / Recovery

所有稳定路径只从 distribution EXE (`Environment.ProcessPath`) 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

```text
<distribution>/settings.ini
<distribution>/settings.v2.json
<distribution>/ui-text.ini
<distribution>/logs/facm4-events.jsonl
<distribution>/runtime/diagnostics/
<distribution>/runtime/recovery/state.json
<distribution>/runtime/recovery/settings.v2.lkg.json
<distribution>/runtime/recovery/feature-kill-switch.json
```

Settings 2.0 strict parser/validator 继续 fail closed；legacy import 后旧 INI Gate 13 前保留。Primary save same-dir temp + flush-to-disk + replace/move。

Gate 11 不放宽 strict Settings2。`RecoveringSettings2Repository` 只在 strict load 抛 `InvalidDataException` 后读取 validator-backed LKG；没有有效 LKG 时返回安全内存默认，并强制 `AutoUpdateEnabled=false`。坏 primary 不被 recovery 自动覆盖。

Diagnostics 复用 Gate 5 JSONL；Gate 9 只读 Product State + current JSONL + `.1`，再次 scrub secret/Basic/Bearer/Windows/UNC path，ZIP exactly `summary.txt/events.jsonl/manifest.json`。Diagnostics 无业务 writer。

## 4. Desktop coordinate / DPI contract

Core `AnchorPlacementService` 是 physical desktop geometry owner；支持负坐标、nearest monitor、edge/corner、margin/clamp、off-screen recovery。

Core `DesktopDpi` 是 DPI->scale 与 DIP->physical pixel 的唯一计算 contract：96/120/144/168/192 DPI 分别映射 100/125/150/175/200%。Windows adapter 只采集 work-area + effective DPI facts；FloatingWindow 也只调用 Core helper。

`EnumDisplayMonitors/GetMonitorInfo` work-area 与 `AppWindow.MoveAndResize` 使用 Windows desktop physical pixels。F nominal 64 DIP 根据目标 monitor scale 转 physical size 后才交给 Core placement。

## 5. DPI awareness / Accessibility

`FACM.App/app.manifest` 固定：legacy `dpiAware=true/pm`；modern `dpiAwareness=PerMonitorV2, PerMonitor`；execution `asInvoker`。

Main Shell/F/Diagnostics actionable controls 使用稳定 AutomationId。Accessible Name/HelpText 通过 `IUiTextProvider + UiTextKeys`，不维护第二套硬编码 accessibility 文案。

- NavigationViewItem / Button 保持 keyboard-capable 默认行为；禁止 pointer-only action path。
- 主要 action 不允许 `IsTabStop=False`。
- 长正文/状态/说明允许 `TextWrapping=Wrap`；关键 TextBlock 不使用固定高度裁剪。
- semantic colors alias WinUI platform theme resources；High Contrast 不增加 FACM 私有硬编码 palette。

`scripts/check-facm4-accessibility.ps1` 守 manifest、Core DPI ownership、5 档 DPI smoke、mixed-DPI fixtures、AutomationProperties、keyboard/text scaling/semantic theme contract。

## 6. Gate 11 Feature policy：只减权，不扩权

Feature policy 的 baseline 是 Core 手写 approved capability list，不从 enum 自动推导。当前显式 capability：Cleanup execute、Update check/install、Diagnostics export、以及现有四个 League write capability。

```text
EffectiveEnabled = ApprovedBaseline - DisabledKillSwitchSet
```

没有 `enabled=true` remote/local override。新增 enum 不会自动启用。`FeaturePolicyEvaluator.IsNoMorePermissive` 用于证明 candidate policy 是 baseline 子集。

`FeatureGatedLeagueWriteGateway / FeatureGatedCleanupExecutor / FeatureGatedUpdateManifestSource / FeatureGatedUpdateInstaller / FeatureGatedDiagnosticsBundleExporter` 都在调用底层实现之前拒绝 disabled capability。

`feature-kill-switch.json` 只有 schemaVersion + disabled。未知字段、未知 capability、坏 JSON、超界或读取失败全部 fail closed：disable all approved capabilities。Kill switch 不枚举目录、不访问网络、不拥有 writer。

App 当前实际把 Update check 与 Diagnostics export 接到 feature policy；WinUI 尚未新增 League/Cleanup/UpdateInstall writer surface。未来若接入这些动作，也必须通过现有 gated wrapper/capability contract。

## 7. Gate 11 Recovery / LKG

Recovery Core phase：`Clean / Starting / Running / Failed / Recovering`。上一轮留下 `Starting` 时，新启动识别 `previous-start-incomplete`；成功进入 Running 后刷新 last-known-good app version 并清 failure count。

`JsonRecoveryStateStore`：64 KiB bounded，same-directory temp + WriteThrough + flush-to-disk + replace。malformed/oversized state 回安全 initial state。Recovery metadata 自身是 defense-in-depth，写入失败不得把本来能启动的产品变成 crash。

Settings LKG：只有 strict validated primary load/save 成功后才 best-effort 刷新 `settings.v2.lkg.json`。corrupt primary 保留原文，不自动 promote recovery copy 覆盖 primary。

Recovery reason/diagnostic 只记录受控枚举/异常类型，不持久化任意 `exception.Message`、credential 或 raw user path。

## 8. Update recovery boundary

`UpdateRecoveryPolicy` 只是 Core recovery decision contract，不替代正式 updater。

- 没有 `ValidatedReceipt`：禁止 replacement，保留当前版本。
- replacement 前必须有 old-version preservation 语义。
- replacement failed：旧版本保持可用；有 rollback evidence 时进入 rollback path。
- replacement 完成前 candidate 不能成为新的 LKG。

正式 updater 仍必须保留 size limit、SHA-256、signature/package validation、validated receipt、wait-exit、separate replacement、failure keeps old、rollback。Gate 11 不修改 production update pointer。

## 9. Engineering evidence vs real-machine evidence

Hosted CI 能证明 source/API/contracts、deterministic state machines、fake-writer zero-call、physical temp-file atomic behavior、Windows runner runtime-path/monitor facts、WinUI build/publish。

它不能证明真实 UAC/Defender/OS/multi-monitor/accessibility/interrupted updater 用户体验。缺少真实证据时必须在 Gate 12 记录为 `blocked` 或 `not-run`，不得伪装成 `passed`。

## 10. Gate 12 evidence architecture

Gate 12 不再新增一套业务 runtime；它聚合 Gates 0～11 的发布证据并守性能基线。

结构化 evidence 必须至少表达：

```text
id / category / status(passed|blocked|not-run) / source / notes
```

规则：

- automated `passed` 必须指向可追溯 CI/smoke/source evidence；
- external/manual `passed` 必须有真实设备/操作证据引用；
- 没证据不能默认 pass；
- required blocker 为 blocked/not-run 时，Gate 12 可完成 engineering work，但 overall release readiness 必须是 BLOCKED；
- evidence matrix 不得修改 production pointer。

Gate 12 同时重验 Performance Contract、League cadence、exactly-one session/gameflow owner、UI no-polling、已有 deterministic smoke/source gate 不消失。

## 11. Persistent invariants

- Cleanup：preview -> explicit confirm -> UAC -> allowlist/reparse guard -> execution-time revalidation。
- Single Instance = Ensure Open / Activate。
- Hotkey = RegisterHotKey；不使用 low-level hook/GetAsyncKeyState/polling。
- PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke/source gates 不得静默删除。

## 12. Release boundary

Gate 13 只有在 Gates 0～12 evidence 闭环且获得 fresh production/destructive authorization 后才能退休 legacy、改 production pointer、发布 4.0.0；否则必须保持 **release blocked**。
