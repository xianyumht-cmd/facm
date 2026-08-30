# FACM 3.5.15 -> 4.0 Full Product Parity Specification Audit

Status: **BATCH-W-COMPLETE / BATCH-X-IN-PROGRESS / IMPLEMENTATION-BACKLOG-RECORDED**

Audit date: 2026-08-30

Production baseline: `908d5782e6eb5b30fee0e4d5794c312d70ac0e36` (FACM 3.5.15)

Audited candidate: `0eebe940b26edb3b4900587e54ff2f3b685c224a` (Batch X outside-click slice; Batch U included)

Formal P7: `9744af848e4b888c1876e76e2cbf0c06d5c526bf` (unchanged)
PR #234: Draft / open / unmerged

This is a behavior and product-surface audit, not a UI 2.0 redesign and not permission to
merge, publish, run Gate13, change the production pointers, or retire the legacy line.

## UI-UPGRADE-FROZEN-CONTRACT

The following behavior is frozen while the UI Upgrade is designed; the UI Upgrade must not
change these contracts.

1. **Tray:** Keep a resident `NotifyIcon`; single left-click keeps Windows/default behavior,
   double-click toggles or opens CompactLauncher, and right-click opens the FACM tray menu.
2. **Floating F:** Keep the F entry available as the product entry, preserve its click/drag
   distinction, and persist and recover its position as already specified.
3. **CompactLauncher:** Keep its existing entry routing, primary categories, outside-click
   close timing, and modal suppression behavior.
4. **MainWindow:** Keep the existing product routes, action semantics, lifecycle, and
   single-instance activation target.
5. **Outside-click:** A physical left click released outside an open FACM surface closes that
   surface; the opening click is not reused, and protected modal/picker work is not closed.
6. **Modal suppression:** Content dialogs and system pickers retain ownership until they finish;
   outside-click handling must not dismiss or interfere with them.
7. **Single-instance:** A second FACM launch activates the existing instance and must not create
   another FACM shell or duplicate runtime ownership.
8. **Desktop Pet:** Pet replacement remains single-owner and lifecycle-safe: switching or
   disabling pets must not leave duplicate visible pets, hosts, or stale runtime ownership.
9. **Settings persistence:** Existing legacy settings compatibility and Settings2 atomic,
   validated, recoverable persistence must remain intact.
10. **Update flow:** Update check, announcement, download, validation, replacement, rollback,
    and safe failure behavior remain unchanged.
11. **League session/discovery:** Lockfile/process discovery, LCU session ownership,
    authentication containment, and safe no-session behavior remain unchanged.
12. **League polling/cache contracts:** Shared gateway ownership, bounded polling, cache and
    invalidation rules, cancellation, concurrency limits, and Workbench responsiveness remain
    unchanged.

## UI Upgrade surface classification

### A. SAFE TO REDESIGN NOW

- **Logs** — presentation may change while log opening, path ownership, and sanitized output
  behavior remain unchanged.

### B. VISUAL-ONLY, BEHAVIOR FROZEN

- **Repair** — layout and presentation may change; repair actions, confirmation, privilege
  boundaries, and failure handling stay unchanged.
- **Cleanup** — layout and presentation may change; scan, whitelist, confirmation, UAC, and
  execution-time revalidation stay unchanged.
- **Settings** — layout and presentation may change; legacy/Settings2 persistence and recovery
  contracts stay unchanged.
- **Maintenance** — layout and presentation may change; update, announcement, log, and safe
  failure flows stay unchanged.

### C. BLOCKED UNTIL BEHAVIOR FIX/REAL-MACHINE VALIDATION

- **Tray** — real supported-Windows interaction and clean lifecycle evidence remain open.
- **Personalization** — picker, theme/pet selection, persistence, and close-path parity remain
  open for validation.
- **Pet Picker** — replacement and single-visible-pet behavior require the real multi-switch
  sequence before redesign.
