# FACM 架构

## 1. 双轨迁移与依赖方向

FACM 3.5.15 WinForms 仍是生产/回滚基线；FACM 4.0 使用 .NET 10 + WinUI 3 并行迁移。Gate 13 前不退休 legacy、不修改 production release controls。

```text
FACM.App (.NET 10 + WinUI 3)
├─ MainWindow: one AppTitleBar + one NavigationView + one Frame
├─ FloatingWindow: narrow F desktop entry
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core (platform/UI neutral)
├─ module lifecycle / performance
├─ Settings 2.0 / UI Text
├─ Product State / observability / diagnostics contracts
├─ Desktop placement geometry
├─ Cleanup
├─ League gameflow/capability/session contracts
└─ Online/update contracts
        ↓
FACM.Infrastructure                 FACM.Platform.Windows
├─ settings/text/diagnostic IO      ├─ executable/runtime identity
├─ diagnostics reader/exporter      ├─ League discovery/session owner
├─ HTTP/League transport            ├─ monitor/work-area/DPI facts
├─ one Gameflow monitor             └─ UAC/hotkey/process integration
└─ update metadata
```

固定 direction：App -> Core/Infrastructure/Platform.Windows；Infrastructure -> Core；Platform.Windows -> Core；Core 不引用 UI/platform implementation。

## 2. UI Intent / State Boundary

```text
Window/Page -> ViewModel -> Core intent/state
                            ↑
            Infrastructure / Platform adapters
            wired only in App composition root
```

禁止 ViewModel/Page 直接 new HttpClient、League runtime、settings/diagnostic file store、Process/Registry/Win32 implementation。页面不得自己维护 LCU polling/state cache。

## 3. Runtime paths / Settings / UI Text

```text
<distribution>/settings.ini             legacy rollback/migration source
<distribution>/settings.v2.json         FACM 4.0 typed settings
<distribution>/ui-text.ini              optional text override
<distribution>/logs/facm4-events.jsonl  bounded diagnostics log
<distribution>/runtime/diagnostics/     sanitized diagnostic bundles
```

稳定路径只从 `Environment.ProcessPath` 的 distribution executable 推导，不使用 single-file self-extract `AppContext.BaseDirectory`。

Settings 2.0 schema v2 覆盖 Environment / Online / Appearance / Pets / League；legacy 15-key import 后旧 INI 仍保留。坏 JSON/非法值/future schema fail closed；atomic save 使用 same-directory temp + flush-to-disk + replace/move。

`IUiTextProvider` 是用户可见文字 contract。Main Shell、F entry、Workbench、Diagnostics Center 都通过 `UiTextKeys`；读取 override 失败时 fallback defaults。

## 4. League ownership / performance

4.0 exactly one discovery/auth/session owner：`WindowsLeagueTransportSessionSource`；`LeagueHttpGateway` read/write 共用它。secret 不进入公共 descriptor/Product State/diagnostics，credential 只发 loopback。

当前 Core write allowlist：

```text
ApplyMySelection      -> PATCH /lol-champ-select/v1/session/my-selection
CreatePerkPage        -> POST  /lol-perks/v1/pages
UpdatePerkPage(id)    -> PUT   /lol-perks/v1/pages/{positive-id}
SetCurrentPerkPage    -> PUT   /lol-perks/v1/currentpage
```

Bench 仍是用户显式手动动作；后续 writer 只能收窄，不能由 UI raw path 扩权。

Gate 8 固定 one Gameflow owner：

```text
WindowsLeagueTransportSessionSource
              ↓
LeagueHttpGateway
              ↓ read
LeagueGameflowMonitor
              ↓
LeagueGameflowPhaseMapper
        ┌─────┴─────┐
        ↓           ↓
ProductStateStore   PerformanceBudgetProvider
        ↓           ↓
        LeagueWorkbenchViewModel
```

cadence：ChampSelect 2s、Matchmaking/ReadyCheck 3s、InGame 10s、connected other 5s、disconnected/connecting/error 10s。Product State 与 Performance 必须来自同一个 mapping。

## 5. Workbench / Main Shell

用户 League IA exactly 3：`比赛 / 攻略 / 自动化`。旧 dashboard/player/live/mayhem/recommendation/efficiency/repair/presence 不直接搬成八个 WinUI tab。

`LeagueWorkbenchViewModel` 只消费 Product State + Performance；禁止 raw `/lol-*` path、HttpClient、Task.Delay polling、session discovery、writer。

Main Shell 固定 one AppTitleBar / one NavigationView / one Frame / four product entries：`清理与修复 / LOL 工作台 / 个性化 / 更多设置`。用户 copy 走 UI Text。禁止 Form-in-Form / WindowsFormsHost / Z-order / timer/reflection patch。

## 6. Product State / structured observability

`ProductStateStore` 是唯一 product-state 聚合 store，覆盖 Application / League / Environment / Services；相同状态不增加 revision，subscriber 在 lock 外调用。它不拥有业务 runtime。

`DiagnosticEvent` 固定 `TimestampUtc / ActionId / Module / DurationMs / Result / Reason / LeagueState / ClientVersion / Data`。Gate 5 factory + bounded JSONL sink 两层 `DiagnosticRedactor`，日志 IO best-effort 且不能阻止启动/退出。

