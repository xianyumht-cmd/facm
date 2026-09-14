# Runtime Companion Akari screenshot reference

The user-provided Akari screenshots are the durable feature/interaction reference for the Runtime Companion expansion in draft PR #283. They are not a request to clone Akari branding, CSS, typography or implementation technology. When a later implementation decision is ambiguous, prefer the workflow demonstrated by those screenshots while preserving FACM ownership, safety and lightweight WinForms constraints.

## What the screenshots mean for FACM

The target is a compact League-side companion that exposes useful current-context decisions without opening the full FACM dashboard. The reference is functional and hierarchical:

1. **Current champion first.** Champion identity and current mode/position must be immediately visible.
2. **Build decisions are icon-first.** Runes, summoner spells, skills and item paths should be recognized visually before reading long text. Text remains available as evidence/tooltips/fallback.
3. **Current draft matters.** Ally/enemy picks that the client actually exposes should affect presentation. FACM may prioritize relevant matchup information from data it already owns, but must not infer hidden enemy identity or intent.
4. **Player context is compact.** The local player's useful recent/current-champion context belongs beside the build, but a transient 320x420 popup is not a replacement for FACM's full player-history views.
5. **Matchup context should become specific when evidence allows.** A revealed exact-position enemy can be promoted inside an already-fetched verified counter list. If position or source evidence is unavailable, FACM must fall back to the general source ordering rather than pretending to know the lane matchup.
6. **ARAM / Mayhem stays mode-specific.** Bench, build modules, ARAM balance and augment ranking should appear only when the active mode makes them relevant. Rarity filtering is local and should not refetch.
7. **Secondary detail is progressive.** The most useful recommendation is shown first; alternatives/details stay behind compact expansion, paging or tooltips.
8. **The window remains transient and non-disruptive.** No taskbar entry, no focus stealing, fixed compact top context, wheel-scroll details, pin/collapse/close controls.
9. **Automation follows the shared League lifecycle.** Akari-style automation settings may be adopted only when they can reuse FACM's existing Gameflow owner and narrow write transports. A UI option must not create a second Gameflow poller or an unrestricted LCU writer.

## Current implementation alignment

- 320 logical px width and 420 logical px maximum expanded height.
- Fixed header/champion context/Bench with hidden-native-scrollbar wheel body.
- Grouped rune schemes, icon-first spells/items, Q/W/E/R skill tokens and guarded Apply/import owners.
- Local current-champion recent-use statistics from the existing local-player history owner only.
- Ally/enemy draft projection from the already-read ChampSelect session.
- OP.GG counter icons from the already-fetched build payload.
- Revealed-enemy counter prioritization without extra requests or hidden-intent inference.
- Exact assigned-position counter prioritization when both local and enemy positions are explicitly exposed.
- Per-counter OP.GG sample/win evidence retained so the focused visible matchup can show source evidence rather than a fabricated score.
- Ordinary ARAM balance and Mayhem build/augment modules remain separated.
- Bench quick swap, one-click leave-Champion-Select owner, matchmaking delay/ReadyCheck automation and persistent window state remain owned by their existing FACM services.
- Akari-style **停止匹配策略** is now part of the existing automation owner: `永不`, `固定时间`, and `超过队列预估时间`. Fixed mode is bounded to 1-600 seconds; estimated mode uses the client's existing `/lol-matchmaking/v1/search` elapsed/estimate state. The controller is driven by the shared Gameflow state, cancels as soon as phase leaves `Matchmaking` (including `ReadyCheck`), and the transport permits only the existing search POST, the new narrowly fenced search DELETE, and ReadyCheck accept POST. A stop is considered successful only after search-state reconciliation confirms the queue ended.

## Remaining screenshot-driven follow-ups

These are the next useful parity directions, subject to the same no-duplicate-owner rule:

- enrich compact team rows with more already-exposed live context, especially summoner-spell presentation, without per-player history fan-out;
- make the focused matchup visually clearer when a revealed exact-position opponent intersects verified source data;
- reuse already-loaded catalog/icon metadata instead of issuing a second catalog request from the Form;
- audit the existing FACM presence/toolbox owners against the Akari screenshots before adding duplicate chat-status, signature, profile/background, rank-card or lobby utilities;
- continue improving compact player/team readability before adding any new external data source;
- keep deeper scouting, long-term player history and post-game analysis in their existing FACM-owned surfaces unless a later task explicitly changes that lifecycle boundary.

## Non-goals preserved

- no Akari brand clone;
- no WinUI/WPF/web migration;
- no second Gameflow/ChampSelect observer;
- no automatic match-history request for every teammate/opponent;
- no hidden enemy pick/identity inference;
- no production version bump, online manifest change, merge or release as part of incremental parity work.
