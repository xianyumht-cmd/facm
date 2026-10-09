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

## Cloud identity and sync

`CloudSyncModule` is a fail-soft consumer of Tencent CloudBase. It does not own UI and must never block GGman startup.

- `RuntimePaths.DataDirectory` is the application-local persistent data root; it is distinct from regenerable `runtime` data.
- `CloudIdentityStore` keeps a random stable `device_id` plus the CloudBase anonymous user id in `data/cloud-identity.json` and a last-known-good copy. Access tokens and refresh tokens are process memory only and are never persisted.
- `CloudBaseClient` uses the existing .NET Framework `System.Net.Http` stack and fixed HTTPS gateway for environment `ggman-d4gioqqcz434d9e4d`. Anonymous auth uses `x-device-id`; PostgreSQL requests use the returned access token.
- Remote work starts once from `Application.Idle` after the normal host/UI initialization path. Network failure, CloudBase downtime, auth failure, or RLS rejection is logged as a fail-soft skip and cannot disable League or other local features.
- P1 writes only the `ggman_devices` record (`device_id`, app version, OS version, last-seen timestamp), omits `owner_id`, and verifies the read-back owner against the authenticated CloudBase `sub`.
- PostgreSQL RLS remains the data-ownership boundary. Client code must not receive a service-role/API-key credential and must not supply `owner_id` itself.
- Settings sync, account-history sync, telemetry upload, recovery-code/hardware-fingerprint matching, and local SQLite are later scopes, not implicit P1 behavior.

### Personal stats and anonymous ranking

`LeaguePersonalStatsModule` is an event-driven consumer of the existing League Gameflow owner. It does not create a second phase poller. On connected Gameflow state changes it may read `/lol-summoner/v1/current-summoner`, keeps the last captured account hash as an episode fence, and records only when the observed account changes. It derives a device-scoped HMAC-SHA256 account key from the local random `device_id` and PUUID, then discards the raw PUUID.

- `PersonalStatsStore` persists long-lived local history under `data/personal-stats.json` with a last-known-good recovery copy. It stores active calendar days, first/last use timestamps and hashed account records only.
- Local personal stats are enabled by default. Cloud ranking is a separate opt-in setting and defaults off.
- Cloud account history uses `ggman_record_account`; clients never send `owner_id`. The SQL function derives ownership from `auth.uid()`.
- Global ranking uses `ggman_get_personal_stats`, a SECURITY DEFINER aggregate that returns only the current user's account count, rank, participant count and percentile. It does not expose other users' owner IDs or account hashes.
- `ggman_devices.ranking_opt_in` is the population gate. Turning ranking off removes the user from the aggregate population while keeping local history intact.
- Cloud failures remain fail-soft. The local profile continues to work when the migration, network or CloudBase service is unavailable.
- Full settings sync, general product telemetry, hardware fingerprints and raw Riot identifiers remain outside this scope.

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
- build recommendation reads reuse `LeagueBuildAdvisorModule.RuntimeCompanionReadService` and the module-owned shared `CachingOpggBuildApi`. The same OP.GG payload may project up to three source-ordered alternatives for supported categories; expansion is presentation-only and does not fan out another request.
- Bench swaps reuse `LeagueBenchQuickPickService`. Bench availability controls only the quick-swap strip; it does not gate ARAM/Mayhem guide relevance when a valid ChampSelect session/champion and mode are known. Explicit rune/summoner-spell actions reuse `LeagueBuildApplyService`; the first source row remains the apply default and the existing owner retains phase/champion/queue revalidation plus settled postcondition verification.
- core-build import reuses `LeagueItemSetService`; preparation is read-only, the user confirms explicitly, live context is revalidated before the first filesystem write, only FACM-owned recommendation files are changed, and the committed JSON is verified. Categories without a deliberately wired safe owner remain display-only.
- explicit quit-current-Champion-Select uses `LeagueChampSelectQuitService` and dedicated `ILeagueChampSelectQuitWriteApi`. The writer allowlists the primary `POST /lol-lobby-team-builder/champ-select/v1/session/quit` and one guarded `POST /lol-gameflow/v1/session/request-lobby` fallback after definite HTTP 400 with original party identity/roster observed. It never deletes/recreates a lobby or kills a client process. The service verifies settled Lobby phase, absent ChampSelect session, and matching original party ID/members. When original identity is unavailable the fallback is disabled; no second Gameflow poller is introduced.
- base ARAM balance is supplementary presentation data. `RiotGameDataService.EnrichAsync` is the single full-Mayhem enrichment owner: it starts `OpggAramBaseBalanceService.EnrichAsync` once in parallel with visual metadata, using that service's bounded ten-minute complete-result cache. `MayhemAutomaticGuideService` must not pre-call the balance service and then call Riot enrichment again.
- Bench availability is not a game-mode classifier. `LeagueQueueModePolicy` separates ordinary ARAM (queue 450 / ARAM) from ARAM Mayhem (observed global queue 2400, CN/WeGame queue 3270, or KIWI/ARAM_MAYHEM mode tokens). Ordinary ARAM requests only its version-bound base-balance supplement; Mayhem uses the full Mayhem guide/augment pipeline. Unsupported Bench modes fail closed instead of displaying Mayhem data.
- the companion renders base ARAM balance only when a real `BaseBalanceSummary` exists. `syncing`/`unavailable` states remain explicit warning states; missing data is omitted rather than rendered as a fake `0` or `no modifier` result.
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


