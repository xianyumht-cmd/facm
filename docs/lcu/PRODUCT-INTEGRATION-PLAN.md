# GGman — LCU Research Product Integration and Release Execution Plan

**Date:** 2026-11-28 (revised after research-author review)  
**Status:** Implementation in progress on the task branch; production release remains separately gated.  
**Task branch:** `feat/champ-select-mute-20261128`  
**Audited executable baseline:** `9743f5e2f8c2c4b5e9979571810a561b6d5847a7`  
**Main at audit:** `4c8a55fb7175e8b020747bdfcee3a4703f43e199` (task branch ahead 5, behind 0 at that time).  
**Existing production manifest at audit:** 3.5.90, enabled, non-forced.  
**Authority:** AGENTS.md and canonical project knowledge take precedence. This plan does not authorize merging, production publication, changes to security software, or live-client experiments.

## 1. Objective and delivery contract

Convert the November 28 LCU research into complete, useful, maintainable GGman player-facing features rather than accumulating additional disconnected services. Keep the existing .NET Framework 4.8 native backend, Shard ownership model, compact WebView2 workspace, existing native fallback and signed single-EXE delivery. Do not migrate to Electron, Tauri, or a second production application architecture.

A capability is complete only when the correct existing business owner exposes a validated contract, the intended player can discover and use it in the appropriate game phase, cancellation and account/session transitions are handled, unavailable data is distinguished from actual zero, and the relevant tests and release gates provide evidence. Compilation alone is not completion. Do not add controls that silently do nothing. Use concise Chinese product labels and the existing UI text contract; keep English technical documentation.

Reuse research evidence for legitimate, user-authorized functionality through existing local-client interfaces. Respect platform restrictions and server-side eligibility. Do not circumvent matchmaking penalties, hidden player identity restrictions, anti-cheat, or access controls. Treat process injection and intrusive research utilities as isolated experimental material for this delivery scope, not a dependency or prerequisite of the production product. This is a release boundary, not a permanent decision to abandon the underlying UX goals; see WP4. No work in this plan requires loading code into a third-party process, disabling security controls, or changing the game process.

## 2. Evidence inventory and baseline findings

The five source commits are d6c81f2a, 813ea805, e6fa09e8, d46f9c11 and 9743f5e2. Review docs/lcu/README.md, CAPABILITY-MATRIX.md, FEATURE-PLAYBOOK.md, PLUGIN-BUNDLE-INDEX.md, PLUGIN-CAPABILITIES.md, endpoints.csv and the relevant schema exports. The reported corpus contains 48 unpacked plugins, 1,432 HTTP routes, 680 events and 3,569 types. Counts are research artifacts, not a promise that every operation is supported on every client version or account.

The source review found:
- LeagueClientModule constructs LeagueInsightService, but exposes no corresponding consumer-facing entry point; its reference is not cleared during disposal.
- LeagueRuntimeCompanionController and LeagueRuntimeCompanionForm connect the mute operation. Actual WebView2 route and native fallback parity remain to be traced.
- LeagueChampSelectMuteRuntime has unused muted-state parsing, uses state toggling rather than guaranteed mute semantics, manually constructs JSON and parses identity values with Int32.
- LeagueRewardsOverviewService can report availability for one successful request while the other silently retains default numeric values.
- LeagueInsightService launches six feature groups concurrently without phase-specific demand; failure in a constituent task can fault the aggregate.
- LeagueSocialService contains a block-list owner and writer; module exposure and actual consumer wiring were not established.
- Existing C# and independent native research components cover overlapping operations. Parallel active ownership is a correctness risk; no live conflict was reproduced in the audit.
- The legacy C# champion-select exit owner contains behavior that no longer matches the research conclusion regarding server eligibility. Restrict the product to supported client behavior; do not attempt alternative ways to evade restrictions.
- The native research README documents an independent runtime, and its incident report records a roughly 4.9-second stalled UI loop. Its size and prior compilation do not establish runtime safety, compatibility, or production readiness.
- The official C# publisher has signing, asset verification and manifest activation stages. No matching PR workflow runs were returned for the audited executable SHA by the available query. This is not proof that all possible CI runs are absent.

Before implementation, refresh main and task HEAD, inspect active PRs and concurrent changes, read nested AGENTS.md files and canonical docs, and enumerate actual Shard registrations, source/test locations and workflows. Do not rewrite divergent history. Record exact evidence rather than treating this preliminary audit as exhaustive.

