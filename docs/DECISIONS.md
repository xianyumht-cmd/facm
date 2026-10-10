# GGman / FACM Decisions

This file records current product decisions. Historical implementation detail belongs in Git history and the 3.5.19 backport audit.

## D-001 — 3.5.x is the only maintained product line

**Decision:** keep WinForms/.NET Framework 4.8/single-EXE as the canonical product line.

The public product name is GGman. Historical/internal FACM identifiers remain where required for compatibility. 4.x is retired from the default working tree. Do not reintroduce WinUI/Morphing Surface, FACM.App/Core/Infrastructure/Platform.Windows, native bootstrapper, CAB or multi-version runtime unless a future requirement is independently justified.

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

`FACM.PetHost` source stays because VPet compatibility is useful, but normal 3.5 publishing builds/self-tests it separately and does not embed the self-contained bundle into `GGman.exe`.

A stale local `out/PetHostBundle.zip` must never change the ordinary build output implicitly.

## D-008 — One canonical publisher

The only current release workflow is `.github/workflows/publish-3.5-lightweight.yml` (**GGman 3.5 Lightweight Release**). Its file-driven request is `release/3.5-request.json`.

The old heavyweight publisher and `release/request.json` are retired.

## D-009 — Normal 3.5 updater only

GGman downloads a trusted 3.5 Release EXE, validates it, then uses the embedded small updater for atomic replacement/rollback/restart. 4.x bootstrapper/migration mode is retired.

The repository path and internal updater/resource identifiers may still contain `FACM`; that is a compatibility detail, not a public brand contract.

## D-010 — Git history is not the working tree

Removing 4.x means removing it from the current working tree and current CI/release surface. Do not rewrite Git history. Old releases/tags/remote branches are separate destructive-history cleanup and require a separate explicit decision.

## D-011 — Public rebrand is GGman; internal FACM compatibility identifiers stay

**Decision (superseded on 2026-09-17):** the earlier “rebrand later and narrowly” plan is complete as of GGman 3.5.40 / PR #285.

Public identity is now GGman（鸡鸡侠）:

- executable and Release asset: `GGman.exe`;
- Windows product/title/company fields: GGman;
- user-visible product text: GGman;
- local/CI package identity: GGman;
- updater-visible download filename and User-Agent: GGman.

Do **not** mechanically rename these internal compatibility identifiers without a separate migration design and end-to-end compatibility proof:

- repository `xianyumht-cmd/facm`;
- `FACM.sln` and `src/FACM/`;
- `namespace FACM.*`;
- `FACM.Resources.*` embedded logical resource names;
- `FACM.ToolBundle`, `FACM.Updater`, `FACM.PetHost` internal component identities;
- existing signing secret/environment variable names and other persisted protocol identifiers.

A future internal-identifier migration is a separate engineering project, not unfinished branding work.

## D-012 — 3.5.20 is the first post-cleanup release

3.5.20 was published after P1 and the 4.x working-tree cleanup reached `main`. It is a new lightweight Release rather than mutated/reused 3.5.19 bytes. Future releases must use a new 3.5.x patch version and keep the online manifest migration-free.

## D-013 — One shared WinForms design system

**Decision (2026-09-06):** product-experience work stays on WinForms and converges existing surfaces on the current theme runtime instead of introducing a second UI framework or page-local palettes.

- `ThemeCatalog` remains the palette source.
- `FacmThemeRuntime` remains the process-wide active-theme owner.
- `FacmDesignSystem` owns semantic colors, radii and common styling.
- `FacmWindowChrome` owns ordinary top-level window chrome.
- reusable interactive primitives such as action buttons, toggle switches and status badges must preserve native WinForms `Button`/`CheckBox` behavior.
- visual refactors must not change update protocol, League write semantics, polling ownership, or launcher routing merely to achieve consistency.

This lets the 3.5 lightweight product look coherent without paying the architecture, startup or packaging cost of WPF/WinUI migration.

## Registered accounts own cross-device personal stats; anonymous history is not blindly summed (2026-10-10)

Default local history recording on GGman launch is independent from identity and survives login/logout. Registered email UID becomes owner of new cloud usage-day and LOL account observations; a per-UID HMAC of the observed PUUID is stable across machines but is not an encryption of PUUID and must not be treated as a secret. Existing per-device HMAC hashes cannot be mathematically converted to those new identifiers. Preserve local history and offer a one-time, explicit source-aware import; old counts are lower-bound maxima and must not be summed across device sources or added to new unique counts. A separate ranking visibility preference is enabled on registered sign-in by default with an accessible opt-out. Feature usage telemetry remains anonymous and separately opt-in. Do not force user registration or transfer an anonymous table's ownership via SQL reassignment. Finish schema and authorization acceptance before deploying clients.

