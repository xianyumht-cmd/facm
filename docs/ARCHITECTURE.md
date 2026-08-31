# FACM 架构

## 1. 双轨迁移

FACM 3.5.15 WinForms 仍是 production/rollback baseline；FACM 4.0 使用 .NET 10 + WinUI 3。正式 cutover 前不退休 legacy、不修改 production release controls。

```text
FACM.App
├─ MainWindow: one persistent morphing surface host + legacy feature adapter
├─ legacy FloatingWindow / CompactLauncherWindow: diagnostic fallback only
├─ ViewModels: Core intents/state only
└─ composition root
        ↓
FACM.Core
├─ Performance / Product State
├─ Settings 2.0 / UI Text
├─ Observability / Diagnostics
├─ Desktop geometry + DPI
├─ Recovery / Feature policy
├─ Release evidence evaluator
├─ Production cutover decision guard
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

## 2. Runtime ownership

Window/Page -> ViewModel -> Core intent/state。具体 adapter 只在 App composition root 组装。

By default FACM owns one persistent `MainWindow` surface. `FacmSurfaceStateMachine` changes only
the presentation mode (`Orb`, `ControlMatrix`, `FeatureSurface`, `LeagueSurface`,
`ChampSelectStrip`, `HiddenInGame`); ViewModels, Core services and League ownership remain
modular and are not recreated per mode. `FACM_SHELL_EXPERIENCE=legacy` preserves the old
`FloatingWindow` / `CompactLauncherWindow` routing for diagnostics and fallback.

FACM-owned top-level surfaces use one shared outside-click lifecycle implementation where the
legacy product requires desktop-blank dismissal: `DesktopSurfaceOutsideClickWatcher` owns the
physical left-button edge, screen-bounds hit test, opening-click release rule, and disposal for
the active shell. MainWindow activates this watcher only for dismissible expanded modes; idle
`Orb` and `HiddenInGame` stop it and reset its opening-click state, so the 40 ms native sample is
not an idle/hidden UI timer. CompactLauncher activates the same watcher only while that window is
alive. MainWindow feature pages are not separate native windows; their FolderPicker and
ContentDialog flows acquire an explicit suppression scope so a desktop click cannot close the
host while a modal interaction is active. Morphing geometry snaps to its final clamped bounds;
the transition is a bounded opacity/translation effect and does not introduce a resize loop.

Process-wide exactly one：

```text
WindowsLeagueTransportSessionSource
LeagueHttpGateway
LeagueGameflowMonitor
LeagueBenchRuntimeObserver (reuses Gameflow.Observed; no loop)
PerformanceBudgetProvider
ProductStateStore
```

Gate 12 source gate 直接统计 App composition construction count，任何一个不是 exactly 1 都失败。禁止 ViewModel/Page 创建 HttpClient、League runtime、settings/diagnostic/recovery store、Process/Registry/Win32 implementation 或第二 polling loop。

Workbench 只有 `比赛 / 攻略 / 自动化` 三层 IA；Bench 仍手动；writer 只能走 Core capability allowlist。

## 2026-08-30 Morphing Surface presentation contract

The primary shell is a single native `MainWindow`, but Morphing mode must not look like a traditional
MainWindow layout. It morphs between the compact Orb, ControlMatrix, feature/League surfaces,
ChampSelect strip and hidden InGame state. In Morphing mode the NavigationView left pane is closed and
does not reserve a visible navigation column; matrix buttons and compact header controls provide the
navigation affordances. The state machine owns mode transitions and emits `facm.surface.transition`
or `facm.surface.transition-failed` telemetry; it does not own League polling, LCU transport, settings,
pet processes or feature business logic.

The Orb is a 36-DIP custom-vector F anchored through the existing desktop geometry contract. Expansion
uses one-shot anchor calculation with negative-coordinate, multi-monitor and edge-clamp handling,
then a bounded 180 ms opacity/translation presentation. A failed or unavailable geometry read falls
back to the last safe placement and still leaves the shell usable.

The default shell maps desktop entry to ControlMatrix, feature entry to FeatureSurface, League entry
to LeagueSurface, an actionable Bench-backed ChampSelect context to ChampSelectStrip, InGame to
HiddenInGame, and Lobby return to Orb. ChampSelect without actionable Bench candidates stays on the
current safe surface rather than opening an empty strip.
Green Collapse returns any ordinary expanded surface directly to Orb; Back from a feature returns to
ControlMatrix; the red control preserves the established close/shutdown behavior. The Orb is only its
36-DIP F at idle; an information rail is transient, one-shot, and itself activates the same primary
surface action when clicked.

Every presentation request is made on the MainWindow Dispatcher. A valid same-mode request is an
idempotent no-op (with lifecycle activation reconciled); an invalid same-mode presentation is
re-applied. Successful transitions validate AppWindow visibility, target bounds, and visible content
immediately. A failed request records the requested/previous mode, operation, exception type/HResult,
thread, bounds, phase, and correlation id, then restores the last safe presentation or a bounded
36-DIP Orb fallback; it never leaves a visible blank shell.

When entering ChampSelect, the process-level Bench observer consumes the existing Gameflow heartbeat
and performs the small Bench session read even if the League page was not previously selected. Its
runtime snapshot drives ChampSelectStrip, including the Legacy/TeamBuilder bench route, existing
champion metadata/icon reads, and the existing one-shot bench write plus bounded read-back. The
Workbench may still refresh its detailed Live snapshot, but that page lifecycle is not required for
automatic strip activation. The strip adds no LCU owner, Gameflow owner, permanent UI timer, or second
polling loop. Existing heavy feature pages remain an in-window adapter until a later visual-only
migration that observes this contract; explanatory copy is supplied by the thin Inspector where
possible, while safety-critical warnings remain at the action point.

## 2026-08-31 Morphing Surface runtime presentation stabilization

MS9.1 diagnostics proved that the shared failure was the presentation invariant itself: on this
Win10 runtime, the unique Morphing `MainWindow` was clamped to a `136×39` outer AppWindow while
the valid Orb contract requested `36×36`. The Orb XAML content was visible and every failing
diagnostic ran on thread 2 with Dispatcher access; the exception was thrown by
`EnsureSurfacePresentationInvariant`, not by League transport, XAML visibility, activation, or
the dispatcher.

The Windows platform adapter now installs one lifetime-bound HWND subclass for the Morphing
MainWindow and changes only `WM_GETMINMAXINFO.MinTrackSize` to `1×1`; all other messages are first
forwarded to the original window procedure. This is a platform boundary for the one existing
surface host, not a new window owner or a general application lock. The existing
`AppWindow.MoveAndResize` path remains authoritative for every Orb, matrix, feature, League and
ChampSelect geometry change. The resulting real candidate measured `36×36` outer and `30×30`
client bounds.

No MS9 coordinator, retry loop, debounce, or League change was required: the reviewed failure
trace did not prove overlapping or stale presentation requests. Existing same-mode policy remains:
ordinary valid `Orb→Orb` is idempotent, while `EnsureCurrentSurfacePresentation(Orb)` may repair an
invalid presentation. Diagnostic generation/correlation fields record request context but are not
presented as stale-request suppression. Existing outside-click lifecycle ownership remains the
same; the MS8 flood was a consequence of the shared invariant never committing the first Orb, not
a new watcher owner.

## 2026-08-31 Morphing Bench Swap Strip contract

`LeagueBenchSwapStripPolicy` remains the presentation policy over observed Bench facts, while the
process-level `LeagueBenchRuntimeObserver` now owns the compact surface's current context. It reuses
the existing `LeagueGameflowMonitor.Observed` heartbeat and the one shared
`LeagueBenchQuickPickService`; it does not create another Gameflow monitor, session, gateway, timer,
or polling loop. `LeagueBenchRuntimeSnapshot` carries the ChampSelect generation, Bench enabled state,
candidate IDs, latch, and source freshness independently of whether the detailed Workbench page has
ever been opened.

The observer creates one generation when entering ChampSelect, refreshes Bench facts on the existing
heartbeat, and latches only after actionable candidates are observed. Candidate changes update the
same generation in place. A zero-candidate or temporarily unavailable read keeps an already latched
surface alive as a compact waiting strip; leaving ChampSelect clears the latch. The detailed Workbench
still owns its page-level Dashboard/Player/Live refreshes, but it is no longer the sole source for
automatic Compact/Strip presentation.

The strip is the existing `MainWindow` `ChampSelectStrip` mode: a 56-DIP horizontal surface with
44-DIP portrait tiles, content-driven width clamped to 280–600 DIP, and a dedicated `F` drag handle.
There is no normal collapse button for this latched surface. Portrait buttons use the existing
`TrySwapAsync` command and its single POST plus 35/70/140 ms read-back; busy state disables both
presentations, and result text is brief and non-modal. Unknown identity data uses a compact
`Unknown champion` placeholder and never renders a raw `#<id>` primary label.