## 3. Target ownership and integration design

Maintain a single authoritative C# owner per player operation. UI adapters consume typed snapshots and existing allowlisted actions; they do not open their own authenticated transport or introduce independent pollers. Use existing Gameflow, session discovery, request budgets, settings, performance budgets, and cancellation conventions. Map each new service to an existing Shard or adapter before considering a new one. Keep UnifiedWorkspaceForm responsible for window lifecycle, navigation and dispatch, not business logic.

For each capability create a source-backed integration row: current owner, existing duplicate, target owner, input contract, phase, identity fence, source availability, refresh trigger, UI route, fallback, test ID and observed status. Trace registration to actual invocation, not merely class references. Reuse equivalent existing ranked, social, queue and history projections where available. Prefer narrow changes over generic managers.

Treat read results as available, partial, unavailable or stale according to existing project contracts. Preserve missing-vs-zero semantics at field level, with source timestamps when useful. Reject stale generations, invalid identities and mismatched account/game contexts. Never expose raw authenticated payloads or credentials to WebView2, logs, diagnostics or fixtures. Writes must be deliberate, scoped to supported behavior and report observed results rather than assumed success.

## 4. Implementation work packages (ordered)

### WP0 — Inventory and establish acceptance baseline
- Inspect the actual default branch, feature branch, worktree, open PRs and CI. Inventory all active Shards and compare against the prior 23-Shard baseline; do not assume the count is immutable.
- Trace the existing client module, Gameflow, companion, profile, social, matchmaking, history, WebView2 bridge and native fallback entry points.
- Produce a capability-to-owner matrix with duplicates, omissions and source references. Classify each research result as implemented, documented only, unsupported, unverified or not applicable.
- Capture existing build/test commands and performance baselines before changing runtime code. Label missing environments and evidence explicitly.
- Exit: exact baseline recorded, affected owners and tests identified, no unrelated release/config changes.

### WP1 — Correctness of existing player-facing operations (P0)
- Runtime evidence from ef93da84 (Tencent 16.23, Practice Tool, synthetic identity) demonstrates that the legacy muted-players collection adds and removes the four-field identity in response to toggles. It does not establish real-human PvP or atomic behavior against concurrent manual changes. The researched toggle-player-muted operation is a toggle, not a set-muted operation. Read muted-players on demand and establish whether its response is reliable for the current session and identity; an empty response alone is not proof that all players are unmuted. Where reliable state exists, offer an explicit idempotent mute action that does not unmute already-muted players. Where reliable state is unavailable, abort the bulk action and report unavailability. A separate manual toggle is not part of this delivery slice; never silently fall back to it. Never schedule automatic toggling or retry an ambiguous write. Use the existing authenticated client owner and structured serialization; validate identifiers without narrowing their numeric range or resolving hidden identities.
- Preserve intentional scope, exclude the local player, handle duplicate or incomplete identities, cancellation, partial success and session changes. Do not treat HTTP success alone as proof of settled state.
- Trace the native companion and active WebView2 command path, including localized labels, accessibility, phase eligibility, busy-state release and window disposal.
- Use the team-builder champion-select session.showQuitButton as the primary server-supplied eligibility field, as documented in QUIT-INVESTIGATION.md and FEATURE-PLAYBOOK.md. Read on phase entry from the existing session snapshot and revalidate on state changes and before an explicit quit. Enable only when the field is explicitly true and the current phase, session and relevant UI context are valid; missing/false/stale is unavailable. Do not probe eligibility by issuing a write expected to fail. The researched request-lobby response of HTTP 200 with false is not success or a supported alternate exit. Reconcile legacy C# behavior accordingly; the server remains authoritative and can still reject an eligible-looking request. No forced-disconnect or process-termination fallback.
- Exit: synthetic tests for already-muted, mixed-state, repeated request, empty/unreliable state, explicit toggle labeling, cancellation, phase/session changes, partial errors, and showQuitButton true/false/missing/stale. No penalty-bypass behavior.

