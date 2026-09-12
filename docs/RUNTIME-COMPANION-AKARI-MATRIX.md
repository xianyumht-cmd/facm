# Runtime Companion Akari parity matrix

This matrix freezes the intended feature boundary for draft PR #283 before the consolidated Tencent-client acceptance pass. Akari is used as an interaction-density and workflow reference; FACM keeps its own WinForms/.NET Framework 4.8 architecture, data owners and safety fences.

## Implemented in the transient Champion Select Runtime Companion

| Capability | Status | FACM implementation boundary |
| --- | --- | --- |
| Narrow side companion | Implemented | 320 logical px width, 420 logical px max height; fixed header/context/Bench and wheel-scroll body; native light scrollbars hidden. |
| Champion context | Implemented | Champion portrait/name, mode/position/patch and source stats where verified data exists; raw internal `#championId` is not accepted as visible identity. |
| Grouped rune recommendations | Implemented | OP.GG source schemes remain grouped; primary scheme uses up to six LCU perk icons when `iconPath` exists; Apply stays on `LeagueBuildApplyService`. |
| Summoner spell recommendations | Implemented | Icon-first LCU assets, source alternatives through progressive disclosure, Apply through the existing guarded owner. |
| Skill priority | Implemented | Compact source-derived Q/W/E/R tokens and source evidence; no fabricated ability metadata. |
| Starter items / boots / core build | Implemented | Icon-first LCU assets with readable text/tooltips; core import stays on `LeagueItemSetService`. |
| Counter matchups | Implemented | Existing OP.GG `counters` payload projected as up to five champion icons; display-only and no extra request. |
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
| Pin / collapse / drag persistence | Implemented | Shared `AppSettings` + existing LKG recovery, not a private companion settings file. |
| DPI / multi-monitor placement | Implemented | PerMonitorV2-aware placement and working-area clamping, including negative monitor coordinates. |

## Deliberately not duplicated into this 320x420 transient surface

These are not accidental omissions. They either already belong to another FACM-owned view or would require a materially different runtime/data-ownership design.

| Capability | Decision | Reason |
| --- | --- | --- |
| Automatic match-history scan for every ally/enemy | Not in this task | Would fan out per-player requests during Champion Select and conflict with the lightweight/no-extra-scouting contract. Current draft context uses only the already-read session payload. |
| Deep player scouting / long-term trend pages | Keep in existing FACM player views | Existing player/history services already own deeper history; duplicating them into the transient companion would turn it into a dashboard. |
| Full post-game analysis | Keep outside Runtime Companion | Post-game is a different lifecycle and should not extend a Champion Select-only popup owner. |
| Persistent in-game overlay / timers | Separate future task | Current popup closes when Champion Select ends. Adding an in-game overlay would require a separately justified lifecycle and presentation owner rather than quietly extending this PR. |
| Hidden enemy identity / intent prediction | Rejected | Tencent/LCU hidden information must fail closed; FACM must not infer or fabricate it. |
| Akari branding / exact visual clone | Rejected | FACM keeps `FacmDesignSystem` semantics, typography and native WinForms architecture. |

## Final acceptance rule

PR #283 is feature-frozen at this boundary. Do not add another Akari-like capability to this PR unless a consolidated live test reveals a correctness/usability defect in an implemented feature.

Before merge/release, one consolidated Tencent-client pass should cover Ranked/Training Champion Select, ordinary ARAM where available, ARAM Mayhem, `退` preserving lobby, the lightweight matchmaking-delay settings, wheel scrolling, grouped rune/build alternatives, draft rows, Bench, and at least the user's normal desktop DPI. CI must remain green. Production merge/version bump/update-manifest/release still require explicit closeout intent.