## Floating-ball-first quick launcher (3.5.50 integration)

`MainForm` remains the single shell owner: floating ball and tray are the default entry, and `CompactMenuForm` is a transient anchored flyout. `DesktopLauncherEnhancer.Apply` transforms the prepared compact window into a two-row launcher **before** `MainForm.PositionMenu` and `Show`, avoiding first-frame geometry jumps. It now consumes typed `CompactMenuForm.LauncherTheme`, `LauncherOwner`, `LauncherSettings`, `LauncherCleanup`, and explicit menu actions instead of reflecting on private fields and methods. Four main action targets route via `LeagueHubUiBridge` to existing stable view IDs; footer actions retain the cleanup/repair, personalization and more-settings paths. Source-level visual adjustments do not create any new League Gameflow polling, LCU write owner, cloud dependency, persistent full-size dashboard or runtime resource. The legacy non-launcher CompactMenuForm path remains available as a guarded fallback.


## LOL workbench single-rail navigation (3.5.51)

`LeagueHubForm` owns one persistent grouped primary navigation rail with all nine existing stable `LeagueHubNavigation` view IDs, a client-status footer and one fill-width content canvas. The old global secondary tab strip and persistent context action dock have been removed; each view's factory, Gameflow owner, contextual launch through `LeagueHubUiBridge.RequestOpen(owner, viewId)`, and embedded child lifecycle remain unchanged. This is a transient workbench opened from the floating-ball-first shell, not a resident dashboard or separate League session. The main content area has no new polling or LCU writes. Navigation shows Match (dashboard/player/live), Guide (recommendation/mayhem), Automation (efficiency/presence/repair), and the separated My GGman view; internal section-key compatibility remains intact. The navigation rail compacts at narrow window widths and scrolls vertically if constrained by DPI.


## Player history page presentation (3.5.52)

`LeaguePlayerForm` continues to consume `LeaguePlayerDataService` through the existing profile/match history cache, pagination, progressive enrichment and lifetime cancellation contracts. Its UI layout now recalculates match/stats lists, footer buttons, empty/loading state, and seven match table column widths from the actual embedded form client size; the virtualized recent match ListView remains unchanged. Winner/loser semantic colors highlight only the result cell instead of coloring all visible match details. No new network requests, background polling, or data ownership were added. `LeaguePlayerSmokeTest` validates the layout bounds and responsive column-width constraints without creating a real League client. Native WinForms/DPI verification is still required after automated build checks.


## Recommendation workbench presentation (3.5.53)

`LeagueRecommendationForm` remains the single owner of recommendation display state, selection, guarded confirmation, refresh and routed League apply service calls. Its content is hosted inside a vertically scrollable WinForms panel with 1/2/3 responsive preview/choice columns; an independent fixed-height bottom action bar retains Refresh, Apply and explicit result feedback at narrow embedded dimensions. `LeagueRecommendationDesignEnhancer` styles labels inside the inner content panel by control identity rather than pre-refactor absolute Y positions, and refreshes semantic feedback tones on theme change. Shared `FacmDesignSystem` tokens replace legacy independent neon colors. No new Gameflow or network pollers, data owners, LCU writes, auto-apply timing changes or dependency bundles. Existing read/apply and preview smoke contracts remain valid, and a layout/status policy smoke covers breakpoints and semantic result colors.