- **CompactLauncher** — outside-click, modal suppression, and visible entry ownership remain
  behavior-frozen and require acceptance.
- **MainWindow Shell** — outside-click coverage for every owned surface and visible shell
  lifecycle remain open.
- **League Workbench** — click-by-click phase traces and responsiveness evidence are required
  before changing its surface.
- **Player** — real data shape, loading/error states, and session behavior remain open.
- **Live** — real Champ Select/current-game data and read-only behavior remain open.
- **Advisor** — real League context, provider/cache behavior, and unsupported/in-game rules
  remain open.

### D. 4.0-ONLY, FREE DESIGN WITH EXISTING SAFETY CONTRACTS

- **Diagnostics** — it has no 3.5.15 visual parity target and may be redesigned subject to
  existing redaction, bounded export, and credential/path-safety contracts.

## Tray single-left parity correction

The immutable FACM 3.5.15 source and the current 4.0 source both have no custom
`NotifyIcon.Click`, `NotifyIcon.MouseClick`, or `MouseButtons.Left` action for the tray icon.
The 3.5.15 contract is therefore: single left-click preserves Windows/default NotifyIcon
behavior; double-click calls `ToggleMenu()`; right-click uses `ContextMenuStrip`. The current
4.0 contract is equivalent: single left-click has no FACM action; double-click dispatches
`OpenCompactLauncher`; right-click uses the tray menu. Only this row is reclassified by this
correction; all other matrix statuses are unchanged.

## Audit method and scope

The baseline was inspected directly from the immutable 3.5.15 commit with `git show` and
`git grep`; the candidate was inspected from the current worktree and its active composition.
The audited source inventory covers:

- 210 baseline FACM C# files across `src/FACM`, `src/FACM.PetHost`, `src/FACM.Updater`, and
  `src/FACM.ToolBundle`;
- the active 4.0 layers under `src/FACM.App`, `src/FACM.Core`, `src/FACM.Infrastructure`,
  `src/FACM.Platform.Windows`, `src/FACM.WindowsSmoke`, `src/FACM.FlyingHost`, and
  `src/FACM.PetHost`;
- the retained `src/FACM` / Updater / ToolBundle compatibility line. Its source presence is
  recorded as compatibility evidence only; it is not treated as an active 4.0 UI path;
- the complete 3.5.15 shell, forms, dialogs, menus, tray, timers, mouse/keyboard handlers,
  settings, theme, pet, cleanup, repair, online, Mayhem, League, process, updater, and
  single-instance surfaces named in the Full Product Parity plan.

The candidate has 168 C# files in the new active layers and 420 FACM-related C# files when
the retained legacy/host/helper lines are included. The architecture is intentionally not a
one-file-to-one-file port. The matrix below therefore classifies the user-visible contract,
the active 4.0 owner, and evidence rather than counting source files as features.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| `EXACT` | The 4.0 owner and contract are present, and the available deterministic/source evidence shows the 3.5.15 behavior is preserved. Remaining evidence, if any, is listed separately. |
| `PARTIAL` | A corresponding 4.0 path exists, but part of the 3.5.15 contract, surface, compatibility behavior, or acceptance evidence is not yet closed. |
| `MISSING` | The audited active 4.0 path does not currently provide the required behavior. |
| `4.0-ONLY` | A new 4.0 capability with no direct 3.5.15 baseline. It must remain additive and must not weaken parity behavior. |
| `NEEDS-REAL-MACHINE` | Source and/or deterministic checks are insufficient to conclude product behavior on a real supported Windows/League environment. |
| `FAIL` | The current 4.0 behavior explicitly contradicts the 3.5.15 contract. |

## Full product behavior matrix

