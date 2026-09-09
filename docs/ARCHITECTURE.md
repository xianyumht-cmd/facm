# FACM Architecture

## Product boundary

The maintained product is FACM 3.5.x lightweight: **WinForms + .NET Framework 4.8 + one FACM.exe**. The repository intentionally avoids restoring the retired 4.x WinUI/Core/Infrastructure/Platform/bootstrapper architecture.

## Solution

`FACM.sln` contains four current projects:

- `src/FACM` — main WinForms application.
- `src/FACM.ToolBundle` — validated embedded tool resources.
- `src/FACM.Updater` — small Windows updater used for atomic EXE replacement and rollback.
- `src/FACM.PetHost` — optional .NET 8/WPF VPet runtime source, validated separately.

The normal 3.5 build embeds ToolBundle but does **not** embed a self-contained PetHost ZIP.

## Main host

`Program.cs` creates one `FacmHost` and registers small modules around the existing 3.5 services. `ShellModule` owns the primary `MainForm`; the compact menu, tray and floating entry remain WinForms surfaces.

The module layer is an ownership/lifecycle boundary, not a separate 4.x application architecture. Do not split the product into a new Core/Infrastructure/Platform stack without a concrete 3.5 requirement.

## WinForms design system

FACM UI evolution stays inside the lightweight WinForms product. `ThemeCatalog` is the palette source, `FacmThemeRuntime` owns the active process theme, `FacmDesignSystem` owns semantic colors/geometry/common product styling, and `FacmWindowChrome` owns the normal top-level FACM window shell.

Shared interactive primitives live under `src/FACM/Theming/` and must preserve native WinForms behavior:

- `FacmActionButton` keeps `Button` click/focus/keyboard semantics while rendering primary/secondary/danger states from the shared palette.
- `FacmToggleSwitch` remains a `CheckBox`; `Checked` and `CheckedChanged` are the behavior contract.
- `FacmStatusBadge` displays semantic neutral/accent/success/warning/error states without introducing page-local palettes.
- `FacmNavButton` and `FacmPillButton` remain native `Button` controls, are keyboard reachable (`TabStop=true`), and show focus cues from the shared palette. Fix navigation behavior in these primitives rather than adding page-local workarounds.

New or materially redesigned product surfaces should use these shared tokens/primitives instead of adding private `Color.FromArgb(...)` design systems. Theme changes must refresh already-open shared controls. Visual refactors must not change feature routing, update semantics or League read/write ownership merely to achieve consistency.

### Current UI migration state

The design system is intentionally **in transition**, not fully unified yet:

- `LeagueHubForm` uses `FacmDesignSystem`, `FacmGlassPanel`, `FacmNavButton` and `FacmPillButton` for its outer shell.
- `LeagueDashboardForm` uses a primary connection/Gameflow status surface plus compact metadata rows instead of a six-card equal grid; its actions and status tones use shared primitives.
- `LeaguePlayerForm` keeps its dense ListView/virtualization structure but now uses shared semantic colors and `FacmActionButton` rather than a page-local dark palette.
- `OnlineCenterForm` and other newer surfaces use shared semantic primitives directly.
- `CompactMenuForm` still contains a legacy private fallback rendering layer (`ThemedPanel`, `ThemedButton`, gradient/theme decorations, raw `ThemeDefinition` radii), but the normal visible control-center path is owned by `DesktopLauncherEnhancer`. The enhancer hides the legacy body, overlays a flat `FacmDesignSystem.Canvas`, normalizes the visible header, uses the shared `WindowRadius`, and renders launcher tiles/context state with one restrained shared accent. Treat the hidden fallback renderer as compatibility debt, not as an approved second visual system.
- Other older League forms must be audited individually for page-local RGB colors, private button styling and rigid fixed geometry. Migrate high-value user surfaces first rather than mechanically rewriting every `Color.FromArgb` occurrence.
- Historical `ThemeCatalog` styles remain for palette compatibility, but shared visible product surfaces should not revive large glass radii, decorative dual-accent gradients, generic equal-card dashboards or pill-heavy navigation.

The target direction is one restrained modern Windows desktop product language influenced by Fluent/PowerToys interaction behavior while staying native WinForms. External design skills may inform audit criteria, but Web-only implementation advice (CSS/React/GSAP/etc.) does not define FACM architecture.

## League runtime

League features share one client/session boundary and one Gameflow monitor.

Important rules:

- Features consume the shared Gameflow state; do not create competing phase polling loops.
- Connected phase reads are authoritative. Process presence is only a fallback when LCU is temporarily unavailable.
- Cadence is activity-based: disconnected/reacquisition 3s, Queue/ReadyCheck 3s, ChampSelect about 2s, InGame 10s, ordinary client state about 5s.
- Lobby/ReadyCheck automation evaluates immediately when the relevant phase is observed.
- Stable Lobby membership uses a fingerprint to prevent duplicate search writes.
- Failed/ambiguous matchmaking writes reconcile `/lol-matchmaking/v1/search` before deciding to retry.
- ReadyCheck attempts use an episode fence; true failures can retry after a short delay, while a final local response prevents duplicate writes.

### Runtime Companion (task PR #283)

The Runtime Companion is a narrow transient **consumer/orchestration surface**, not another League runtime.

