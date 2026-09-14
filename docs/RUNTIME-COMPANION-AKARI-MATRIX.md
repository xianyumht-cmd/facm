# Runtime Companion Akari parity matrix

This matrix records the intended feature boundary for draft PR #283. Akari is used as an interaction-density and workflow reference; FACM keeps its own WinForms/.NET Framework 4.8 architecture, data owners and safety fences. Production merge/release boundaries are unchanged.

Durable round-by-round execution status is tracked in `docs/RUNTIME-COMPANION-PROGRESS.md`.

## Implemented in the transient Champion Select Runtime Companion / shared lightweight League workflow

| Capability | Status | FACM implementation boundary |
| --- | --- | --- |
| Narrow side companion | Implemented | 320 logical px width, 420 logical px max height; fixed header/context/Bench and wheel-scroll body; native light scrollbars hidden. |
| Champion context | Implemented | Champion portrait/name, mode/position/patch and source stats where verified data exists; raw internal `#championId` is not accepted as visible identity. |
| Local current-champion recent use | Implemented | Reuses `LeaguePlayerDataService` for the signed-in local account only. A bounded recent sample projects current-champion games, wins/losses and average K/D/A; no ally/enemy history fan-out. |
| Grouped rune recommendations | Implemented | OP.GG source schemes remain grouped; primary scheme uses cached LCU perk icon metadata when available; Apply stays on `LeagueBuildApplyService`. |
| Summoner spell recommendations | Implemented | Icon-first LCU assets, source alternatives through progressive disclosure, Apply through the existing guarded owner. |
| Skill priority | Implemented | Compact source-derived Q/W/E/R tokens and source evidence; no fabricated ability metadata. |
| Starter items / boots / core build | Implemented | Icon-first LCU assets with readable text/tooltips; core import stays on `LeagueItemSetService`. |
| Counter matchups | Implemented, draft-aware | Existing OP.GG `counters` payload is projected as up to five source counters. Revealed enemies may be prioritized; when local/enemy assigned positions are explicit and equal, that exact-position revealed source counter wins priority. Label/icon/stat alignment is preserved, and focused evidence now prefixes the verified normalized position token before the existing source win/sample evidence. No matchup score is invented and no extra OP.GG/LCU request is added. |
| Champion Select countdown | Implemented | Reuses timer data from the same lightweight ChampSelect session read; no second observer/request owner. |
| Ally draft context | Implemented | Up to five local-team rows; locked champion or local-team pick intent may be shown, with position/player tooltip and ban count. |
| Enemy draft context | Implemented fail-closed | Only champion/account information actually exposed by the client is rendered; hidden enemy intent/identity is never inferred. |
| ARAM Bench quick swap | Implemented | Existing `LeagueBenchQuickPickService`, compact paging and fully visible zoomed portraits. |
| Ordinary ARAM balance | Implemented | Version-bound base-balance supplement; no Mayhem-only augment leakage. |
| ARAM Mayhem guide | Implemented | Summoner spells, skill priority, starter, boots, core build, balance and augment ranking when verified source data exists. |
| Mayhem rarity filtering | Implemented | Local `全部 / 棱彩 / 黄金 / 白银` filter from Riot game-data rarity; filtering causes no external refetch. |
| Alternative recommendations | Implemented | Up to three source-order rows from the same already-fetched OP.GG payload; `更多` performs no network request. |
| Leave current Champion Select but keep lobby | Implemented | Dedicated fenced POST to `/lol-lobby-team-builder/champ-select/v1/session/quit`, followed by phase + surviving-lobby verification. |
| Auto matchmaking minimum party | Implemented | Existing matchmaking controller; 1-5 members, no second Gameflow observer. |
| Matchmaking start delay | Implemented | 0-60 s, phase/settings bounded and cancellable. |
| ReadyCheck accept delay | Implemented | 0-15 s, phase/settings bounded and cancellable. |
| Matchmaking stop strategy | Implemented | Shared Gameflow owner exposes `永不 / 固定时间 / 超过队列预估时间`; DELETE is narrowly fenced and success requires post-write search-state reconciliation. |
| Presence modes | Implemented | Existing narrow `PUT /lol-chat/v1/me` owner supports online/away/dnd/mobile/offline-ish/show-in-game; writes are read back with no background fight-loop. |
| Chat signature | Implemented | Reuses the same fenced Presence owner. Read-modify-write preserves unrelated fields; empty text clears; first + settled readback detects client overwrite. |
| Displayed rank metadata | Implemented | Reuses the same Presence owner; queue/tier/division are allowlisted, apex tiers omit division, unrelated state survives and mixed-case canonical queue tokens remain intact. |
| Profile background | Implemented | Dedicated writer hard-fenced to `POST /lol-summoner/v1/current-summoner/summoner-profile`; explicit game-data selection and bounded `backgroundSkinId` readback. |
| Profile border / prestige crest | Implemented | Dedicated writer hard-fenced to `PUT /lol-regalia/v2/current-summoner/regalia`; current banner type is preserved and requested crest state is read back. |
| Last-season banner preference | Implemented, preservation-safe | Reuses challenge-preferences writer hard-fenced to `POST /lol-challenges/v1/update-player-preferences`; title/tokens/crest/prestige and exposed JWT state are preserved while only the audited banner accent changes. |
| Challenge-token cleanup | Implemented, preservation-safe | Reuses the same challenge-preferences owner, preserves unrelated preference state and reports success only after bounded readback proves the token list is empty. |
| Account emote cleanup | Implemented, ownership-safe | Reads account-scope loadouts only on explicit demand, requires exactly one complete audited 13-slot emote owner, PATCHes only the validated loadout ID and verifies all audited slots cleared without touching unrelated slots. |
| Pin / collapse / drag persistence | Implemented | Shared `AppSettings` + existing LKG recovery, not a private companion settings file. |
| DPI / multi-monitor placement | Implemented | PerMonitorV2-aware placement and working-area clamping, including negative monitor coordinates. |