| # | 3.5.15 product contract | Active 4.0 owner | Status | Evidence / remaining gap |
| ---: | --- | --- | --- | --- |
| 1 | Startup initialization, fail-soft startup diagnostics, and normal message-loop ownership | `FACM.App/App.xaml.cs`, `StartupCrashDiagnostics.cs` | `EXACT` | Foundation/Windows smoke and x64 rebuild pass; startup must still be exercised on the final target machine. |
| 2 | One FACM process; a second launch activates the existing instance instead of creating another shell | `WindowsSingleInstanceGate`, `App.xaml.cs`, `RequestExternalActivation` | `EXACT` | Single-instance source/pressure checks pass; real second launch remains part of final acceptance. |
| 3 | Runtime tray icon with default single-left behavior, double-click activation, right-click commands, and clean disposal | `WindowsTrayHost`, `App.Tray.cs`, `TrayCommandRouting` | `EXACT` | 3.5.15 and current 4.0 both bind no custom tray single-left action; both retain resident icon, double-click activation, and right-click menu ownership. Real icon visibility/interaction remains a separate machine-evidence concern. |
| 4 | Default F/floating entry is visible, clickable, and acts as the product entry | `FloatingWindow`, `WindowsFloatingSurfacePlatform` | `NEEDS-REAL-MACHINE` | Pointer routing and desktop source gates pass; the final Win10 visible interaction is not yet closed. |
| 5 | Drag F, persist its desktop position, recover off-screen placement, and reset position | `FloatingWindow`, `AnchorPlacement`, Settings2 narrow update | `EXACT` | Desktop placement and settings source checks pass; mixed-DPI and multi-monitor evidence remain Gate13 items. |
| 6 | Compact launcher exposes the same primary product categories and can reach detailed control center | `CompactLauncherWindow`, `MainWindow`, `App.xaml.cs` | `PARTIAL` | Functional routes exist, but 3.5.15's exact compact card/menu grouping and visual states still need the X/Y audit. |
| 7 | Compact launcher closes on the next physical left click outside it, without closing on the opening click or during modal work | `DesktopSurfaceOutsideClickWatcher`, `CompactLauncherWindow` | `EXACT` | Batch R deterministic state coverage and the shared watcher source gate pass. |
| 8 | Every FACM-owned open surface closes on a desktop-blank outside click, while modal dialogs and pickers are protected | `DesktopSurfaceOutsideClickWatcher`, `MainWindow`, `MaintenanceSettingsControl` | `PARTIAL` | Batch X now gives MainWindow and CompactLauncher one lifecycle-safe physical left-click owner; cleanup, League, maintenance, FolderPicker, and ContentDialog flows hold explicit suppression scopes. Real visible Win10 interaction and any additional top-level FACM surface remain to be accepted. |
| 9 | F/pet left-click opens FACM; right-click reaches the tray/context commands; drag does not accidentally activate | `FloatingWindow` pointer handlers, `WindowsVPetRuntime`, `WindowsFlyingPetRuntime` | `PARTIAL` | Batch S/P/U preserve the routing and IPC contracts; multi-process visible click behavior still needs real retest. |
| 10 | 3.5.15 UI text catalog, named keys, replacement section, and hover descriptions remain effective | `IUiTextProvider`, `FileUiTextProvider`, `UiTextContracts`, XAML text assignment | `PARTIAL` | Baseline exposes 198 named UI keys. Active 4.0 exposes 120 renamed role-scoped keys and does not yet map the old role-specific names; old `ui-text.ini` customizations can therefore be ignored by new surfaces. |
| 11 | Legacy `settings.ini` remains readable/writable with the exact 15 ordered production keys | `LegacySettingsCodec`, `LegacySettingsContract`, `IniSettingsRepository` | `EXACT` | Ordered 15-key contract and settings parity source checks pass; no legacy file rewrite is allowed during migration. |
| 12 | N/A in 3.5.15: strict Settings2 schema, LKG recovery, atomic narrow mutation, and fail-closed validation | `Settings2`, `Settings2Repository`, `RecoveringSettings2Repository` | `4.0-ONLY` | Additive 4.0 capability; deterministic migration/atomic/recovery checks pass. Real 3.5.15-to-4.0 migration remains unverified. |
| 13 | Theme catalog, panel theme selection, desktop form, theme persistence, and runtime application | `FacmThemeRuntime`, `PersonalizationCatalog`, `WinUiThemeRuntime`, `MainWindow` | `PARTIAL` | Theme ownership/source gates pass; exact visual state and every picker/close path still require Y plus real-machine checks. |
| 14 | Pet catalog, picker, descriptions, current-selection state, and legacy pet IDs | `PersonalizationCatalog`, `PersonalizationViewModel`, `MainWindow.Personalization` | `PARTIAL` | Current IDs retain the supported flying/VPet and legacy compatibility set; text/key mapping and full picker visual parity remain open. |
| 15 | Switching pets replaces the previous runtime and leaves one visible desktop pet/entry | `WindowsDesktopPetRuntimeRouter`, Flying/VPet runtimes | `NEEDS-REAL-MACHINE` | Batch P/U closes the known lifecycle and replacement races; the real `greenfly -> butterfly -> vpet -> greenfly -> Off` sequence is still required. |
| 16 | PetHost/FlyingHost IPC handshake, activate/reset/stop, timeout poisoning, process-tree cleanup | `WindowsVPetRuntime`, `WindowsFlyingPetRuntime`, Host projects | `EXACT` | Batch P/U deterministic IPC lifecycle smoke, host self-tests, and Foundation/Windows smoke pass. Real multi-switch evidence remains row 15. |
| 17 | Online update check, announcement prompt/read state, manual update, log opening, and safe failure UI | `ControlCenterViewModel`, `MaintenanceViewModel`, `HttpUpdateManifestSource`, online contracts | `NEEDS-REAL-MACHINE` | Automated update/recovery contracts pass; real network response, announcement, log-open, and final UI paths remain unverified. |
| 18 | Cleanup directory recognition, whitelist scan, preview, explicit confirmation, UAC boundary, and execution-time revalidation | `CleanupViewModel`, `WindowsCleanupEnvironment`, `WindowsCleanupEngine` | `EXACT` | Cleanup source gates, FoundationSmoke, and WindowsSmoke pass. Real non-admin/UAC-cancel evidence remains separately required; destructive deletion is not authorized here. |
| 19 | Repair tools, driver cleanup, client/window repair, game exit, skip settlement, and UX restart | `RepairToolsViewModel`, `LeagueGameRepairViewModel`, Windows repair services | `PARTIAL` | The active routes and capability boundaries exist; real privilege, process, and visible client-window behavior is not fully accepted. |
| 20 | Validated update package download, signature/identity checks, wait-exit, atomic replacement, fallback, rollback, and old-version preservation | `HttpUpdatePackageDownloader`, `WindowsUpdateReplacementLauncher`, `src/FACM.Updater` | `EXACT` | Helper self-test, recovery contracts, and release build pass. Controlled interrupted replacement is still a separately authorized real-machine test. |
| 21 | One League lockfile/process discovery, LCU auth/session, and safe no-session behavior | `WindowsLeagueTransportSessionSource`, `LeagueTransportSession`, `LeagueHttpGateway` | `EXACT` | Batch U deterministic smoke and live probe pass. Current live evidence used process-command-line fallback and HTTP 200 without serializing credentials. |
| 22 | Shared LCU gateway and single Gameflow heartbeat without UI-thread blocking or duplicate scans | `LeagueHttpGateway`, `LeagueGameflowMonitor`, `PerformanceBudgetProvider` | `NEEDS-REAL-MACHINE` | New live App log shows first discovery about 230 ms, then cache-hit HTTP/Gameflow cycles at 0–1 ms. The user's Workbench-click lag is not closed until a real phase-by-phase Workbench trace is captured. |
| 23 | League Dashboard: connection, account, level/region, gameflow, performance, refresh | `LeagueWorkbenchViewModel`, MainWindow League match section | `PARTIAL` | Read model and shared runtime exist; current candidate has not completed manual real Dashboard interaction across League phases. |
| 24 | League Player: account profile, recent matches, pagination, loading/error states | Workbench Player stage and `LeagueWorkbenchDataSource` | `PARTIAL` | Source/product gates pass; real data-shape and visible loading/error parity remain to be exercised. |
| 25 | League Live: Champ Select/current game, teams, bans, local action, timer, read-only behavior | Workbench Live stage and shared Gameflow | `PARTIAL` | Contract and fixture smoke pass; real Champ Select/current-game data is not yet accepted. |
| 26 | OP.GG Build Advisor: mode/position, recommendation categories, caching, unsupported/in-game rules | Workbench Advisor stage, `LeagueBuildAdvisorService` | `PARTIAL` | Deterministic provider/cache/source checks pass; external service and real League context still need validation. |
| 27 | Recommended loadout: explicit confirmation, rune/spell application, context re-read, result verification | `LeagueBuildLoadoutService`, MainWindow product actions | `PARTIAL` | Narrow capability and deterministic write/read-back checks pass; real client write behavior is unverified. |
| 28 | Recommended item-set generation/application and ownership cleanup | `LeagueItemSetService`, MainWindow item-set actions | `PARTIAL` | File ownership and context guards exist; real League import/application behavior remains open. |
| 29 | Matchmaking and ReadyCheck automation with stable-context/at-most-once rules | `LeagueMatchmakingAutomationService`, shared Gameflow | `PARTIAL` | Deterministic automation evidence passes; real lobby/ReadyCheck lifecycle remains unverified. |
| 30 | Post-game honor/automatic return behavior with verification and safe fallback | `LeaguePostGameAutomationService` | `PARTIAL` | Deterministic lifecycle contract passes; no real post-game cycle has been accepted on this candidate. |
| 31 | Presence modes and one narrow write with read-back verification | `LeaguePresenceService` | `PARTIAL` | Source/smoke capability boundary passes; real client acceptance/reversion behavior remains open. |
| 32 | Bench quick-pick/swap as an explicit single action with bounded read-back | `LeagueBenchQuickPickService` | `PARTIAL` | Deterministic 404/409/capability behavior passes; real Champ Select bench evidence remains open. |
| 33 | Efficiency hotkeys, end-game/close-lobby actions, window repair and client UX repair | `LeagueEfficiencyRuntime`, Windows League services, repair view | `PARTIAL` | Hotkey/source/pressure checks pass; real focus, conflict, privilege, and client-window behavior remains open. |
| 34 | League background cost, request concurrency, cancellation, timeout, session invalidation, and Workbench responsiveness | Shared gateway/monitor/Workbench diagnostic owners | `NEEDS-REAL-MACHINE` | Batch T1/U instrumentation and live idle-phase evidence exist. A click-by-click real trace is required before any further performance policy change. |
| 35 | Mayhem/ARAM query, aliases, public-data fallback, bounded cache, cancel/timeout, and result rendering | Mayhem Core/Infrastructure/App layers | `EXACT` | Mayhem source gates and deterministic smoke pass; external live source health remains a real-data acceptance item. |
| 36 | Base ARAM balance plus Tencent official patch validation and merge semantics | `MayhemBaseAramBalanceService`, `TencentMayhemOfficialPatchService` | `EXACT` | Offline parser/merge/source checks pass; real current-patch data still needs observation. |
| 37 | Rich augment/build details, Chinese localization, Top results, image export/copy, and visible failure states | Mayhem App/ViewModel and infrastructure services | `PARTIAL` | Current services and deterministic contracts exist; the full visible/export path has not been accepted on a real desktop. |
| 38 | N/A in 3.5.15: bounded sanitized Diagnostics Center and export bundle | `DiagnosticsCenterViewModel`, diagnostics contracts/exporter | `4.0-ONLY` | Additive capability. Source gate, redaction, and Windows smoke evidence pass; it must not expose settings, LCU credentials, or arbitrary paths. |
| 39 | N/A in 3.5.15: Product State, recovery coordinator, feature kill switch, and monotonic safety policy | `ProductStateStore`, `RecoveryCoordinator`, `FeaturePolicyEvaluator` | `4.0-ONLY` | Additive safety capability. Deterministic recovery/source checks pass; full crash/relaunch evidence is still a Gate13 item. |
| 40 | DPI/mixed-monitor placement, keyboard-only access, high contrast, text scaling, focus and screen-reader basics | WinUI XAML automation properties, Desktop platform contracts | `NEEDS-REAL-MACHINE` | Source accessibility/DPI gates pass; real Win10 1809/22H2, mixed DPI, keyboard-only, high-contrast, and screen-reader observation remains open. |
| 41 | Portable distribution, embedded tools/hosts, single-file output, version identity, and legacy rollback/build line | `FACM.App` publish, Host stores, `src/FACM`, `src/FACM.Updater`, `src/FACM.ToolBundle` | `PARTIAL` | Local x64 build, FoundationSmoke, WindowsSmoke, and helper paths pass. Final signed package identity and full legacy-to-4.0 migration/rollback are not accepted. |

