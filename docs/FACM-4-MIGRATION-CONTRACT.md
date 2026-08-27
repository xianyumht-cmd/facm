# FACM 4.0 Migration Contract

> Status: Gate 0 baseline
> Production baseline: FACM 3.5.15
> Gate 0 branch base: `main@1f7d5d5f9e4a16daac68673d8ce387241af4417d`
> Tracking: #185

## 1. Gate 0 purpose

FACM 4.0 is a platform and shell migration, not a UI reskin. Gate 0 freezes the behavior and safety contracts that already work in FACM 3.5.15, decides ownership boundaries for the new codebase, and proves the deployment model before any production feature is moved.

Gate 0 MUST NOT:

- modify `online/version.json` or `release/request.json`;
- publish a 4.0 build to the production update channel;
- delete the existing WinForms implementation;
- rewrite stable League automation just to make it look more modern;
- re-open the legacy Fix-LCU-Window path that 3.5.15 already replaced;
- use a UI rewrite as an excuse to widen LCU write permissions.

## 2. Verified technology baseline (2026-08-27)

### .NET

- Target runtime for the new shell: **.NET 10 LTS**.
- Current servicing baseline at Gate 0: **10.0.11** (released 2026-08-11).
- .NET 10 support ends **2028-11-14**.
- FACM remains Windows-only.

Official references:

- https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.11.md
- https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md

### Windows App SDK / WinUI 3

- UI framework: **WinUI 3**.
- Current stable Windows App SDK at Gate 0: **2.4.0** (released 2026-08-13).
- Windows App SDK supports Windows 10 version 1809 / build 17763 or later.
- FACM 4.0 will initially publish **x64** only. x86/Arm64 are not Gate 1 requirements.

Official references:

- https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
- https://learn.microsoft.com/windows/apps/windows-app-sdk/support
- https://learn.microsoft.com/windows/apps/get-started/versioning-overview

### Single-file semantics

Microsoft supports `PublishSingleFile` for **unpackaged + self-contained** WinUI 3 apps. This is a single distributable EXE, but it is **not** a zero-extraction binary: Windows App SDK/.NET content is extracted to a temporary directory on first launch.

Required deployment-probe properties include:

- `WindowsPackageType=None`
- `WindowsAppSDKSelfContained=true`
- `SelfContained=true`
- `EnableMsixTooling=true`
- `IncludeAllContentForSelfExtract=true`
- `PublishSingleFile=true`

Official reference:

- https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app

**Gate 0 decision:** single-file is acceptable as the primary Gate 1 prototype because FACM users still receive one EXE. The architecture MUST NOT depend on the temporary extraction directory being stable. If first-launch extraction, Defender behavior, startup latency, or updater replacement proves unreliable on real machines, the approved fallback is a **single installer EXE that installs a self-contained folder payload**. MSIX is not the default because FACM needs portable-style behavior, custom updater compatibility, UAC elevation flows, embedded tools, and existing data-path compatibility.

## 3. Frozen FACM 3.5.15 behavior contract

The following are migration invariants. A new shell is not allowed to weaken them.

### 3.1 League ownership and permissions

- Exactly one League discovery/auth/session owner per FACM process.
- Feature modules consume that session; they do not create competing discovery loops or auth stacks.
- Existing narrow writer boundaries remain narrow. Gate 2 through Gate 7 writers, Bench swap, Matchmaking, PostGame, Presence, and Client UX Repair must not be replaced by a generic arbitrary LCU writer.
- Optional Tencent/LCU fields stay optional; they cannot become hard gates for unrelated actions.
- Bench quick pick remains an explicit user action. FACM does not become an automatic champion-stealing tool.
- `RegisterHotKey` remains the hotkey model. No low-level keyboard hook and no polling loop is introduced by the migration.

### 3.2 League performance

`docs/PERFORMANCE-CONTRACT.md` remains normative:

- no network, large file scan, bulk image decode, or heavy statistics on the UI thread;
- cancel stale work;
- finite timeout/budget for external requests;
- bounded concurrency;
- In Game and Champ Select always receive the most conservative background budget;
- deterministic `--performance-contract-test` coverage must be migrated or replaced by demonstrably equivalent/stronger tests before legacy removal.

### 3.3 Mayhem / OP.GG

Preserve:

- resilient source fallback;
- header timeout plus cancellable response-body reads;
- bounded network/image/decode work;
- cache reuse;
- offline fixtures for deterministic CI;
- live third-party probes remain outside the core deterministic build gate.

### 3.4 Game Repair

Preserve the 3.5.15 native repair model:

- native Win32 window operations;
- monitor-aware positioning, including negative coordinates;
- reasonable-size restoration rather than blind hard-coded geometry;
- WinEvent event-driven repair with debounce/cooldown rather than permanent polling;
- existing narrow restart-UX writer;
- play-again behavior reuses its existing writer.

