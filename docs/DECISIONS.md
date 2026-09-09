# FACM Decisions

This file records current product decisions. Historical implementation detail belongs in Git history and the 3.5.19 backport audit.

## D-001 — 3.5.x is the only maintained product line

**Decision:** keep WinForms/.NET Framework 4.8/single-EXE as the canonical FACM product.

4.x is retired from the default working tree. Do not reintroduce WinUI/Morphing Surface, FACM.App/Core/Infrastructure/Platform.Windows, native bootstrapper, CAB or multi-version runtime unless a future requirement is independently justified.

## D-002 — Backport behavior, not architecture

Useful 4.x lessons are allowed when they solve a concrete 3.5 problem with a small implementation: immediate automation evaluation, one state owner, cancellation, generation/fingerprint fences, postcondition reconciliation and reason-owned visibility.

Do not transplant architecture solely because it is newer.

## D-003 — Preserve the 3.5 Mayhem data path

The 3.5 Mayhem/海符 path is fast and remains canonical. Fix display units, UI completeness, cancellation or concrete defects without replacing its service/cache/network stack.

## D-004 — One Gameflow owner

League phase/activity observation has one process-wide owner. Automation and UI subscribe to that state instead of creating parallel phase loops.

Human-visible response speed comes from reacting immediately to observed state and removing unnecessary sleeps, not from multiplying pollers.

## D-005 — Writes must be deduplicated and reconciled

Matchmaking and ReadyCheck automation are best-effort writes with explicit ownership:

- successful writes commit their fence;
- true failures can retry within the existing phase/episode;
- ambiguous writes read authoritative local state before retrying;
- normal success paths do not pay extra reconciliation network cost.

## D-006 — In-game hiding is non-destructive

Entering InGame hides the shell/pet but does not stop the pet runtime. Gameflow restores only visibility it owns. User-explicit actions remain allowed.

## D-007 — Lightweight PetHost contract

`FACM.PetHost` source stays because VPet compatibility is useful, but normal 3.5 publishing builds/self-tests it separately and does not embed the self-contained bundle into FACM.exe.

A stale local `out/PetHostBundle.zip` must never change the ordinary build output implicitly.

## D-008 — One canonical publisher

The only current release workflow is `.github/workflows/publish-3.5-lightweight.yml` (**FACM 3.5 Lightweight Release**). Its file-driven request is `release/3.5-request.json`.

The old heavyweight publisher and `release/request.json` are retired.

## D-009 — Normal 3.5 updater only

FACM downloads a trusted 3.5 Release EXE, validates it, then uses the embedded small updater for atomic replacement/rollback/restart. 4.x bootstrapper/migration mode is retired.

## D-010 — Git history is not the working tree

Removing 4.x means removing it from the current working tree and current CI/release surface. Do not rewrite Git history. Old releases/tags/remote branches are separate destructive-history cleanup and require a separate explicit decision.

## D-011 — Rebrand later and narrowly

Future public brand target may be GGman（鸡鸡侠）, but do not globally replace `FACM` identifiers. First inventory update URLs, namespaces, assembly/resource names, mutex/config paths and compatibility contracts. User-facing naming can change before internals.

## D-012 — 3.5.20 is the first post-cleanup release

3.5.20 was published after P1 and the 4.x working-tree cleanup reached `main`. It is a new lightweight Release rather than mutated/reused 3.5.19 bytes. Future releases must use a new 3.5.x patch version and keep the online manifest migration-free.

## D-013 — One shared WinForms design system

**Decision (2026-09-06):** product-experience work stays on WinForms and converges existing surfaces on the current theme runtime instead of introducing a second UI framework or page-local palettes.

- `ThemeCatalog` remains the palette source.
- `FacmThemeRuntime` remains the process-wide active-theme owner.
- `FacmDesignSystem` owns semantic colors, radii and common styling.
- `FacmWindowChrome` owns ordinary FACM top-level window chrome.
- reusable interactive primitives such as action buttons, toggle switches and status badges must preserve native WinForms `Button`/`CheckBox` behavior.
- visual refactors must not change update protocol, League write semantics, polling ownership, or launcher routing merely to achieve consistency.

