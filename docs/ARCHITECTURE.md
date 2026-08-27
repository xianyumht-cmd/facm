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
├─ Performance / Product State / Feature contracts
├─ Settings 2.0 / UI Text
├─ Observability / Diagnostics
├─ Desktop geometry + DPI conversion
├─ Cleanup / League / Online contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/diagnostic IO           ├─ executable/runtime identity
├─ diagnostics reader/exporter      ├─ League session owner
├─ HTTP/League transport            ├─ monitor/work-area/DPI facts
├─ one Gameflow monitor             └─ Windows integration
└─ update metadata
```

Direction 固定：App -> Core/Infrastructure/Platform.Windows；Infrastructure -> Core；Platform.Windows -> Core；Core 不引用 UI/platform implementation。

## 2. UI 与 runtime ownership

Window/Page -> ViewModel -> Core intent/state。具体 adapter 只在 App composition root 组装。禁止 ViewModel/Page 创建 HttpClient、League runtime、settings/diagnostic file store、Process/Registry/Win32 implementation 或第二 polling loop。

4.0 exactly one `WindowsLeagueTransportSessionSource`、one shared `LeagueHttpGateway`、one `LeagueGameflowMonitor`、one `PerformanceBudgetProvider`。Workbench 只有 `比赛 / 攻略 / 自动化` 三层 IA；Bench 仍手动；writer 只能走 Core capability allowlist。

## 3. Stable paths / Settings / Diagnostics

所有稳定路径只从 distribution EXE (`Environment.ProcessPath`) 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

```text
<distribution>/settings.ini
<distribution>/settings.v2.json
<distribution>/ui-text.ini
<distribution>/logs/facm4-events.jsonl
<distribution>/runtime/diagnostics/
```

Settings 2.0 legacy import 后旧 INI Gate 13 前保留；save same-dir temp + flush-to-disk + replace/move。

Diagnostics 复用 Gate 5 JSONL；Gate 9 只读 Product State + current JSONL + `.1`，再次 scrub secret/Basic/Bearer/Windows/UNC path，ZIP exactly `summary.txt/events.jsonl/manifest.json`。Diagnostics 无业务 writer。

## 4. Desktop coordinate / DPI contract

Core `AnchorPlacementService` 是 physical desktop geometry owner；支持负坐标、nearest monitor、edge/corner、margin/clamp、off-screen recovery。

Gate 10 新增 Core `DesktopDpi`：

```text
DPI 96  -> scale 1.00
DPI 120 -> scale 1.25
DPI 144 -> scale 1.50
DPI 168 -> scale 1.75
DPI 192 -> scale 2.00
```

`DesktopDpi` 是 DPI->scale 与 DIP->physical pixel 的唯一计算 contract。`WindowsDesktopWorkAreaProvider` 只采集 work-area + effective DPI facts，再调用 Core helper；`FloatingWindow` 也调用 Core helper，禁止 UI 再写第二套 `dip * scale`。

`EnumDisplayMonitors/GetMonitorInfo` work-area 与 `AppWindow.MoveAndResize` 都使用 Windows desktop physical pixels。F nominal 64 DIP 根据目标 monitor scale 转 physical size 后才交给 Core placement。

## 5. DPI awareness

`FACM.App/app.manifest` 显式声明：

```text
legacy fallback: dpiAware = true/pm
modern: dpiAwareness = PerMonitorV2, PerMonitor
execution: asInvoker
```

DPI-awareness 不得暗中改成 always-elevated；也不得为了 DPI 问题退回 WinForms。

## 6. Accessibility contract

Main Shell/F/Diagnostics 的 actionable controls 使用稳定 AutomationId。Accessible Name/HelpText 通过 `IUiTextProvider + UiTextKeys`，不维护第二套硬编码 accessibility 文案。

- NavigationViewItem / Button 保持 keyboard-capable 默认行为；禁止 pointer-only action path。
- source contract 禁止 `IsTabStop=False` 把主要 action 移出键盘导航。
- 长正文/状态/说明允许 `TextWrapping=Wrap`；关键 TextBlock 不使用固定高度裁剪。
- semantic colors alias WinUI platform theme resources；High Contrast 不增加 FACM 自有硬编码 palette。
- Floating F button具有 AutomationId + provider-driven Name/HelpText。

`scripts/check-facm4-accessibility.ps1` 守 manifest、Core DPI ownership、5 档 DPI smoke、mixed-DPI fixtures、AutomationProperties、keyboard/text scaling/semantic theme contract。

## 7. Engineering evidence vs real-machine evidence

Gate 10 hosted CI 能证明：manifest/source contract 可编译、Core DPI math、synthetic mixed-DPI geometry、Windows runner monitor/DPI API、WinUI accessibility API wiring。

它**不能**证明真实用户体验。以下仍需 Gate 12/13 evidence：Win10 1809/22H2 + Win11；100/125/150/175/200%；左右/上下 dual monitor、负坐标、mixed DPI 屏间移动；keyboard-only/focus；High Contrast；text scaling；basic screen reader。

## 8. Gate 11 Recovery / Feature Flags boundary

Gate 11 的 Feature Flags 必须是 Core typed contract，default-safe。Remote/local kill switch 只能做集合交集式收缩：

```text
EffectiveEnabled = LocalAllowed AND RemoteAllowed AND RecoveryAllows
```

任何 remote/kill-switch 输入都不能把本地 false 变 true，也不能新增 `LeagueWriteCapability` 或其它 writer permission。unknown flags fail closed。

Recovery/LKG 必须区分 current/candidate/last-known-good；candidate 只有通过 validation 后才能 promote。坏 settings/update candidate 不覆盖 LKG。

Updater 继续守 size limit、SHA-256、signature/package validation、validated receipt、wait-exit、separate replacement、failure keeps old、rollback。Gate 11 不修改 production update pointer。

## 9. Persistent invariants

- Cleanup：preview -> explicit confirm -> UAC -> allowlist/reparse guard -> execution-time revalidation。
- Single Instance = Ensure Open / Activate。
- Hotkey = RegisterHotKey；不使用 low-level hook/GetAsyncKeyState/polling。
- PetHost 保持独立进程。
- Performance Contract、UI Text Contract、deterministic smoke/source gates 不得静默删除。

## 10. Release boundary

Gate 12 汇总兼容/性能/真实设备 evidence。Gate 13 只有在 Gates 0～12 闭环并获得 fresh production/destructive authorization 后才能退休 legacy、改 production pointer、发布 4.0.0；否则必须保持 release blocked。