## Only game ESC files qualify a cloud snapshot source (2026-10-10)

In WeGame installations, `LeagueClient\\Config` contains YAML client/account preferences and `Game\\Config` contains actual in-game ESC settings. For auto-detection and direct backup/restore path normalization, prefer `Game\\Config` over sibling client Config and require an actual allowlisted file; a League executable alone is insufficient. Continue supporting a verified legacy root `Config` folder holding ESC files. Keep YAML client preference files out of this feature unless separately specified and reviewed. This fixes the 3.5.62 false-positive locator without modifying registered CloudBase ownership, RLS or backup schema.

## ESC cloud actions belong to My GGman and may run with LOL open (2026-10-10)

ESC settings are owned by the player's registered GGman account. Place controls inline beneath personal stats in **LOL 工作台 → 我的 GGman**, not under generic app settings or tray More, and keep the existing workbench navigation intact. Reuse the current manual upload/restore and confirmed recovery implementation rather than adding another independent window. Prioritize a running League process path, then already configured game path and League uninstall registry paths; resolve each only if a real `Config` directory is validated, with manual folder selection available. Do not prohibit reads or writes simply because League is running: file permissions/locks and integrity checks are authoritative. A successful on-disk restore cannot promise immediate in-game application or persistence after a client exit, so show that caveat and do not silently reapply or poll in the background.

## ESC cloud owner is registered UID, not device ID (2026-10-10)

The prior experimental anonymous ESC profile upload is superseded and must never be published. ESC upload/restore is explicitly initiated by the player and uses only the CloudBase registered email account's bearer; Postgres grants and RLS permit `authenticated` only. User intent is captured with a pre-upload/restore preview; optimistic `version` protects against a second PC's silent overwrite. The local snapshot remains fully usable offline. No background ESC sync, global device-settings migration or forced login is introduced. CloudBase catalog/RLS checks, mock A/B SQL isolation, and live anonymous/no-credential denial checks supported promotion to official 3.5.61 at the owner's request. Real registered A/B HTTP ownership and Riot game retention/recovery remain explicitly unverified and need post-release monitoring and acceptance.

## Account confirmation closes its modal; management stays accessible (2026-10-10, released in 3.5.61)

The optional email login modal is an owned, short-lived task window, not a second persistent GGman dashboard. After an authenticated registered UID is established, close the modal with a success result and update the existing **我的 GGman** account action. Reopening the same entry shows the email/UID and logout rather than disabled sign-in inputs. A verification failure must leave the login window open with an error; successful closure does not revoke the session. This keeps session lifetime independent from window lifetime and preserves the existing lightweight floating-ball and Hub architecture. The owner confirmed real Windows email sign-in and the corrected modal UX; PR #314 was merged for 3.5.61. OTP CAPTCHA and cross-device UID behavior remain pending real-world follow-up.

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
- recommendation categories without an intentionally wired safe owner remain display-only in the companion even when another page supports a broader workflow.
- pin, collapse and dragged position preferences use the process-shared `AppSettings` owner and its last-known-good recovery path; the transient Form must not create a private settings file or load a stale second settings object.
- initial placement and saved-position clamping happen after the Form reaches `Shown`, when the existing PerMonitorV2 manifest contract has established its physical DPI-scaled geometry. Pre-Show 96-DPI placement math is not authoritative on mixed-DPI desktops.
- saved coordinates may be negative for monitors left of the primary display; monitor topology changes must clamp the companion back into a current working area.

This keeps the Akari-style narrow interaction model as a UI improvement while preserving the product's single-session, lightweight WinForms architecture.

## D-017 — Runtime Companion alternatives are bounded projections of one source payload