### WP2 — Service consolidation and data contracts (P0/P1)
- Expose LeagueInsightService through an appropriate narrow existing module contract or replace it with existing owner-specific projections where that better fits architecture. Do not add an unused public interface merely to increase coverage.
- Clear owner references and cancel outstanding work according to established lifecycle rules.
- Reconcile LeagueRankedStatusService with existing profile/ranked snapshots and history ownership; reuse the current-player data path and avoid duplicate requests.
- Reconcile LeagueQueueStatusService and LeagueLeaverBusterService with the existing matchmaking/Gameflow owner. Reuse one /lol-matchmaking/v1/search response, with errors[] as the observed source for QUEUE_DODGER and other reported restrictions (33b3e01a); notifications absence does not establish eligibility. Distinguish no reported restrictions from confirmed eligibility. Distinguish normal queue estimates, low-priority status, absent values and unknown status.
- Reconcile LeagueFriendActivityService and LeagueBlockedPlayersService with existing social capabilities. Restrict data to what the current account is permitted to see. Expose intentional block/unblock actions using existing session and error conventions; do not automate them.
- Reconcile LeagueEndOfGameStatsService with current history and postgame models; associate the result with the correct match and current account.
- Repair LeagueRewardsOverviewService per-source availability, distinguish collection counts from owned quantities, and verify current task schema against research artifacts. Do not expose automated claiming, purchasing or other transactions as part of this plan.
- Make aggregation demand-driven and bounded. Use the existing cancellation and performance budget, isolate constituent failures, and allow partial snapshots.
- Exit: all nine additions have an explicit disposition and real consumer or a documented replacement; no redundant active writer or periodic network owner.

### WP3 — Phase-aware UI and Shard integration (P1)
- First delivery slice: after WP0 confirms existing ownership, integrate the six read-only feature groups (penalty, queue, friends, postgame statistics, rewards, ranked/LP) into one compact tab in the existing Efficiency surface, reusing existing cards, native owners and snapshots. Render phase-relevant groups first; load optional groups only on entry or explicit refresh. Do not add six separate pages, six pollers, or duplicate existing functional views. Use a single typed snapshot contract only where it avoids duplication. The champion-select mute and social block/unblock remain explicit, separately scoped actions in their appropriate existing surfaces.
- Second delivery slice: add minimal phase-context summaries or deep links only where the existing workspace already offers a relevant surface and the data materially benefits the player. The following phase mappings describe intended behavior, not a requirement to create separate new UIs for every item.
- Disconnected: show existing connection state; no stale account data and no repeated feature queries.
- Lobby: expose current ranked summary and penalty status through existing profile/efficiency surfaces. Display friends through the existing social entry; load rewards and block list only when opened or explicitly refreshed.
- Matchmaking: add compact elapsed/estimated wait and applicable low-priority information to the existing phase shortcut or matching surface. Unknown estimates must remain unknown.
- Champion Select: keep the companion compact; add the corrected mute action to the active presentation and native fallback. Existing build, guide and history routes retain ownership and behavior. No extra top-level control center or injected overlay.
- In game: suspend lobby-only reads and do not overlay the game or introduce game-process access.
- Postgame: show the available result, actual LP change and essential match statistics in the existing postgame/history flow; do not guess missing metrics or imply an LP change before evidence arrives.
- Back/Home, focus, skin, DPI, compact layout, localization and loading/error states must match existing UI conventions. Keep user actions single-click where appropriate under the established product rules; do not remove genuine integrity guards.
- Exit: each approved feature has a discoverable path from the shipping UI, with a typed bridge contract, deterministic UI tests, and no independent transport from the renderer.

### WP4 — Independent native research boundary and resource assessment (P1)
- Product direction checkpoint (owner decision pending): in-client inline controls, advertising-related presentation changes and deeper visual integration are requested UX goals, not approved deliverables of this particular work package. Keep them in the backlog with feasibility, compatibility, authorization and maintenance risks explicitly recorded. Do not represent experimental code as shipped or declare these UX goals permanently abandoned. The current candidate remains an external GGman application using existing supported interfaces; consider app-owned companion UI and officially supported extension mechanisms for comparable experiences. Any change of product scope requires a separate decision and review, and does not block WP1–WP3 or WP5.
- Treat injected-agent/ and its related tools as an experimental source tree. Audit whether any project item, build script, packaging glob, startup configuration, manifest or distribution rule accidentally includes experimental components. Report any inclusion and keep them out of the production build.
- Use historical incident records to identify general reliability lessons: synchronous work on UI threads, blocking I/O, missing resource ownership, unmanaged handles, unclear shutdown, and competing business owners.
- Apply relevant lessons to GGman's own processes and supported local-client integration. Measure application-owned CPU, memory, handle count, responsiveness and request frequency on isolated synthetic fixtures or GGman's own test harness.
- Do not extend, deploy, optimize or operationalize process injection, anti-cheat interaction, intrusive probes or penalty circumvention. Existing research is evidence, not production acceptance.
- Exit: production dependency/package audit confirms separation; risks and unverified experimental runtime behavior recorded. Any future native component proposal must be independently reviewed as an app-owned, non-injected architecture.