After a user click completes, the App requests one explicit refresh through the shared Bench runtime
and the existing Workbench ViewModel. This is a user-action reconciliation, not a new timer or
polling owner.

Outside-click, League-client click, candidate click, and simple F-handle click preserve the latched
Strip. Ordinary expanded surfaces retain their existing outside-click dismissal policy. Modal scopes
suppress automatic activation and re-evaluate after the scope closes. InGame takes precedence and
hides the host directly; Lobby restoration returns it to Orb. The App emits low-noise
`league.bench.surface-evaluation` events only when the decision/state signature changes, including
phase, context generation, candidate count, current surface, latch, source owner, and freshness. The
existing MS9 HWND minimum-track-size adaptation, TopMost/hit-test boundary, modal suppression,
anchor persistence, single-instance and tray contracts remain outside this feature's ownership.

## 3. Stable paths

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

Release evidence 是 repository file `evidence/facm4-release-evidence.json`。它不是 runtime 配置，也不是 production pointer。

## 4. Settings / Diagnostics / Recovery

Settings 2.0 strict parser/validator fail closed；legacy INI 正式 cutover 前保留。Primary save 使用 same-directory temp + flush-to-disk + replace/move。

`RecoveringSettings2Repository` 只在 strict load 抛 `InvalidDataException` 后读取 validator-backed LKG；无有效 LKG 返回安全内存默认且 `AutoUpdateEnabled=false`；坏 primary 不自动覆盖。