The retired external Fix-LCU-Window mode pipeline is not a migration target.

### 3.5 Cleanup

Preserve:

- preview before destructive action;
- explicit elevation boundary;
- configured path allowlists;
- reparse-point/junction safety;
- cancellation and bounded scan behavior;
- failure must leave unrelated files untouched.

### 3.6 Online update

Preserve:

- multiple download candidates/fallback routing;
- maximum package size checks;
- SHA-256 verification before installation;
- signature/package validation;
- a validated-download receipt that prevents swapping the file between download and install;
- elevated replacement in a separate process;
- failure keeps the old executable runnable.

The 4.0 updater may be reimplemented, but these semantics are mandatory.

### 3.7 App lifecycle

Preserve:

- single instance means **Ensure Open / activate existing instance**, never toggle-close;
- startup and module initialization are ordered and measured;
- module initialization failure unwinds initialized modules in reverse order;
- background child processes are owned and cleaned up deliberately.

### 3.8 Settings and user text

Preserve compatibility with existing user state:

- current `settings.ini` keys must remain readable during migration;
- existing theme/pet/hotkey/automation settings must not silently reset;
- `ui-text.ini` stable TextKey behavior remains the user-facing text contract;
- new WinUI pages need a WinUI text adapter, not hard-coded strings that bypass the contract.

### 3.9 PetHost

PetHost remains a separate process at Gate 1 unless evidence proves that merging it improves reliability without increasing UI-thread/startup cost.

Preserve:

- x64 self-contained deployment;
- non-UI-thread extraction/start/IPC from the main app;
- explicit lifecycle ownership (parent PID / job semantics);
- versioned embedded bundle and asset validation behavior.

## 4. Migration inventory

Legend:

- **KEEP** — behavior/API contract stays and code can initially be carried with small compatibility changes.
- **EXTRACT** — stable logic should move behind a framework-neutral or Windows-platform interface before the old UI is removed.
- **REWRITE** — implementation is tied to WinForms/GDI or the old deployment model; preserve behavior, replace implementation.
- **DELETE-LATER** — remove only after the replacement passes the relevant Gate and legacy rollback is no longer required.
- **DEFER** — intentionally not in the first shell migration.

| Current area | Gate 0 class | FACM 4 owner | Contract / action |
| --- | --- | --- | --- |
| `Application/FacmHost*`, `IFacmModule` | EXTRACT | `FACM.Core` | Preserve dependency graph, deterministic init order, timing, reverse disposal. Remove WinForms dependencies from module contracts. |
| `Application/Modules/*` | EXTRACT/REWRITE | Core + App adapters | Keep capability ownership; rewrite modules whose only job is Form creation/navigation. |
| `MainForm`, `CompactMenuForm`, WinForms Forms/Pickers | REWRITE | `FACM.App` | Rebuild shell/pages in WinUI 3. Do not port layout hacks/reflection fixes. |
| `Theming/FacmDesignSystem`, WinForms/GDI chrome/skin | REWRITE | `FACM.App` | Preserve theme IDs/preferences where useful; replace rendering with WinUI resources/tokens and native title-bar APIs. |
| `UiTextKeys`, `UiTextCatalog`, config semantics | EXTRACT | `FACM.Core` | Stable keys/defaults/config merge remain. Add WinUI binding/adapter in App. |
| `Services/AppSettings` | EXTRACT | `FACM.Core` + Infrastructure | Keep `settings.ini` read compatibility and migration behavior. Replace UI-specific catalog coupling with value normalization services. |
| `RuntimePaths`, `PortablePaths`, logging | EXTRACT | Infrastructure / Platform.Windows | Keep portable data layout where compatible; no dependence on WinUI single-file temp extraction directory. |
| League discovery/session/runtime | EXTRACT | `FACM.Core` + `FACM.Platform.Windows` | One owner only. WMI/process/lockfile/Windows discovery belongs to Platform.Windows; session/state contracts belong to Core. |
| League read services/models/cache | EXTRACT | `FACM.Core` / Infrastructure | Preserve cancellation/timeouts/caches; no WinUI dependency. |
| League Gate 2-7 writers and narrow writer runtimes | KEEP/EXTRACT | `FACM.Core` | Preserve allowlists and per-capability writers. No generic write client. |
| League Forms/UI bridges | REWRITE | `FACM.App` | Convert to view models/presenters plus WinUI pages; business services remain below App. |
| `LeagueNativeHotkeyService` | EXTRACT | `FACM.Platform.Windows` | Keep RegisterHotKey route and lifecycle cleanup. |
| `LeagueGameRepairService` Win32 work | EXTRACT | `FACM.Platform.Windows` | Keep native monitor/window/event logic; WinUI only hosts command/status UX. |
| Mayhem services, parsers, cache | EXTRACT | `FACM.Core` / Infrastructure | Preserve offline smoke fixtures and network budgets. Renderer/UI gets rewritten. |
| Mayhem GDI card rendering / WinForms lookup | REWRITE/DEFER | `FACM.App` | Data path first; visual redesign after shell foundation is stable. |
| Cleanup engine/profile | EXTRACT | `FACM.Core` + Platform.Windows | Core plans/reviews actions; Windows layer owns filesystem/elevation implementation. |
| Cleanup WinForms review/repair dialogs | REWRITE | `FACM.App` | Preserve preview/confirmation semantics. |
| Online manifest/service/mirror routing | EXTRACT | Infrastructure | Preserve manifest compatibility and fallback behavior. |
| `UpdateInstaller` download/verification | EXTRACT | Infrastructure | Keep validation semantics; separate package acquisition from Windows replacement. |
| `FACM.Updater` net48 helper | REWRITE | `FACM.Updater` net10 or minimal Windows helper | Keep elevated wait/replace/rollback semantics. Must understand WinUI single-file distribution path, not extraction path. |
| `FACM.ToolBundle` assembly wrapper | REWRITE then DELETE-LATER | build/resource pipeline | Keep trusted payload manifest/hash and extraction validation; assembly wrapper can disappear after new resource packaging is proven. |
| embedded `clean driver.exe` | KEEP | resource payload | Preserve exact hash/validation and explicit user action/elevation boundaries. |
| legacy Fix-LCU embedded parts / safe modes | DELETE-LATER | none | Already retired from formal ToolBundle; delete only in a cleanup gate after references/rollback needs are gone. |
| PetHost | KEEP/DEFER | separate `FACM.PetHost` | Do not force WinUI migration. Upgrade target framework separately only when tested. |
| built-in WinForms/GDI pets | DEFER/REWRITE | `FACM.App` or PetHost | Not required to prove Gate 1 shell. Preserve user setting compatibility. |
| `PerformanceBudget` + smoke | EXTRACT | `FACM.Core` | Remains a first-class contract. |
| deterministic smoke tests embedded in FACM.exe | EXTRACT/REWRITE | test projects / probe runners | Migrate out of production EXE progressively; no coverage deletion. |
| `.github/workflows/build.yml` | KEEP then REWRITE | CI | Keep 3.5.x pipeline unchanged while a parallel 4.0 pipeline proves itself. |
| `publish-release.yml`, online management | KEEP | release ops | No 4.0 use until release gate explicitly authorizes cutover. |