## Audited but deliberately deferred

| Capability | Decision | Reason |
| --- | --- | --- |
| Login-time signature/display-rank reapply | Deferred pending shared chat-ready lifecycle | Akari owns this from existing `chat.me`/connection readiness and runs once after a 2-second settle. FACM currently has on-demand LCU session discovery and Gameflow, but no equivalent shared chat-ready event. Adding a new poller or treating recurring Gameflow as chat readiness would violate the lightweight/single-owner boundary. Manual signature/rank controls remain implemented. |
| Queue-ID lobby creation from Akari LobbyTool | Separate explicit mutation task | Akari performs eligibility POSTs then `POST /lol-lobby/v2/lobby`. This requires its own narrow write fence and reconciliation instead of being mislabeled as inspection parity. |
| Arbitrary cross-source game-ID preview | Separate future task | Akari's `ConnectedMatchPreviewer` can prefer SGP and fall back to LCU summary/timeline data. FACM's current local-player history owner is intentionally narrower; an LCU-only clone would regress source behavior and duplicate ownership. |

## Active lightweight presentation work

The next presentation target is compact ally/enemy summoner-spell context. `LeagueLivePlayerRow` already carries `Spell1Id` and `Spell2Id`, so no per-player lookup is necessary for IDs. The existing Build Advisor also owns a cached Riot game-data catalog with spell names/icons, but that cache is currently private to `LeagueBuildAdvisorDataService`. FACM must not add a Form-owned summoner-spell catalog request merely for decoration. The next implementation should expose only already-owned/cached presentation metadata or fail closed to a text fallback; it must not force a catalog fetch or add network fan-out.

## Deliberately not duplicated into this 320x420 transient surface

| Capability | Decision | Reason |
| --- | --- | --- |
| Automatic match-history scan for every ally/enemy | Not in this task | Recent-use is local player only. Per-player fan-out during Champion Select conflicts with the lightweight contract. |
| Deep player scouting / long-term trend pages | Keep in existing FACM player views | Existing player/history services already own deeper history. |
| Full post-game analysis | Keep outside Runtime Companion | Post-game is a different lifecycle. |
| Persistent in-game overlay / timers | Separate future task | Current popup closes when Champion Select ends; an in-game overlay needs separately justified ownership. |
| Hidden enemy identity / intent prediction | Rejected | Tencent/LCU hidden information must fail closed. |
| Akari branding / exact visual clone | Rejected | FACM keeps `FacmDesignSystem`, native WinForms architecture and its own visual language. |

## Current acceptance rule

Development may continue on lightweight follow-ups without stopping for an incremental real-machine test after every batch. Automated gates remain authoritative for code regressions during development. Before merge/release, one consolidated Tencent-client pass should cover Ranked/Training Champion Select, ordinary ARAM where available, ARAM Mayhem, `退` preserving lobby, matchmaking automation strategies, wheel scrolling, grouped rune/build alternatives, local recent-use context, draft-aware counter ordering/evidence, draft rows, Bench, explicit profile-background apply, profile-border/prestige-crest action, last-season-banner preservation, challenge-token cleanup preservation, account-emote cleanup ownership/readback behavior, and normal desktop DPI.

Production merge/version bump/update-manifest/release still require explicit closeout intent.