Diagnostics 只读 Product State + current JSONL + `.1`，再次 scrub secret/Basic/Bearer/Windows/UNC path，ZIP exactly `summary.txt/events.jsonl/manifest.json`。Diagnostics 无业务 writer。

League T1 trace instrumentation remains inside the existing single owners: `LeagueHttpGateway` emits paired request events with correlation/source/phase, timing, status/outcome, endpoint redaction, session invalidation, and in-flight counters; `LeagueGameflowMonitor` emits paired poll events; `LeagueWorkbenchViewModel` emits paired refresh/stage events for Dashboard, Player, Live, and Advisor. The callbacks are best-effort and do not create another transport, polling loop, cache, limiter, or UI thread.

Recovery Core：`Clean / Starting / Running / Failed / Recovering`；bounded atomic state store；previous-start-incomplete 检测。Update recovery 不替代 updater，只约束 validated receipt、old-version preservation、failure keeps old。

## 5. Feature policy：只减权

Feature baseline 是 Core 手写 approved list，不从 enum 自动推导。

```text
EffectiveEnabled = ApprovedBaseline - DisabledKillSwitchSet
```

没有 remote/local enable override。未知字段/capability、坏 JSON/schema、超界、读取异常全部 fail closed。League/Cleanup/Update/Diagnostics gated wrapper 都在底层调用前拒绝 disabled capability。

## 6. Desktop / DPI / Accessibility

Core `AnchorPlacementService` 处理 physical desktop geometry：负坐标、nearest monitor、edge/corner、margin/clamp、off-screen recovery。

Core `DesktopDpi` 是 DPI->scale / DIP->physical pixel 唯一 contract。96/120/144/168/192 DPI 对应 100/125/150/175/200%。Windows adapter 只采集事实；FloatingWindow 不拥有第二套 scale math。

Manifest 固定：`dpiAware=true/pm`、`dpiAwareness=PerMonitorV2, PerMonitor`、execution `asInvoker`。

Main Shell/F/Diagnostics actionable controls 使用 stable AutomationId；Name/HelpText 走 UI Text；主要动作 keyboard-capable；正文 Wrap；semantic colors alias WinUI platform resources。

Synthetic/hosted evidence 不替代真实 mixed-DPI、High Contrast、screen-reader 用户证据。

## 7. Performance contract

Gate 12 将 Performance Contract 从“存在”提升为逐字段 release regression：

```text
Desktop     network4 image2 disk2 cpu2 history20 poll15s  BG/Maint/Visual=true
Client      network3 image2 disk2 cpu2 history12 poll20s  true
Queueing    network2 image1 disk1 cpu1 history4  poll30s  false
ChampSelect network2 image1 disk1 cpu1 history0  poll45s  false
InGame      network1 image1 disk1 cpu1 history0  poll60s  false
Background  network1 image1 disk1 cpu1 history0  poll60s  false
```