- `LeagueHubModule` remains the automatic Champion Select episode owner. It decides when one companion instance may exist and when leaving Champion Select closes it.
- `LeagueLiveModule` constructs the companion from the already-initialized Live/Bench services; it does not create another League session.
- `LeagueRuntimeCompanionController` projects the existing Bench, Build Advisor and Mayhem owners into defensively copied presentation snapshots. Generation/cancellation rules reject stale champion/build/guide work.
- build recommendation reads reuse `LeagueBuildAdvisorModule.RuntimeCompanionReadService`, including the existing shared OP.GG cache/transport.
- Bench swaps reuse `LeagueBenchQuickPickService`; explicit rune/summoner-spell actions reuse `LeagueBuildApplyService`. The presentation Form has no raw League write interface.
- item/build rows that are not deliberately wired to an inline owner remain display-only in the companion. Broader workflows may continue to exist in the unified Recommendation page.
- the transient window has no taskbar entry and keeps `ShowWithoutActivation`; pin/collapse/drag are presentation concerns only.

Window preferences are also shared ownership rather than Form-local state. `LeagueBuildAdvisorModule.RuntimeCompanionSettings` exposes the already-loaded `SettingsModule.Settings` object; `LeagueRuntimeCompanionWindowState` persists position, pin and collapse values through `AppSettings.Save()` and the existing last-known-good recovery file. It never opens a private settings file.

FACM already declares PerMonitorV2 awareness in `app.manifest`. Runtime Companion placement therefore occurs from the Form's `Shown` path, after WinForms has established DPI-scaled physical geometry. Saved coordinates may be negative for displays left of the primary monitor; restore and user drag are clamped to the chosen monitor's current working area. Expanded height is capped against physical working-area height after DPI scaling so 125/150/200% configurations do not reuse a 96-DPI pre-show size assumption.

### Champion Select dodge evidence

The 3.5.38 public field-test probe remains a **consumer** of `LeagueDashboardModule`'s shared Gameflow state. `LeagueDodgeProbeService` owns the episode lifecycle and `LeagueDodgeSideEvidenceProbe` owns short-lived read-only evidence correlation. Neither owns Gameflow or a separate League session.

The probe reads only local LCU GET endpoints:

- `/lol-matchmaking/v1/search` for `dodgeData`;
- `/lol-champ-select/v1/session` for visible `myTeam` / `theirTeam` identity and `chatDetails.chatRoomName`;
- `/lol-chat/v1/conversations` plus selected conversation `messages` / `participants` for room system/departure evidence;
- `/lol-lobby/v2/lobby` for positive party/lobby member departure evidence.

Normal sampling is bounded: conversation details are baselined once and the normal loop relies primarily on conversation `lastMessage`; only a confirmed dodge opens an approximately one-second high-frequency evidence burst. This is intentionally separate from the process-wide Gameflow cadence and does not create a second phase poller.

Tencent live evidence has shown `StrangerDodged` with `dodgerId=0`, a complete visible ally identity set, and hidden opponent identities. The classifier therefore uses positive evidence and fails closed. `StrangerDodged`, hidden opponent IDs, or the absence of an ally departure signal are not sufficient enemy evidence.

The probe is read-only by construction because it receives `ILeagueClientApi`, not a League write interface. It must not send chat, accept/decline ReadyCheck, start/cancel matchmaking, alter Champion Select, or introduce any LCU `POST`/`PUT`/`PATCH`/`DELETE` path.

## Mayhem / ChampSelect

Mayhem keeps the established 3.5 service/cache/network path. UI code must treat win-rate values as **0..100 percentage points** and must not multiply by 100 again.

Do not replace the current fast service with a 4.x data stack merely for architectural consistency. UI completeness, cancellation, bounded waits, cache behavior and fail-soft handling are the relevant contracts.

## Desktop entry and pets

`DesktopEntryGameflowPolicy` owns in-game suppression semantics:

- first InGame transition suppresses desktop entry surfaces;
- restore ownership is recorded only when Gameflow hid something that had been visible;
- repeated InGame observations do not repeatedly close a control center explicitly reopened by the user;
- leaving InGame restores only Gameflow-owned visibility.

`PetsModule`/`AnimalPetManager` expose active and visible state. `VPetHostClient` tracks requested visibility so hide/show intent survives host startup races.

## Update architecture

`OnlineService` reads version/announcement/mirror metadata. `UpdateInstaller` downloads the approved 3.5 Release EXE, verifies it and extracts the embedded `FACM.Updater.exe`. The updater waits for FACM to exit, atomically replaces the EXE, validates the result, restarts it and can roll back on early failure.

There is no current 4.x migration/bootstrapper mode. Unknown legacy JSON properties are ignored rather than used as runtime migration instructions.

## Build contracts

CI must enforce:

- ToolBundle input integrity and embedded resource presence.
- optional PetHost build/self-test.
- no `FACM.Resources.PetHost.zip` in ordinary FACM.exe.
- FACM.exe <10 MiB.
- host, League dashboard/automation, performance, updater, floating-ball, pet and Mayhem smoke tests.
- shared control primitive contract checks, including keyboard-reachable navigation, anti-regression geometry/chrome repaint rules.
- desktop launcher definition/geometry rules and shared compact geometry constraints.
- UI text contract.

`--facm-host-test` covers Runtime Companion settings serialization/recovery and deterministic DPI/multi-monitor window-state geometry. `--league-dashboard-test` covers Runtime Companion presentation/controller projection invariants in addition to the deterministic dodge-probe classifier/departure-text smoke. The Windows build remains the executable gate; real Tencent-client acceptance is still required before the task PR is considered shippable.

## State ownership rules

Prefer one owner per mutable runtime concern:

- one Gameflow monitor;
- one shell/MainForm;
- one updater replacement path;
- one desired pet visibility state;
- one online version manifest;
- one canonical 3.5 lightweight publisher;
- one shared settings object for Runtime Companion window preferences;
- one shared UI design-system direction rather than page-local theme engines.

When asynchronous work can finish after context changes, use cancellation/generation/fingerprint/postcondition checks rather than adding arbitrary sleeps.