## Audit totals

| Status | Count |
| --- | ---: |
| `EXACT` | 12 |
| `PARTIAL` | 20 |
| `MISSING` | 0 |
| `4.0-ONLY` | 3 |
| `NEEDS-REAL-MACHINE` | 6 |
| `FAIL` | 0 |
| **Total** | **41** |

The totals are not a release score. `EXACT` means the source/deterministic contract is
currently evidenced; it does not erase the real-machine items listed in the row.

The corrected totals change only row 3 from `NEEDS-REAL-MACHINE` to `EXACT`; no other row is
reclassified by the tray single-left correction.

## Batch W findings that drive the next batches

1. **Active shell close policy is now centralized for the implemented surfaces.** Batch X
   replaced the compact-only watcher with `DesktopSurfaceOutsideClickWatcher`, shared by
   `MainWindow` and `CompactLauncherWindow`. MainWindow cleanup, League, maintenance,
   FolderPicker, and ContentDialog flows now hold explicit suppression scopes. Visible Win10
   interaction and any separately owned top-level surface remain real-machine acceptance items.
2. **3.5.15 UI-text compatibility is not a simple rename.** The legacy catalog has 198 named
   keys and a `[Replace]` compatibility layer. The active 4.0 contract has 120 role-scoped keys;
   its parser accepts arbitrary lines but does not make old role-specific names affect the new
   controls. Y must build an explicit alias/coverage table before claiming text parity.
3. **Pet lifecycle code is now separately green, but visible replacement is not yet a source-only
   conclusion.** Batch P/U removed the known duplicate-host/activate-order failure modes; the
   real multi-switch sequence remains required to close the user's observed symptom.
4. **League session discovery is no longer the immediate idle-loop evidence.** The current App
   log shows one bounded process fallback discovery, HTTP success, and subsequent positive-cache
   cycles at 0–1 ms. The Workbench click lag still needs a user-phase trace; do not add another
   cache, polling loop, limiter, or UI thread before that trace is reviewed.
5. **Legacy source retention is intentional.** The retained WinForms/Updater/ToolBundle/PetHost
   line supports rollback and build compatibility. It must not be used as evidence that the
   active WinUI product already exposes every 3.5.15 path.

## Batch W exit condition

Batch W is complete when this matrix and its evidence boundaries are committed. No feature code
is changed by Batch W. Batch X has completed the unified outside-click implementation slice;
remaining X shell/window/input parity and real visible entry ownership stay open for the next
authorized continuation.