## 5. Target project boundaries

Gate 1 SHOULD create these projects in parallel with the legacy solution. Names may be adjusted only if dependency direction remains the same.

### `FACM.App` — .NET 10 + WinUI 3

Owns:

- windowing and navigation;
- WinUI resources/theme tokens;
- view models/presentation adapters;
- title bar / visual states;
- user interaction and dialogs.

Must not own:

- raw LCU auth/discovery;
- arbitrary LCU writes;
- cleanup filesystem implementation;
- updater package verification/replacement;
- heavy network/data work.

### `FACM.Core` — .NET 10

Owns:

- capability contracts and module graph;
- settings model/migration contracts;
- TextKey catalog semantics;
- performance budget;
- League domain/read/write capability interfaces;
- cleanup plans/results;
- update manifest/validation models that do not require Win32.

`FACM.Core` must not reference WinUI, WinForms, WPF, or GDI.

### `FACM.Infrastructure` — .NET 10

Owns:

- HTTP transports;
- external API/cache persistence;
- update download/mirror routing;
- config/file persistence implementations that are not privileged Win32 operations.

It may reference Core. Core must not reference Infrastructure.

### `FACM.Platform.Windows` — .NET 10 Windows

Owns:

- Win32 window/monitor APIs;
- process/WMI/lockfile discovery;
- single-instance activation plumbing;
- RegisterHotKey;
- elevation/restart;
- filesystem safety primitives where Windows semantics matter;
- child-process/job ownership;
- updater replacement integration.

It may reference Core. Core must not reference Platform.Windows.

### `FACM.PetHost`

Remains separate. FACM.App talks to it through an explicit client/IPC boundary.

### `FACM.Updater`

Remains a separate replacement process. Reimplementation may target .NET 10 self-contained or a smaller Windows-native helper, but the updater must stay independently executable so it can wait for and replace FACM.App safely.

## 6. Dependency rule

Allowed direction:

`FACM.App -> FACM.Core`

`FACM.App -> FACM.Infrastructure`

`FACM.App -> FACM.Platform.Windows`

`FACM.Infrastructure -> FACM.Core`

`FACM.Platform.Windows -> FACM.Core`

`FACM.PetHost` and `FACM.Updater` are process boundaries, not UI libraries.

Forbidden:

- Core referencing WinUI/WinForms/WPF;
- feature modules creating their own League auth/session stack;
- App calling broad LCU write transport directly;
- Core depending on a single-file extraction path;
- Updater overwriting files before hash/signature/receipt validation succeeds.

## 7. Deployment contract

Gate 1 prototype default:

- Windows x64;
- .NET 10 self-contained;
- WinUI 3 / Windows App SDK 2.4.0;
- unpackaged;
- `PublishSingleFile=true`;
- all WinUI/WASDK runtime content included for self-extraction;
- production data/config paths remain outside extraction temp;
- embedded FACM-owned resources must be addressable through assembly/resource APIs, not relative temp paths.

The final production packaging decision is gated on evidence, not aesthetics.

### Single-file acceptance criteria

A candidate may advance only if CI/Windows evidence confirms:

1. publish produces one distributable application EXE (diagnostic files may be separate CI artifacts, not runtime requirements);
2. first launch succeeds on a clean supported Windows image without a preinstalled Windows App Runtime;
3. second launch is not materially slower than the frozen budget;
4. `Environment.ProcessPath`/replacement target resolves to the distributable application path, while runtime extraction paths are treated as ephemeral;
5. embedded marker resources load correctly;
6. elevation relaunch can target the distributable EXE;
7. Defender/SmartScreen observations are documented before release cutover.

### Fallback line

If single-file self-extraction fails reliability/performance/security review, Gate 1 does **not** fall back to legacy WinForms. It falls back to:

> one signed installer EXE -> self-contained installed folder payload -> same WinUI/Core/Platform architecture.

That preserves the 4.0 architecture while changing only distribution.

## 8. Updater migration constraints

The current updater contract already separates package download/verification from elevated replacement. 4.0 keeps that shape.

Gate 1/2 updater work must prove:

- the downloaded FACM package hash matches the online manifest;
- signature/package policy passes;
- the replacement helper receives a one-time validated receipt/hash;
- helper waits for the old process;
- helper replaces the **distribution EXE**, never a Windows App SDK temp extraction artifact;
- failed replacement restores or retains the last runnable version;
- app relaunch happens only after replacement is complete;
- updating does not require PowerShell/cmd;
- 3.5.15 remains a rollback artifact until 4.0 release gates close.

## 9. Acceptance matrix

| Contract | Deterministic evidence before legacy removal | Real-Windows evidence before release |
| --- | --- | --- |
| .NET 10 / WinUI build | restore/build/publish CI | launch on supported Win10 + Win11 |
| single-file packaging | publish output inspection + probe JSON | first/second launch + temp extraction observation |
| embedded resources | probe loads marker and records success | launch from arbitrary writable/read-only parent locations where supported |
| app lifecycle | single-instance unit/integration replacement tests | duplicate launch activates existing window |
| settings | legacy fixture round-trip tests | upgrade a copy of real 3.5.15 settings without reset |
| UI text | TextKey catalog/config tests + new WinUI lint/analyzer gate | edit config, hot reload/relaunch verification |
| League session | unique-owner tests + existing smoke migration | attach/detach/restart League repeatedly |
| narrow writers | endpoint allowlist tests per capability | explicit user actions in supported client states |
| hotkeys | registration/lifecycle tests | real key conflicts, minimize/background behavior |
| Game Repair | monitor/math/event unit tests | multi-monitor incl. negative coordinates + client restart |
| Cleanup | path/reparse/elevation-plan fixtures | preview + elevated run on test tree |
| Mayhem/OP.GG | offline fixtures, cancellation and budget tests | live optional probe outside core CI |
| performance | migrated Performance Contract tests | FPS/1% low/CPU/GPU/memory on baseline game machine |
| updater | hash/receipt/rollback replacement sandbox tests | signed old->new and interrupted-update scenarios |
| PetHost | current self-test/bundle tests retained | first use, restart, parent exit, IPC failure recovery |

## 10. Gate boundaries

### Gate 0 exit

Gate 0 is complete when:

- this migration contract is reviewed against the repository inventory;
- a parallel deployment probe builds/publishes in Windows CI;
- probe evidence records actual output shape and self-contained/single-file configuration;
- 3.5.15 production online/release state is unchanged;
- remaining unknowns are explicitly assigned to Gate 1 evidence rather than hidden.

### Gate 1 start

Gate 1 may then create the parallel .NET 10 solution/projects and the minimal WinUI shell. It must not delete or replace the 3.5.15 project tree.

### Rollback line

Until a later release gate explicitly changes this rule:

- `main` 3.5.15 behavior is the production rollback baseline;
- FACM 4.0 lives on short-lived migration branches/PRs;
- no Gate 0/1 artifact is referenced by `online/version.json`;
- a failed 4.0 experiment is reverted by dropping the new parallel projects/probes, not by modifying the production updater to point users at an unverified build.