`IsNoMoreAggressiveThan` 必须持续证明降级策略不会增加并发/prefetch/visual work，poll interval 不会变得更激进。

Gameflow cadence 固定：ChampSelect 2s；Matchmaking/ReadyCheck 3s；InGame 10s；connected other 5s；NotRunning/Connecting/ClientError 10s。

## 8. Release evidence architecture

Canonical machine-readable matrix：`evidence/facm4-release-evidence.json`。

每项字段：

```text
id
category
requiredForRelease
status = Passed | Blocked | NotRun | Failed
evidence
notes
```

规则：

- `Passed` 必须有 evidence；
- required 非 Passed 必须有 notes；
- ID 唯一；mandatory release evidence 不得移除；
- candidate identity 必须是 full Git SHA + positive artifact id/size + SHA-256 digest；
- JSON 不存可手改的 `releaseReady`。

Core `ReleaseEvidenceEvaluator` 唯一推导：

```text
ReleaseReady = all(required item.status == Passed)
```

因此 CI 可以在 matrix 合法时 SUCCESS，同时正确输出 `RELEASE BLOCKED`。Blocked 不是 CI 失败；伪造/缺失 evidence contract 才是 CI 失败。

Gate 12 已完成并合入 `main@4be7d6c38a8a59c6ff437a1352b8c0c4a5d2a798`。Gate 13 guard implementation head `71d82ea060f393f271048102bc4eff77d0707305` 已通过 Foundation #240、Windows Build #1366、UI Text #487；Gate13Smoke 与 WindowsSmoke 均 SUCCESS。

当前 matrix 为 **22 required / 12 Passed / 10 Blocked**，`ReleaseReady=false`。

## 9. Current external blockers

仍 required 且 Blocked：

- non-admin launch + UAC cancel；
- Defender / SmartScreen；
- Win10 1809 / Win10 22H2 / controlled real-user Win11；
- real 100/125/150/175/200% mixed-DPI multi-monitor；
- keyboard/focus、High Contrast、text scaling、basic screen reader；
- real 3.5.15 -> 4.0 Settings migration/relaunch/rollback；
- interrupted updater replacement/rollback；
- final signing/package verification。

Hosted CI 不得把这些项目改成 Passed。

## 10. Gate 13 production cutover guard

Gate 13 已验证的安全条件是双门：

```text
CutoverAllowed = ReleaseEvidenceEvaluator.ReleaseReady
                 AND FreshProductionDestructiveAuthorization
```

Core `CutoverDecisionService` 的判定顺序固定：**先 release evidence，后 authorization**。只要 required evidence 尚未全部 Passed，任何授权对象都不能覆盖 `ReleaseEvidenceBlocked`。

Production authorization 约束：

- `Granted=true`；
- scope 精确为 `FACM4ProductionCutover`；
- candidate SHA 与 evidence candidate 精确匹配；
- issued 时间不得来自未来；
- 最大 freshness 30 分钟；
- 最大授权窗口 30 分钟；
- 不在 repository/runtime config 中持久化 token/secret/authorization。

Gate13Smoke 已验证 missing/not-granted/wrong-scope/wrong-candidate/future/expired/stale/overlong authorization 全部拒绝；只有 synthetic all-pass evidence + fresh matching authorization 才允许。

`check-facm4-cutover.ps1` 在当前 `ReleaseReady=false` 时同时冻结：

- `online/version.json`；
- `release/request.json`；
- legacy `FACM.sln`；
- `src/FACM`；
- `src/FACM.Updater`；
- `src/FACM.ToolBundle`。

当前状态是 **GUARD VERIFIED / CUTOVER BLOCKED**。普通“继续做工程”不等于 production/destructive authorization。

## 11. Persistent invariants

- Cleanup：preview -> explicit confirm -> UAC -> allowlist/reparse guard -> execution-time revalidation。
- Updater：size/SHA-256/signature/package/validated receipt/wait-exit/separate replacement/failure keeps old/rollback。
- Single Instance = Ensure Open / Activate。
- Hotkey = RegisterHotKey；不使用 low-level hook/GetAsyncKeyState polling。
- PetHost 独立进程。
- Performance/UI Text/deterministic smoke/source gates 不得静默删除。

## 12. P7 candidate desktop-pet runtime split

FACM 4.0 keeps the two desktop-pet families in separate runtime and payload ownership boundaries:

```text
FlyingSprite -> WindowsFlyingPetRuntime -> WindowsFlyingHostBundleStore -> FACM.FlyingHost
VPetCore     -> WindowsVPetRuntime     -> WindowsPetHostBundleStore     -> FACM.PetHost
```

`FACM.FlyingHost` has no VPet package/cache ownership and `FACM.PetHost` has no FlyingSprite ownership. The router serializes transitions as: clear active -> stop the non-target runtime -> set the target active -> start the target runtime. Each payload preparation, process start, pipe connect, activate/reset/stop write, readiness wait and process exit has a bounded timeout; a deferred prepare gate prevents a timed-out non-cooperative worker from spawning a second extraction worker.

The host projects use .NET 10-compatible `ApplicationHighDpiMode=PerMonitorV2` for their WPF/WinForms analyzer contract. DPI nodes are not duplicated in the manifests. FlyingHost carries its own `FACM.FlyingHost.app` assembly identity; PetHost remains `FACM.PetHost.app`.

Batch P closes the shared host lifecycle contract without merging the two runtime families: the client command write uses a cancellation-aware `WriteLineAsync`/`FlushAsync` pair with a 750 ms command budget; a timed-out transport is marked poisoned, detached, disposed and followed by bounded wait/kill/wait/dispose cleanup. A poisoned transport never receives a second graceful `stop` write. Both WPF hosts keep their dispatcher alive without calling `Show()` during process startup; only the dispatched `activate` command may emit `show`, after which `Loaded` emits `loaded` and the host emits `ready`. The server enters its command reader after pipe connection and does not pre-send a `connected` event. Runtime stage diagnostics carry the stage, generation, PID, pipe name, command and elapsed time as separate App diagnostic fields.

## 2026-08-30 Live League reliability boundaries

`LeagueHttpGateway` remains the single authenticated LCU transport. It may read the current `LeagueGameflowMonitor` snapshot through an optional provider solely to classify known 404 responses; it does not poll, cache a second phase, or create another client. The gateway exposes only bounded in-flight counters for the read-only Diagnostics Runtime Snapshot.

`LeagueGameflowMonitor` remains the single polling owner. Changed/Observed subscribers are isolated individually so one UI or automation observer cannot terminate the loop or suppress healthy observers. Workbench property notifications likewise isolate subscriber faults, while `MainWindow` marshals all navigation/surface reads to its Dispatcher before touching WinUI objects.

`App` owns the lifecycle diagnostics handlers and emits sanitized lifecycle/exception events. Diagnostics Center merges a point-in-time runtime-facts provider into its existing snapshot; it does not own League state. Matchmaking automation keeps the existing one-shot ReadyCheck write boundary and now reports evaluation/read/write outcomes through the existing diagnostic sink.

## 2026-08-31 BOOT-1 bootstrap and modular data-root topology

BOOT-1 adds a native startup boundary without moving existing product ownership:

```text
FACM.exe (native Win32 bootstrapper)
  -> .facm/state/active.json
  -> .facm/versions/<version>/FACM.App.exe
  -> FACM.App (WinUI 3 / managed product)
       -> FACM_ROOT + FACM_DATA_ROOT
       -> Core / Infrastructure / Platform.Windows
```

The bootstrapper owns only active-version resolution, bounded path validation, local state/manifest/pack
operations, startup correlation and child-process creation. It does not own League session, Gameflow,
settings semantics, UI shell, desktop-pet behavior, or network update policy. `active.json` is the minimal
schemaVersion/activeVersion/activePath/previousVersion/lastSuccessfulLaunch record and is atomically replaced.
An install or staging failure leaves the previous active version intact.

The app-local Core uses `facm-core-win-x64`; optional desktop-pet components are identified separately as
`facm-pet-pethost-win-x64` and `facm-pet-flying-win-x64`. `IComponentAvailability` is a read-only boundary,
not a package manager. If a selected optional pet component is unavailable, the runtime stops/restores the
launcher and emits a sanitized unavailable-component result; it does not create a second host or rewrite the
user's persisted enabled preference to `Off`.

Legacy single-file publication remains available and keeps its existing embedded-pet default. BOOT-1's
`BootCore.pubxml` explicitly disables embedded pet payloads and publishes a multi-file self-contained Core.
The current local prototype can produce/verify a ZIP component pack and can provision an expanded local source
tree into staging; native ZIP extraction and network provisioning are intentionally outside this stage.