## 7. Gate 9 Diagnostics Center：只读架构

Gate 9 **复用** Gate 5 observability，不建第二日志系统：

```text
ProductStateStore + facm4-events.jsonl + optional .1
                    ↓ read-only bounded
          FileDiagnosticsSnapshotSource
                    ↓
 DiagnosticsSnapshot / DiagnosticsSummaryFormatter
                    ↓ second sanitize
          DiagnosticsBundleExporter
                    ↓
 summary.txt / events.jsonl / manifest.json
                    ↓
        DiagnosticsCenterViewModel
                    ↓
            MainWindow surface
```

固定输入 allowlist：内存 Product State、当前 JSONL、`.1` rotation。禁止 settings、League lockfile、环境变量、Registry、browser cookies、crash dump/raw memory、目录递归。

`DiagnosticsExportSanitizer` 在 Gate 5 redactor 之上再次处理 Basic/Bearer auth、敏感 assignment、Windows/UNC absolute paths；Product State distribution path 也改成 `[path]`。malformed JSONL 只计数并丢弃，不传播原始行。

默认 export policy：500 events；单输入 4 MiB；总输入 8 MiB；ZIP exactly 3 entries；单 entry 4 MiB；bundle 8 MiB；summary 64 Ki chars。

ZIP allowlist exactly：`summary.txt / events.jsonl / manifest.json`。Exporter 使用 temp -> final move；输出目录由 composition root 固定 `<distribution>/runtime/diagnostics`，UI 不能传任意路径。

`DiagnosticsCenterViewModel` 只依赖 `IDiagnosticsSnapshotSource + IDiagnosticsBundleExporter`；不持有 File/Directory/ZipArchive/League writer/Cleanup executor/Updater installer。Clipboard 是 MainWindow 的窄 WinUI 动作。

`scripts/check-facm4-diagnostics.ps1` 自动守只读 ownership、输入/ZIP allowlist、UI boundary 与 exactly-one source/exporter composition。

## 8. Desktop Surface / DPI coordinate boundary

`AnchorPlacementService` 是纯 Core geometry owner：preferred point、负坐标、nearest monitor、edge/corner、margin/clamp、off-screen recovery。

`WindowsDesktopWorkAreaProvider` 提供 physical-pixel work-area + per-monitor DPI facts。64 DIP F surface 先按目标 monitor DPI 转 physical size，再交 Core；`AppWindow.MoveAndResize` 使用同一 physical coordinate space，禁止 UI 第二次缩放。

`FloatingWindow` 只依赖 `IDesktopWorkAreaProvider + IUiTextProvider + EnsureMainWindow callback`。关闭 MainWindow 不关闭 F；F 点击 create-or-activate；关闭 F 才 shutdown。

## 9. Gate 10 DPI / 多屏 / Accessibility boundary

当前 `FACM.App/app.manifest` 已有 `asInvoker + supportedOS`，但 Gate 9 时尚未显式声明 PerMonitorV2。Gate 10 必须把 DPI-awareness 变成明确 contract，并验证与 Gate 7 physical-pixel geometry 一致。

工程可自动验证：

- manifest PerMonitorV2；
- 96/120/144/168/192 DPI 对应 100/125/150/175/200% scale math；
- mixed-DPI synthetic monitor placement、负坐标/左右/上下屏、off-screen recovery；
- actionable WinUI controls 的 keyboard focus + AutomationProperties；
- UI Text 驱动 accessible names/help text；
- semantic theme resources、高对比依赖平台资源；
- text wrapping/无固定高度裁剪等 source rules。

不能由 hosted runner 冒充的证据：真实 mixed-DPI 双屏移动、keyboard-only、High Contrast、125～200% text scaling、basic screen reader、Win10/11 真机视觉/焦点检查。这些若未获得，进入 Gate 12/13 blocker，而不是伪称通过。

## 10. Cleanup / Update / Recovery invariants

Cleanup：validated root -> preview -> explicit confirm -> UAC -> allowlist/reparse guard -> execution-time revalidation -> per-target result。

Updater：size limit、mirror fallback、SHA-256、signature/package validation、validated receipt、wait-exit、独立提升替换、失败保旧版、rollback/recovery；replacement target 来自 distribution EXE。

Gate 11 feature flags/kill switch 只能减少或禁用功能，不能扩大 writer permission。

## 11. Single Instance / Hotkey / PetHost

Single Instance = Ensure Open / Activate。全局快捷键只能 RegisterHotKey，不引入 low-level keyboard hook/GetAsyncKeyState/polling。PetHost 保持独立进程、IPC、parent/job 生命周期。

## 12. 测试与发布边界

持续维护：`FACM Windows Build`、`FACM UI Text Contract`、`FACM 4.0 Foundation`、`FACM.WindowsSmoke`、各 Gate deterministic smoke/source gate。已有 smoke 只能迁移或由更强验证替代。

Hosted Windows runner 是 engineering evidence，不替代 Gate 10/12 真机 multi-monitor/mixed-DPI/accessibility matrix。

Gate 13 前 production `online/version.json` / `release/request.json` 继续指向 3.5.15。正式 4.0 cutover 还需要 settings 真机迁移、Updater rollback、Win10/11、DPI/多屏/accessibility、Defender/SmartScreen 等真实证据。