### WP5 — Regression and performance verification (P1)
- Run narrow existing and new synthetic smoke tests before the full build. Use sanitized fixtures for valid, malformed, absent and partial results; include large identifiers, unknown enums, empty collections and actual zero values.
- Test session transition, account switching, reconnect, cancellation during page navigation, duplicate clicks, disabled actions, partial responses and disposed owners. Verify reads do not cause mutations.
- Compare existing and candidate builds under the same hardware, phase, fixture, duration and sampling method. Cover idle, disconnected, lobby, matchmaking, champion select and postgame; record CPU, private working set, handles, threads, request counts, latency and UI responsiveness. Do not infer performance from binary size.
- Preserve existing no-idle-polling and request-budget contracts. As a proposed review trigger, investigate a reproducible >10% regression in nonzero steady-state metrics or any material new idle work; report absolute values, variance and measurement limits instead of treating this threshold as an established product SLA.
- Run the Windows Build, Glass WebView2, UI Text Contract and Mayhem Source Probe gates plus applicable mainland route, COS and EXE audit workflows on the exact final executable SHA. Enumerate actual workflow names and triggers from the repository.
- Exit: test matrix contains pass/fail/not-run evidence and exact SHA/run links; no unresolved correctness, session isolation, build or updater integrity failures. Do not claim real Tencent CN or Win10 DPI evidence from hosted fixtures.

### WP6 — Release readiness, signing and publication (P2; separate authorization)
- Re-read AGENTS.md, docs/OPERATIONS.md, current main, manifest, release request, public configuration and active workflow results. Determine next unused version only after this check; do not assume 3.5.91 is available.
- Inspect .github/workflows/publish-3.5-lightweight.yml and its invoked build/smoke scripts. Confirm existing single-EXE resources and expected dependencies only; preserve legacy compatibility asset parity.
- Verify feature flags, updater routing, cached signed configuration, offline fallback, version/rollback behavior and signature validation remain intact. Do not change COS or online policy unless required and authorized.
- Once separately authorized, use the existing reviewed PR and official publisher. Require green exact-head gates and normal signing. Verify public GGman.exe and FACM.exe sizes, SHA-256 values, expected signer, version metadata and updater manifest activation; keep the manifest disabled for the new release until public verification succeeds.
- For any failure, halt activation and follow the existing documented recovery procedure. Never overwrite an immutable released version or bypass signing. Prepare concise Chinese release notes listing actual changes and known limitations.
- Operator installation/gameplay acceptance may be deferred only when the owner explicitly waives it for this release. This does not waive CI, signing, update integrity or honest disclosure of unmeasured runtime risks.
- Exit: publication evidence recorded only if actually authorized and executed; otherwise deliver a release-ready review candidate.

## 5. Feature acceptance matrix

| Capability | Target surface | Required evidence |
| --- | --- | --- |
| Explicit mute / clearly labeled manual toggle | Existing champion-select companion | Repeat action, mixed state, unreliable state, partial failure, account/phase fence, WebView2 and fallback |
| Queue estimate | Efficiency overview; existing matchmaking phase surface where useful | Known/unknown estimates, reconnect, no additional idle polling |
| Penalty status | Efficiency overview; existing lobby status where useful | Valid/absent status, no unsupported bypass |
| Friend activity | Efficiency overview; existing social surface for details | Current-account permission, stable unavailable state, on-demand refresh |
| Postgame stats | Efficiency overview; existing history/postgame surface for details | Exact match identity, source-consistent stats, missing-vs-zero |
| Ranked / LP | Efficiency overview; existing profile and postgame where useful | Queue identity, known/unknown LP change, no duplicate request |
| Block list | Existing social management | Explicit block/unblock, stale session rejection, result feedback |
| Rewards overview | Efficiency overview, on demand | Partial source availability, accurate counts, no transactional automation |
| Insight aggregation | Native owner only | Bounded demand, partial failure, cancellation, actual consumer |

## 6. Execution tracking and stop conditions