## Mayhem lookup and legacy ChampSelect assistant UI (3.5.54)

`MayhemLookupForm` continues using the existing `MayhemLookupLayoutPolicy` for responsive query/actions/status/preview placement; no new layout owner or image pipeline. Query, cancel, save and copy now expose native accessible labels, pending cancellation uses a warning status, and user-visible dialog/file names use GGman branding. `MayhemLookupLayoutPolicy.ValidateForSmokeTest` covers narrow-width query/action and status/preview non-overlap. The separate 660px legacy `LeagueChampSelectAssistantForm` keeps its current automatic-show, ShowWithoutActivation, Bench-only gate, fixed compact/expanded sizes, drag logic and request/read/write owners, but now uses shared `FacmDesignSystem` surfaces/colors, shared native-looking action buttons and semantic swap/guide feedback. The modern `LeagueRuntimeCompanionForm` remains untouched, as do all gameflow and LCU pathways. These are bounded presentation/feedback changes, not a redraw of the runtime companion or a live-client screenshot validation.


## Real-time narrow companion scroll visibility (3.5.55)

`LeagueRuntimeCompanionForm` retains its fixed 320px-width nonactivating WinForms overlay, Gameflow-derived snapshot consumer and existing scrollable recommendation body. A thin decorative scroll-progress rail lives outside the scrolling body in a local dock-fill shell; it computes thumb geometry from the current viewport, content height and scroll offset, and hides itself when all recommendations fit. It is not an interactive scrollbar and adds no polling or network work. The original compact header remains a direct Form child so `LeagueRuntimeCompanionWindowState` still resolves pin/collapse controls and persists/restores position as before. Localized button accessible names follow their current action (including pin/unpin, collapse/expand, recommendation alternatives, augment pages, Bench pages), with original click handlers unchanged. The scroll-geometry helper has deterministic smoke assertions for top/mid/end and no-overflow. This change does not touch quit-from-ChampSelect, loadout/item-set writes or League data service ownership.


## Recommendation footer layout regression guard (3.5.56)

The Recommendation page's scroll body and fixed footer remain owned by `LeagueRecommendationForm`. `ResolveActionBarLayoutForSmokeTest` now determines status/Refresh/Apply rectangles and total action-bar height from the embedded client width; the bottom action bar switches from 69px single-row to 94px stacked status/buttons below 480px. `LayoutActionBar` applies exactly that policy. `LeagueBuildAdvisorSmokeTest.ValidateRecommendationPageLayout` proves footer elements are on-screen and nonoverlapping at 280, 320, 400, 479, 480, 680 and 920 logical pixels. It does not change recommendations, write confirmation, theme system, Gameflow, floating entry, League network calls or auto-apply lifecycle.


## Hub contextual launch without a throwaway Dashboard (3.5.56)

`LeagueHubForm` now resolves an optional initial route before its first `Shown` event and creates only that route's child. `LeagueHubUiBridge` passes a verified contextual route through the form's typed `SetInitialView` method immediately after construction rather than installing an additional `Shown` handler that previously opened Dashboard and instantly discarded it before showing the requested page. Invalid/empty requests still start on Dashboard. All nine route factory mappings, ownership, child close/dispose behavior and Gameflow observers remain unchanged. The existing Dashboard smoke verifies every contextual route and unknown-route fallback without launching a real League process.


## My GGman profile page presentation (3.5.57)

`LeaguePersonalStatsForm` remains the local-first owner of already-projected account counts, active-day statistics, recent account rows, cloud ranking opt-in, optional usage telemetry switch and status. The formerly 720px fixed main canvas is now hosted in a single scrollable WinForms area; four existing panels share a computed content width and the three top summary metrics allocate non-overlapping horizontal slots. Its content has enough height to preserve the status message and refresh button when the Hub's embedded viewport is shorter than the original standalone window. `LeaguePersonalStatsForm.ValidateForSmokeTest` verifies the width and metric bounds under narrow and wide host dimensions, wired into `FacmHostSmokeTest`. No new routing, API calls, identity changes, data migration, cloud ranking or privacy preference writes are introduced.


