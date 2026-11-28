# GGman — HLMD Full-Coverage Implementation, Enhancement and Productization Master Plan

**Owner-approved:** 2026-11-28. **Scope:** existing-feature strengthening plus all 61 original HLMD A–I capability IDs, all FEATURE-PLAYBOOK operations, six additional L1 groups and explicit J limitations. **Baseline:** production 3.5.91, main d198c3d95619fa36f6dc4c567832cdfc2d38506c. **Task branch:** feat/hlmd-full-coverage. This supersedes the narrower historical integration scope; the previous plan is preserved at [e15da519](https://github.com/xianyumht-cmd/facm/blob/e15da519/docs/lcu/PRODUCT-INTEGRATION-PLAN.md). Preparation of a release-ready candidate is authorized; production publication requires separate owner authorization.

## 1. Mission and evidence

HLMD is the collective name for the November 28 Tencent League Client 16.23 research corpus. Use it as the primary engineering reference to repair ALL existing GGman features and implement the full planned capability set. Do not arbitrarily omit a feature for effort, complexity or personal preference. Where exact mechanisms are unavailable or would circumvent platform restrictions, implement the closest legitimate equivalent and record the remaining difference. A substitute is not exact parity.

Mandatory inputs: README.md, CAPABILITY-MATRIX.md, FEATURE-PLAYBOOK.md, PLUGIN-BUNDLE-INDEX.md, PLUGIN-CAPABILITIES.md, endpoints-full.md, endpoints.csv, events-full.md, types-full.md, QUIT-INVESTIGATION.md, EXIT-SEARCH.md, MUTE-STATE-EXPERIMENT.md and the historical integration plan in this directory. Review injected-agent/README.md, dll/ggman_core.c, ggman_hud.c, panel_block.c, tests, assets, docs and relevant mock utilities for portable business logic, UI concepts, fixtures and reliability lessons. Keep intrusive experimental tooling, credential interception, anti-cheat interaction and access-control circumvention outside production. Research counts vary by snapshot (1,432–1,452 interfaces, 680–692 events, 3,569 types, 48 indexed plugins); distinguish resource paths from callable endpoints and actual evidence from proposals.

At each takeover read AGENTS.md, applicable nested rules, AI_WORKSTYLE.md, PROJECT_STATE.md, ARCHITECTURE.md, DECISIONS.md, PITFALLS.md, OPERATIONS.md and AI_CONTEXT.md if present. Verify actual main, active task PR, production manifest, current Shard registration, source and tests. Do not overwrite concurrent changes.

## 2. Architecture and traceability

Preserve C#/.NET Framework 4.8, WinForms host, WebView2, Shards, native fallback, signed single-EXE distribution and existing cloud-control/updater contracts. No Electron, Tauri or parallel production runtime. Keep UnifiedWorkspaceForm a lifecycle/navigation/dispatch owner. One authoritative business owner per operation; reuse shared Gameflow, LCU transport, settings, caches, event streams and cancellation. Verify the historical 23-Shard count. Demand-driven, phase-aware work; no redundant pollers, WebView environments or permanent background work.

Maintain one capability ledger for every existing player-facing operation, every original A–I ID, each playbook operation, all L1 additions and J limitations. Record original ID/name, exact source, official plugin, current and target owner, route/method/body/response, phase, eligibility, account/session/match fence, success readback, failure behavior, event, UI route, tests, executable SHA, runtime evidence and residual gap. Classify as runtime-verified, CI-verified, existing equivalent confirmed, partial, supported alternative, or externally blocked. No empty control or class-only completion. Do not conflate coverage with completion.

## 3. Phase 1 — Existing-feature correction (P0 first)

- **LCU connectivity:** validate runtime candidates and listening ownership; reject stale lockfiles; direct loopback independent of proxy; reconnect on restart/account change; cancel obsolete operations; separate TCP/TLS/HTTP/auth/discovery errors; bounded recovery.
- **Offline lock:** inspect LeagueOfflineStatusLockController, LeaguePresenceService, LeagueAutoReplyController, UnifiedWorkspacePresenceAdapter and LeagueEfficiencyModule. Existing baseline-only offline-to-chat/away transitions miss initial enablement, other availability values and reconnect. Apply user's enabled preference to current authorized session, read back actual state, support reconnect, coordinate existing writers, use bounded attempts and stop on rejection, cancel stale work. Evaluate partial update semantics of PUT /lol-chat/v1/me (chat, away, dnd, offline, mobile), preserve unrelated fields. Report effective local state, not presumed external visibility. Test initial online, all relevant states, stale events, disconnect, disable, refusal, rejoin, repeated signals and account changes.
- **Automatic honor:** inspect actual ballot schema, eligible allies/opponents, vote count, candidate identity, submission body, refresh and confirmation. Use /lol-honor-v2/v1/ballot, honor-player, ballot/refresh where supported. Current production evidence: HTTP 200 but 0/3 confirmed and empty choices. Wait for applicable ballot, exclude already processed/ineligible players, verify updated ballot, avoid ambiguous write retries. Coordinate return-to-lobby with honor completion. Test delayed/empty/expired ballots, partial outcomes, duplicate events, session changes.
- **Champion selection:** audit intent, pick, lock, ban, role preferences, fallback champion, unavailable champions, champion/position/pick-order swaps, ARAM bench and supported reroll, rune/spell/cosmetic preferences. Compare official combined PATCH with completed and championId against existing implementation. Validate current action and phase. Explicit per-route legacy/TeamBuilder compatibility; no wholesale prefix rewrite.
- **Mute:** retain idempotent read-then-skip, four identity fields, 64-bit IDs, local exclusion, roster/session fence, state verification and native/Glass parity. No blind toggle. Practice Tool synthetic experiment does not establish all PvP cases.
- **Exit:** use server-provided eligibility and verify actual phase transition. HTTP 200 false is not success; no penalty circumvention. Keep normal client-close action separate.
- **Postgame:** coordinate pre-end-of-game dismissal, ballot, honor, end-of-game stats, social operations, dismiss stats, play again and lobby return; no timing race or silent skipped outcome.
- **Process detection:** distinguish launcher/client/renderer/game; reconcile OS observation and Gameflow; handle delayed startup/exit and unknown evidence; correct floating-ball behavior without game memory access.
- **Social/profile:** online state, signature, login restoration, supported profile/cosmetic preferences, invitations, recent teammates, friend requests/groups/notes, block/unblock, private chat, quick messages, bounded auto reply.
- **History/companion:** basic/advanced history, details, ranked stats, mastery, recent champion performance, permitted premade indicators, tags, live/champion companion, quick selection, builds/runes and replays; respect privacy and hidden identities.
- **Mayhem/public data:** champion guides, augment rankings, builds/runes, patch news, provider health, cancellation, bounded deadlines, temporary circuit breaking, fallback, version/freshness-aware cache and independent failures. Verify Kiwi Hub evidence; do not invent provider statistics.
- Audit the remainder of the actual shipping feature inventory even if not named here. Trace UI to owner and verify behavioral equivalence.

## 4. Phase 2 — All 61 original capabilities

The IDs below are the complete A–I matrix, not a claim that every original mechanism is supported. Retain original descriptions in CAPABILITY-MATRIX.md; the production target below states legitimate implementation or alternative. Record exact differences and blockers rather than silently deleting any ID.

| ID | Capability | Production target and acceptance |
|---|---|---|
| A1 | One-click account switching | Supported launcher switch; authorized preferences only, isolate accounts, confirm identity. No credential interception. |
| A2 | Login failure diagnosis | Correlate sanitized client logs, network/version errors, reported restrictions; distinguish confirmed and inferred causes. |
| A3 | Startup popup reduction | Supported dismissal/preferences and clean GGman workspace; retain mandatory/security notices. |
| A4 | Startup page/background/music | Local theme assets, optional audio/volume, efficient GGman loading. |
| A5 | Multiple clients/accounts | Officially permitted concurrent sessions or supported switching; no launcher restriction bypass. |
| A6 | Reduce loading animations | Optimize GGman and supported optional animations; no queue/authentication bypass. |
| B1 | Advertising-free experience | Ad-free GGman workspace and supported official preferences; record official-lobby gap. |
| B2 | Integrated workbench | Existing contextual workspace for history, Mayhem, builds, social, utilities. |
| B3 | Shortcuts/command palette | Configurable search, commands, hotkeys, conflict/phase validation. |
| B4 | Hide/reorder modules | Pin, hide, reorder GGman entries with persisted layout. |
| B5 | Friend management | Groups, sorting, filters, notes, available rank/duration, explicit management. |
| B6 | Friend activity | Available mode, phase, champion and joinability; respect invisible-status/privacy restrictions. |
| B7 | Server/queue status | Available maintenance, connection, queue and regional status; unknown counts remain unknown. |
| B8 | Assets | Owned champions, skins, currency, loot, missions and supported claims. |
| B9 | Performance controls | GGman resource/animation controls and supported client settings; measured results. |
| B10 | Crash recovery | Bounded GGman recovery, session rediscovery, workspace restore, supported reconnect/relaunch. |
| B11 | One-click exit | Direct intentional close, phase/integrity safeguards. |
| B12 | Window enhancement | Position, scale, topmost, transparency, multi-monitor persistence. |
| C1 | Matchmaking readiness | Ready-check notifications/restoration; no artificial activity to defeat AFK enforcement. |
| C2 | Queue information | Elapsed/estimated time, low-priority state, available population; unknown preserved. |
| C3 | Multi-channel notifications | Desktop, sound, taskbar, optional configured remote alerts. |
| C4 | Minimize/restore | Configurable tray behavior and restoration at ready-check/phase change. |
| C5 | Queue prediction | Bounded local history, sample count, freshness and uncertainty. |
| D1 | Return-to-lobby control | Phase-aware companion, official eligible exit, accurate unavailable state. |
| D2 | Player intelligence | Available rank/performance/champions/premade/tags in companion; no hidden-identity bypass. |
| D3 | Matchups/counters | Patch-aware public champion data. |
| D4 | Outcome estimate | Optional transparent calibrated estimate from permitted data; uncertainty. |
| D5 | Countdown/voice | Synchronize client timer, avoid drift, configurable notifications. |
| D6 | Mute-all | Verified-state idempotent action; preserve previous mutes. |
| D7 | Champion/loadout adjustments | Valid user-configured actions during authorized windows; no timing abuse. |
| D8 | Protect teammate intent | Avoid banning visible teammate intent; valid fallback. |
| D9 | Build/rune recommendations | Existing companion context, valid user-authorized application. |
| D10 | Chat/emotes | Supported mute/preferences and GGman-local filtering. |
| D11 | Disconnect warnings | Legitimate session indicators; distinguish uncertainty. |
| D12 | Composition analysis | Visible composition damage/frontline/CC/range/roles. |
| E1 | Loading matchup overview | External permitted companion, no game-renderer modification. |
| E2 | Loading diagnosis/reconnect | Correlate phase/process/log evidence, supported reconnect. |
| E3 | Guide preload | Fetch relevant guide when context exists, cancel stale work, bound cache. |
| F1 | Live information | Authorized live data in lightweight external companion. |
| F2 | Key-event capture | Permitted event data and standard OS capture, configurable triggers/retention. |
| F3 | Communication shortcuts | Supported controls, prepared messages and authorized accessibility; no unauthorized input hooks. |
| G1 | Advanced postgame analysis | Damage, economy, CS, participation, comparisons, transparent scoring. |
| G2 | MMR estimate | Clearly labeled estimate from observable ranked data; no official hidden MMR claim. |
| G3 | Auto return | Supported animation dismissal, honor, stats and play-again sequence. |
| G4 | Social actions | Explicit report/block/unblock/add friend in current match context. |
| G5 | Honor preferences | Eligible recipients and actual vote count, confirmed outcomes. |
| H1 | Chat history | User-accessible local history, account isolation, search/export/retention/deletion. |
| H2 | Message history/deletion | Normal permitted local history, respect platform deletion/privacy semantics. |
| H3 | Presence/status | Supported online/offline preferences, signature and honest local presentation. |
| H4 | Friend notifications | Permitted presence events, deduplication and configurable alerts. |
| H5 | Communication management | Explicit individual/consent-based group communication; rate limits, no spam. |
| H6 | Filters/auto reply | Local keywords, bounded responses, loop/duplicate/account guards. |
| I1 | Multiple-instance architecture | Authorized independent contexts or account switching; link A5. |
| I2 | Developer tools | GGman-owned WebView2 diagnostics and isolated test harnesses. |
| I3 | UI extensibility | GGman-owned HTML/CSS/JS components and Shard bridge. |
| I4 | Response transformation | Typed projections and synthetic fixtures; no falsification of authoritative client/server data. |
| I5 | Request diagnostics/replay | Sanitized GGman request diagnostics and mock replay; no credential capture. |
| I6 | Version management | GGman update integrity, compatibility, rollback; respect mandatory League updates. |
| I7 | Resources | GGman themes, icons, fonts, backgrounds, sounds. |
| I8 | Cosmetic preview | Legitimate catalog previews separated from ownership/in-game availability. |
| I9 | Crash analysis | Sanitized GGman-owned crash diagnostics and available client-generated reports. |

### J — Server-authoritative boundaries

Retain explicit ledger entries for unsupported penalty-free exits, authoritative rank/MMR/win-rate/history changes, unauthorized champion/skin ownership or use, ban/freeze/device restriction bypass, matchmaking manipulation, match-result/penalty alteration. Legitimate diagnostics, estimates, previews or supported actions are not exact parity. Never imply a local display change is a server-state change.

## 5. Phase 3 — Full FEATURE-PLAYBOOK and L1 coverage

Trace every numbered playbook section and all sub-operations. Additional requirements beyond the matrix: team boost (explicit purchase); champion locks, rerolls, bench and all three swap families; lobby create/change/leave/start/cancel; invite/accept/decline/join/play again; kick/promote/invitation privileges; role preferences/spectator slots; custom bot add/modify/remove if supported; friend request/list/group/note/delete; block/unblock; chat/conversation/active state; presence and signature; skip pre-end-of-game, honor/refresh, dismiss stats and report; shop catalog, ownership, payment eligibility and batch purchases; loot recipes/material slots, craft/mass craft, redeem, milestones, refresh/new-item notices; missions/progress/claims/series and reward selection; spectator/replay launch; available security diagnostics from legitimate reports. Require explicit purchase intent and verify results, prevent duplicate claims/charges, preserve missing vs zero, respect eligibility and restrictions. Do not interfere with security controls.

All six additional L1 groups: account asset overview and supported reward collection; full friend management; match-data export; available server/queue information; locally rendered history share image; account/login security diagnostics using available legitimate evidence. CSV/JSON export where appropriate, no credentials or unrelated private data.

## 6. Phase 4 — Events and types

Use events-full.md and actual client event contracts for connection, login, account, presence, friends, chat, lobby, party, matchmaking, ready check, champion actions/swaps, Gameflow, postgame, honor, ranked and assets. Shared initial snapshots plus event updates, bounded resync on reconnect/missed events; deduplicate, cancel stale sessions, no socket per feature. Use types-full.md to preserve 64-bit identity, nullable/unknown fields, enum validation, malformed payload rejection, account/match ownership, structured serialization and compatibility. Prefer partial mutation when verified. Use sanitized fixtures, never credentials/raw private payloads.

## 7. Phase 5 — Product and UI

Place features in the correct player context: disconnected diagnostics; startup preferences; lobby profile/assets/social/workbench; matchmaking queue and ready check; champion companion; loading matchup/diagnosis; permitted live companion; postgame statistics/honor/social/return; settings for configuration, skin, performance and shortcuts. No one-page-per-feature proliferation. Preserve floating ball, unified workspace, concise Chinese labels, iOS-inspired compact presentation, themes, DPI, responsive layout, direct ordinary actions and visible close control. No raw enums/errors or placeholders. Keep necessary integrity/transaction guards.

## 8. Phase 6 — Performance and reliability

Measure host/WebView CPU, private/working-set/peak memory, threads, handles, startup/companion readiness, responsiveness, request frequency, cache size and long-session behavior. Reuse environment/controllers where appropriate; release hidden view work, demand-load, bound cache, share events. Investigate measured regressions; unmeasured is not improved. Distinguish disconnected/loading/available/partial/unavailable/unsupported/rejected/failed/stale/submitted-unverified/confirmed. HTTP status alone never establishes outcome; use response body/readback/event/phase as appropriate.

## 9. Phase 7 — Diagnostics and tests

Expand existing sanitized on-demand diagnostics: session discovery, Gameflow/process, offline preference/effective state/result, honor candidate/confirmation counts, selection/swap outcomes, postgame workflow, owner/Shard state, provider health, WebView metrics, updater and tool security errors. No tokens, headers, raw conversations or private IDs.

Test normal/invalid/missing/unknown/malformed, phase eligibility, rejection, timeout, cancellation, duplication, concurrency, disconnect/reconnect, account transition, stale result, disposal, partial success and cleanup. Focused deterministic tests first. Required Windows Build, Glass WebView2, UI Text Contract, Mayhem Source Probe and applicable host/history/cancellation/performance/updater/mirror/packaging/signing contracts. Check job execution (not merely green checks). Record exact executable SHA and actual status. No repeated owner manual experiments; consolidated field acceptance or explicit waiver, never claim it passed.

## 10. Execution order

WP0 actual baseline + complete traceability ledger; WP1 repair production defects (offline, honor, game detection, LCU, postgame and providers); WP2 full existing-feature HLMD audit; WP3 social/lobby; WP4 matchmaking/champion selection; WP5 loading/live/postgame; WP6 inventory/missions/rewards/store; WP7 workspace/system; WP8 performance and consolidation; WP9 completeness audit and signed release-ready candidate. Continue independent work while a specific capability is blocked. Substantive batches, focused tests, one active task branch/PR. No repeated approval for ordinary engineering choices.

## 11. Release and definition of done

Use canonical main and existing lightweight publisher only. Verify actual version, next unused number, assembly, signed EXE and byte-identical compatibility asset, public readback, manifest, mirror and rollback integrity. No production publication until explicitly authorized. Maintain canonical knowledge and actual PR/CI evidence. Deliver complete ledger, existing-feature audit, corrected defects, new capabilities, UI paths, tests, diagnostics, performance assessment, canonical docs, signed candidate and external limitations.

Final report must show original counts A6/B12/C5/D12/E3/F3/G5/H6/I9 = 61; additional L1 = 6; actual audited existing-feature and playbook counts; implemented, runtime-verified, CI-verified, partial, alternative and blocked per row. No disappeared ID or unsupported completion percentage. Preserve exact gaps and evidence.

**Execution directive:** HLMD is the primary ongoing technical reference. Systematically convert its findings into existing-feature corrections and new product capabilities, preserve all IDs and legitimate user goals, and do not leave research unused merely because it was organized as documentation or experimental code.