Each work package should produce a small coherent change on the existing task branch, with actual test evidence and concise canonical documentation updates. Avoid cosmetic-only commits, unrelated dependency upgrades, duplicated knowledge documents and artificial branch creation. At each checkpoint inspect HEAD and concurrent changes. If an owner already provides equivalent behavior, integrate or retire the duplicate rather than preserving two competing implementations.

Track progress as Not started / In progress / Implemented / Verified / Blocked. A feature marked Verified must cite exact source SHA, test and workflow evidence. Mark real-client, physical hardware and regional behavior separately. Update docs/PROJECT_STATE.md, ARCHITECTURE.md, PITFALLS.md, DECISIONS.md and OPERATIONS.md only where the implementation actually changes their facts.

Stop release progression for identity/session correctness defects, unsupported operations, accidental experimental component inclusion, signature or hash failures, broken updater behavior, unexplained performance regressions, or missing required final-head CI. Report the exact blocker, affected scope and next safe action. Do not publish on the strength of a plan, a local compilation report or an earlier green commit.

## 7. Definition of done

All research-derived capabilities within the approved product scope have an explicit ownership and user-facing disposition; implemented functions work through the existing Shards and active UI; no competing active business owner or experimental injection dependency is shipped; phase/account boundaries and partial-data semantics are preserved; deterministic tests and exact-head CI pass; performance evidence is reported with its limitations; documentation and release notes reflect verified facts. Production publication remains a separate authorized step through the established signing and update pipeline.

## Execution checkpoint — 2026-11-28

The executable baseline `03abbebd` passed all four required PR workflows (Windows 46128879004, Glass 46128879002, UI Text 46128879007, Mayhem 46128879033). This is hosted verification, not live client acceptance.

### Ownership reconciliation (source inspection, not a whole-repository runtime trace)

| Capability | Existing owner / overlapping read | Integration disposition |
| --- | --- | --- |
| Mute and quit | LeagueRuntimeCompanionController, existing companion presenters | Shared supported C# action path; no second active writer |
| Queue and restriction | LeagueMatchmakingAutomation.ReadSourceQueueBlockAsync and readiness flow | Overview uses one shared search response; automation retains fresh action-specific reads; do not cache or replace those safety checks |
| Friends | LeagueFriendInvitationController.ParseFriends | Overview is read-only aggregate; existing invite owner retains its identity/lobby checks; no new periodic reads |
| Ranked | LeagueRankedStatusService | On-demand standings; do not attribute an uncorrelated LP notification to a match |
| Rewards | LeagueRewardsOverviewService | Separate bounded loot and mission failures; unknown mission status not interpreted as zero |
| Postgame | LeagueEndOfGameStatsService, existing history and postgame owners | Overview verifies game ID and projects local-player known fields; history and postgame automation remain independent |
| Block list | LeagueSocialService (currently unwired) | Read route reported HTTP 200; write descriptions conflict (README vs playbook); sanitized schema and normal-action contract evidence required before enabling management |

The generated endpoints.csv is based on function-name transformation and is not proof of literal HTTP path or request schema. Obtain sanitized read and normal-action evidence for the block-list contract, plus a ranked postgame LP payload with match attribution evidence, from the research workstream. Do not infer current-match LP from notification arrival alone.

### WP4 packaging boundary (static inspection)

FACM.sln references FACM, ToolBundle, PetHost and Updater. FACM.csproj's resource list and project references include the existing application resources, pinned WebView2 loader, tools, updater, and optional PetHost; the inspected ToolBundle project and tools manifest do not include the experimental native tree. The publisher builds FACM.sln, signs the lightweight EXE, copies identical compatibility assets, and checks public bytes and signer before activation. No injected-agent project or distribution reference was observed in those inspected paths. This is a source-level boundary check, not a binary or live-process audit. Experimental runtime is not advanced or deployed in this task.

### Product integration follow-up

The plugin route index (PLUGIN-CAPABILITIES.md lines 317–318, 3847, 3859–3860, 3902) independently corroborates the parameterized block-list removal route, whereas the README's shared POST/DELETE body description is insufficient. The verified read route is now consumed by the existing friend overview only. Count distinct string IDs for a valid collection, report unknown for unrecognized payloads, and do not expose IDs or write operations. Nonempty collection schema remains a runtime assumption requiring confirmation; malformed shapes degrade safely. Full block/unblock management and match-attributed LP remain blocked on evidence, not compilation. Ranked presentation reuses existing Chinese tier copy; absent tier and explicit unranked are distinct. Postgame attribution rejects zero local identities. Optional reads respect caller cancellation and individual deadlines.