## Update Center readable state and release notes (3.5.58)

`OnlineCenterForm` remains the existing signed 3.5.x update dialog and keeps `OnlineService` as the sole metadata reader and `UpdateInstaller` as the download/signature/atomic-replacement owner. Its 560×620 client surface uses a slightly taller version card and shorter announcement card, with the same forced-update exit behavior. The version card now separates compact verification/download status from a selectable, read-only, independently scrollable multiline release-notes field; announcements retain their own scrollable region. `CanInstallUpdateForSmokeTest` gates manual/automatic update actions on a verified available manifest and no metadata error; `ResolveReleaseNotesForSmokeTest` exposes entire publisher notes without forcing multiline text into a clipped status label. On metadata failure, a fresh error-only snapshot prevents a stale available-update action; on installer failure, an explicit inline error and badge persist alongside the existing error dialog. `UpdateMirrorSmokeTest` covers these state contracts. No manifest schema, mirror choice, approved URL, download hash/signature, updater payload or compatibility asset changes.


## Fresh-install quick-access guidance (3.5.59)

`SettingsModule.IsFreshInstallation` is resolved before `AppSettings.Load` creates a new settings file. The decision requires no primary settings, no recovery settings and no legacy FACM settings; migrations and restored installations do not receive first-use UI. After shell initialization, `Program` opts `MainForm` into one initial lightweight introduction. `MainForm.HandleShown` schedules the existing `CompactMenuForm` launcher to open once, only if the shell is not in a suppressed gameflow episode or cleanup startup. `DesktopLauncherEnhancer` inserts a short native card above its existing four primary shortcut tiles, while shifting launcher/footer geometry together and preserving the floating-ball-first process entry, tray recovery and contextual launch routes. The card has two explicit choices: open the already-owned workbench or dismiss, which returns the launcher to its normal dimensions. An optional '使用指南' entry in the existing '设置' popup lets users reopen the same card later; no extra window, network request, custom persisted preference, new League observer or notification timer is introduced. The shared `UiTextKeys`/`UiTextCatalog` define all new public copy, and `ShellUxSmokeTest` proves fresh-vs-migrated eligibility and context/non-context layout bounds.


## Tray and legacy launcher entry semantics (3.5.60)

The compact desktop launcher remains the primary floating-ball entry; a ball click intentionally **toggles** its existing panel. By contrast, the tray's **Open control center** item and tray icon double-click now call `MainForm.EnsureMenuOpenAndActive` so an already-open panel is surfaced rather than unexpectedly dismissed. The tray's existing **More** group now exposes the same on-demand `使用指南` card as the launcher Settings popup via `MainForm.OpenGettingStarted`. The old compact League button directly invokes the typed `LeagueHubUiBridge.RequestOpen(owner, string.Empty)` path. `ShellMenuGroups.FindGroup` is now a pure menu lookup; the retired League dropdown returns null without indirectly scheduling a Hub window. Both active and legacy surfaces still target the single nine-route `LeagueHubForm`, with the existing modal lifecycle and nonactivating in-game companion unchanged.


## Manual League ESC settings backup and CloudBase bridge (3.5.61)

`EscSettingsForm` is an explicit user-invoked settings dialog reachable from the launcher Settings popup and native tray More menu. It uses `EscSettingsBackup` for a strictly allowlisted Config-folder snapshot, local backup/restore with pre-restore copy and rollback, file-size/hash verification and client-running preflight. The cloud action uses `CloudIdentityStore`'s current anonymous device identity with an on-demand `CloudBaseClient`; it never starts a new background sync or polls LCU, and shares no mutable owner with existing gameflow/automation writes. The new CloudBase `ggman_esc_profiles` RPC/table migration is fully separate from `ggman_settings_sync`; the new cloud payload is manually replaced and read only by its authenticated owner (see `docs/ESC-SETTINGS-BACKUP.md`). Cloud upload/download are not activated until the actual CloudBase PostgreSQL migration is applied. Device identity is not a cross-device login contract.