This lets the 3.5 lightweight product look coherent without paying the architecture, startup or packaging cost of WPF/WinUI migration.

## D-014 — Contextual shell navigation consumes shared state only

**Decision (2026-09-07):** the floating entry may adapt its home surface and LOL destination to the current Gameflow scene, but navigation is a consumer of the existing `LeagueDashboardModule` state, never a new League runtime owner.

- the shell may cache the latest shared `LeagueDashboardPhaseState` for display/routing only;
- no contextual-home feature may add LCU polling, matchmaking writes, ReadyCheck writes or a second League session;
- a direct floating-entry click may show the context card, while tray/external control-center opens remain the generic four-shortcut home;
- contextual LOL navigation reuses the unified LOL Hub and selects an existing view rather than creating a second product hierarchy;
- Gameflow visibility ownership remains independent: navigation context must not weaken the existing in-game hide/restore policy.

This keeps the home surface useful in the moment without turning shell UX work into a new automation or transport subsystem.

## D-015 — Tencent dodge-side classification requires positive evidence

**Decision (2026-09-09):** Champion Select dodge-side classification must fail closed and may not treat `StrangerDodged` as enemy proof.

Live Tencent-client evidence captured natural dodges where `/lol-matchmaking/v1/search` reported `state=StrangerDodged` but `dodgerId=0`; at the same time the local `myTeam` identity set was complete while opponent Summoner IDs were hidden. The public LCU schema therefore cannot be assumed to expose a usable dodger identity on Tencent.

For the 3.5.38 public read-only field test:

- keep `dodgeData` as the authoritative signal that a dodge occurred;
- correlate only positive side evidence from the existing ChampSelect session, room/chat system events, conversation participant changes and lobby member changes;
- a positively identified local-team departure can classify `ally` / `ally-party`;
- `enemy` requires a positive opponent identity or a non-zero dodger identity excluded from a known-complete local roster;
- hidden opponent identities, `StrangerDodged`, or failure to observe an ally signal must remain `unknown` rather than being converted to enemy;
- ordinary player chat bodies are not diagnostic data and must not be logged; only bounded system/event-style departure evidence may be recorded locally;
- the probe remains GET-only and must not gain a League write interface or a second Gameflow owner.

A normal user-facing ally/enemy notification is deferred until public field evidence demonstrates a stable positive mapping. This keeps the experiment useful without turning missing Tencent data into false certainty.

## D-016 — Runtime Companion is a presentation/orchestration layer, not a new League runtime

**Decision (2026-09-10, task PR #283):** the Champion Select helper may become a narrow context-aware Runtime Companion, but it must reuse current 3.5 owners instead of creating a parallel product stack.

- `LeagueHubModule` keeps one-presentation-per-Champion-Select-episode ownership and remains the only automatic-popup lifecycle owner.
- `LeagueRuntimeCompanionController` projects Bench, Build Advisor and Mayhem state into presentation snapshots; it does not own Gameflow or a second League session.
- Bench swaps continue through `LeagueBenchQuickPickService`.
- Rune and summoner-spell inline actions continue through `LeagueBuildApplyService`, including its confirmation-adjacent preparation, phase/champion/queue revalidation and settled postcondition checks; the Form has no raw LCU write path.
- recommendation categories without an intentionally wired safe owner remain display-only in the companion even when another FACM page supports a broader workflow.
- pin, collapse and dragged position preferences use the process-shared `AppSettings` owner and its last-known-good recovery path; the transient Form must not create a private settings file or load a stale second settings object.
- initial placement and saved-position clamping happen after the Form reaches `Shown`, when the existing PerMonitorV2 manifest contract has established its physical DPI-scaled geometry. Pre-Show 96-DPI placement math is not authoritative on mixed-DPI desktops.
- saved coordinates may be negative for monitors left of the primary display; monitor topology changes must clamp the companion back into a current working area.

This keeps the Akari-style narrow interaction model as a UI improvement while preserving FACM's single-session, lightweight WinForms architecture. PR #283 remains a review task until Windows CI and real Tencent-client acceptance are complete; this decision does not authorize merge or release.