**Decision (2026-09-10, PR #283 P0):** richer Akari-style recommendation density must not multiply transports, polling loops, or write owners.

- Build Advisor may retain at most the first three OP.GG alternatives for runes, summoner spells, starter items, boots, core items and skill order from the same already-fetched build payload.
- source ordering remains authoritative: row zero is the existing default and remains the option used by `LeagueBuildApplyService`; later alternatives are presentation-only until a separately designed chooser exists.
- `pick_rate`, sample count and win evidence are projected only when the source actually supplies them. Missing win evidence stays unknown and must never be rendered as `0%` merely because a JSON key is absent.
- Runtime Companion uses progressive disclosure for rows two and three. Expanding a section performs no network request.
- champion Tier/rank/win/pick/ban summary is projected from the existing recommendation object, not a new statistics endpoint.
- equipment import routes through the existing `LeagueItemSetService`; preparation remains read-only, the user confirms explicitly, the owner revalidates phase/champion/queue before writing, only product-owned recommendation files are changed, and committed JSON is verified.
- base ARAM balance enrichment reuses the existing bounded ten-minute cache service. `RiotGameDataService.EnrichAsync` owns the single automatic-guide call and starts it in parallel with visual metadata; `MayhemAutomaticGuideService` must not call the same service first and then enter Riot enrichment. Available/fail-closed balance text is projected into a dedicated companion section without a periodic balance poller.

This is the preferred pattern for future lightweight parity work: first reuse an existing response/cache/owner, then expose more of it. Do not buy UI richness with duplicated background work.

## D-018 — Bench availability does not define ARAM Mayhem

**Decision (2026-09-10, PR #283):** mode-specific Runtime Companion data must be selected from queue/mode context, not from `benchEnabled` alone.

- ordinary ARAM remains queue 450 / ARAM and uses Build Advisor plus a version-bound base-ARAM balance-only supplement;
- ARAM Mayhem is recognized separately by observed global queue 2400, CN/WeGame queue 3270, or `KIWI` / `ARAM_MAYHEM` mode tokens and may use the full Mayhem build/augment pipeline;
- Bench remains only a capability signal for quick swap; it is not sufficient evidence that Mayhem-only recommendations apply;
- unsupported or unrecognized Bench modes fail closed and do not start Mayhem external work;
- ordinary ARAM waits for its matching Build Advisor version before the balance request so the base-balance parser can retain patch-mismatch semantics.

This prevents a UI similarity feature from changing data truth: normal ARAM must never show Mayhem augments merely because both modes have a Bench.

## D-019 — Quit Champion Select without closing the lobby

**Decision (2026-09-11, PR #283):** the Runtime Companion may expose a one-click `退出选人` action, but it is a narrowly fenced Champion Select transaction rather than reuse of the process-killing `close-lobby` action.

- first use `POST /lol-lobby-team-builder/champ-select/v1/session/quit`; after a definite HTTP 400 only, a single `POST /lol-gameflow/v1/session/request-lobby` is allowed if original party identity and members were readable;
- never call `DELETE /lol-lobby/v2/lobby` and never kill `LeagueClient`, `LeagueClientUx`, or `LeagueClientUxRender` for this workflow;
- require a live ChampSelect preflight;
- never retry an uncertain response, send at most one original quit and one guarded fallback per explicit click, and recheck that the phase is still ChampSelect before fallback;
- require Lobby phase, absent ChampSelect session, and (when the original party was readable) unchanged lobby identity and membership before reporting success;
- keep the action behind a dedicated write interface sharing the existing League session, not the generic build writer;
- leave League's own dodge/queue penalty semantics untouched and communicate that in the UI tooltip.

This provides the Akari-style convenience the user asked for without weakening the product's write-target fences or lightweight single-session architecture.

## D-020 — GGman 3.5.40 is the first production release under the new public brand

**Decision (2026-09-17, PR #285):** `v3.5.40` is the first formal release whose public executable, Windows product identity, release title and online-update asset are GGman / `GGman.exe`.

Production release must continue to use the canonical lightweight publisher, signature verification, public-byte/hash/signer re-verification, disabled-before-publication manifest staging, and post-verification online enablement. The brand change does not weaken any release safety gate.

## D-021 — Cloud identity uses a random portable device id, not a hardware/IP fingerprint

**Decision (2026-09-25):** GGman cloud identity starts with a randomly generated stable `device_id` stored beside the portable application, then maps that device to a CloudBase anonymous-auth `sub`. Hardware fingerprint recovery is not part of the authoritative identity.

- The device id lives under the persistent application-local `data` directory so copying the whole GGman folder preserves the portable identity.
- Public IP, MAC address, disk serial, motherboard serial and similar machine identifiers are not part of the normal identity key.
- CloudBase anonymous authentication is the cloud ownership source; PostgreSQL rows use `auth.uid()`/JWT `sub` and RLS rather than trusting a client-supplied owner id.
- P1 persists no access token or refresh token. A process may refresh an in-memory session when possible; after restart it signs in again using the same stable device id.
- The client never embeds a server API key, service-role token, Tencent SecretId/SecretKey or database administrator password.
- Future recovery may use an explicit recovery code and a privacy-minimized hashed hardware signal as secondary evidence, but neither can silently replace the cloud owner identity.
- User settings, account history and telemetry are separate later sync scopes and must preserve the same least-privilege/RLS boundary.

This choice favors predictable portability, privacy, and recoverability over brittle device fingerprinting while keeping a path open for a future real account system.

## D-022 — Personal history is local-first; anonymous ranking is explicit opt-in

**Decision (2026-09-26):** GGman personal stats keep their durable source on the user's portable application directory and upload only privacy-minimized account hashes when the user enables anonymous ranking.

- Local history records active days and unique played-account hashes under `data/personal-stats.json`; raw PUUID, Riot account name, password and LCU credentials are never persisted in this store.
- Account keys are HMAC-SHA256 values derived from the portable random `device_id` and the current PUUID. The raw PUUID is discarded after derivation.
- Local personal stats default on because they are application-local and user-visible. Cloud ranking defaults off and is independently switchable.
- Ranking is computed server-side from opted-in users only. The client receives only its own count/rank/population/percentile aggregate and cannot enumerate other users through RLS.
- The current lightweight line does not add SQLite/native runtime dependencies merely for this feature. The JSON store uses the same application-local atomic/LKG durability pattern as other small portable state. A later SQLite migration must justify its release-size/runtime cost and include deterministic migration from this schema.
- Turning a feature off stops future collection/sync; it does not silently erase existing local history. Destructive deletion, if added later, must be an explicit user action.

This keeps the user-facing retention value (history, active days, rank, future summaries) without making cross-user tracking or hidden device fingerprinting a prerequisite.


## 2026-10-09 — Floating entry is GGman's primary shell

Decision: retain the 56px floating-ball/tray entry as the default lightweight user interaction. The quick launcher is transient and the full workbench opens only on demand; do not make a full-size dashboard the default or always-visible window. Initial UX redesign ships incrementally in numbered, immutable, updater-compatible 3.5.x public versions after the normal Windows/UI text/League safety gates, instead of distributing out-of-band review EXEs as the only delivery mechanism. Rationale: player context switches should remain quick and unobtrusive, and historical changelogs/releases provide traceable updates. First scoped step v3.5.50 targets only quick-launcher presentation and typed menu ownership; existing League Gameflow/LCU writes remain unchanged.


## 2026-10-09 — Single navigation rail for the on-demand LOL workbench

The GGman floating ball remains the default process entry. When the optional LOL workbench opens, it uses a single grouped left navigation rail and one content canvas. Remove the persistent top subnav and right context dock, which compete with match history and guide content. Preserve all nine existing view IDs, nested form factories, task ownership and external contextual requests; keep player profile visually separated without changing legacy internal section identifiers. This trades a slightly wider rail for direct access and more stable content width. The original on-demand workbench is not made a startup surface.


## 2026-10-09 — Readability-first, data-dense player history

The player history page should keep a high-density virtual ListView rather than wrapping every match into a heavy decorative card. Use responsive geometry inside the on-demand workbench, neutral text for match metadata, and semantic win/loss color only for the result cell. Empty and loading states must remain distinguishable and should reuse existing localization keys. Preserve data service ownership, cache, paging and cancellation. This is a presentation release, not a change to how matches are fetched or counted.


## 2026-10-09 — Responsive, sober recommendation controls

Preserve recommendation selection, confirmation and LCU write ownership in `LeagueRecommendationForm`, while moving its pure presentation onto existing `FacmDesignSystem` tokens. At narrow embedded workbench widths, let selections and previews wrap to two or one column and scroll vertically; keep the action/status footer visible so important applied/rejected feedback is not lost off-screen. Reuse existing UI text keys, keep actual client/hero state authoritative and distinguish failed/partial/succeeded results without implying success on a rejected or uncertain write. This is a UI-focused 3.5.53 patch, not a replacement of the underlying recommendation service.


## 2026-10-09 — Preserve native companion lifecycle while unifying Mayhem chrome

The floating ball remains GGman's primary unobtrusive process entry. The Mayhem lookup uses its proven responsive policy instead of another form rewrite. Standardize feedback, accessible button naming and public branding within that page. Keep the ChampSelect quick assistant's explicit non-activating, Bench-gated compact/expanded lifecycle and game actions intact, while replacing its private hardcoded visual palette with common design tokens and semantic result colors. Treat the narrow runtime companion as a separate high-risk lifecycle surface for a later narrowly verified release; do not conflate styling checks with real LCU interaction validation.


## 2026-10-09 — Make hidden-overflow companion content discoverable without taking more space

Keep the in-game companion narrow, lightweight, nonactivating and under existing pin/collapse/window-state ownership. Since its native body scrollbars are hidden and recommendations can exceed the ~360–420px panel, display a slim passive progress rail adjacent to the scrollable region rather than adding tall navigation chrome or a secondary scroll controller. This uses the existing scroll offset only, with no additional Gameflow/LCU reads. Preserve the current density, revealable recommendation alternatives and game actions, while improving screen-reader button labels and direct guidance that more recommendations exist below the fold.


## 2026-10-09 — Regression-first after successive GGman UI releases

After 3.5.50–3.5.55, prioritize verifiable clipping/overlap and interaction regressions instead of adding more screens. The recommendation footer is deliberately two-row below 480px so error/success text cannot sit under the Refresh/Apply actions; above this threshold, preserve the existing single-row footprint. Continue to use a pure WinForms layout policy with deterministic geometry checks and no new custom rendering, data reads or League controller ownership. Keep floating-ball-first entry, workbench routing and on-demand runtime companion behavior unchanged.


## 2026-10-09 — Resolve deep-link targets before building Hub child forms

Workbench contextual launch is a typed initial-state configuration, not a second navigation event. Select the first requested valid route on `LeagueHubForm` before `Shown`, retaining Dashboard for ordinary/unknown requests. This prevents avoidable child creation/disposal, reduces extraneous load from direct menu navigation, and does not change the on-demand Hub's user-facing navigation controls, forms or Gameflow ownership.


## 2026-10-09 — Consolidate My GGman in the existing responsive workbench canvas

The My GGman history, metrics and opt-in preferences are one user task, not several new windows. Keep the existing page and its local/cloud owners, but let its children fill the embedded workbench width with a vertically scrollable content panel. Allocate summary metrics in three bounded slots; reserve sufficient content height for the status line. Keep cloud ranking explicitly opt-in and avoid reading or sending any additional account information merely to improve the page presentation. Validate layout with a pure geometry policy and the existing host smoke instead of inventing a second UI framework.


## 2026-10-09 — Update Center treats notes, availability and failure as distinct states

A verified update's description is informational text, not a transient progress/error status. Keep full notes in a scrollable native read-only text box, and reserve the small status label and semantic badge for verified availability, check/download progress or an actual error. Never display an unverified initial state as “up to date”; never re-enable an install action using a previous successful manifest after a later metadata failure. Keep the existing signed updater and forced-update lifecycle unchanged. Prefer a compact layout within the present 560×620 dialog rather than creating an additional settings window or migrating UI technology.


## 2026-10-09 — Onboard through the existing floating launcher, not a new welcome app

For the 3.5.59 first-use slice, use the pre-existing settings-creation signal and a small dismissible card inside the four-tile launcher. Automatically reveal the launcher only for genuinely new installs and never during suppressed gameflow or cleanup startup; return to normal menu sizing after dismissal. Existing/migrated/recovered users should receive no surprise first-run prompt. Keep the guide recoverable from the existing Settings popup instead of adding a permanent onboarding page, cloud identity flow or settings-migration flag. Preserve typed workbench routing and the original floating ball/tray lifecycle.


## 2026-10-10 — Opening an existing panel must not be interpreted as a toggle

Keep a deliberate behavioral distinction between the floating ball (click to toggle) and explicitly named **Open** commands in native tray menus (open or activate). A tray **Open control center** command or tray double-click must never close an already-visible launcher. The legacy compact League navigation should call the current typed Hub bridge, not rely on the side effect of a shell group lookup and then report a false “unavailable” status. Share the lightweight getting-started card between settings popups and the tray's secondary More menu, without adding a new top-level entry or duplicating settings storage. This is an entry/feedback consistency correction; no automation, overlay or League data-owner changes are required.


## 2026-10-10 — Register real GGman user identity before ESC cloud storage

For account-enabled ESC sync, user consent and portable ownership are required before remote data upload. Implement independent email OTP account login using CloudBase's official email send/verify/sign-in/sign-up API and treat the returned registered `sub` as the only acceptable cloud sync owner. Do not repurpose the existing anonymous `x-device-id` identity or silently transfer old anonymous data. Keep login optional and first-stage credentials in memory only: this avoids introducing unreviewed local refresh-token persistence on shared PCs. A later opt-in remember-device design requires Windows DPAPI per-user encryption and verified backend revocation. The identity feature was merged in PR #314, followed by registered-only ESC cloud schema/UI in PR #315 and signed v3.5.61. This does not establish real two-account gateway isolation or Riot settings persistence, which remain follow-up acceptance (Issue #313). Because the underlying CloudBase OTP endpoint is public, a client-only resend clock is not a business-wide email-volume ceiling. Retain provider-owned per-address/IP limits and optionally introduce an audited, privileged server gateway only if CloudBase cannot satisfy required per-project rate budgets.
