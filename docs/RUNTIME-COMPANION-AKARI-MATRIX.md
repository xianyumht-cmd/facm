# Runtime Companion Akari parity matrix

This matrix records the intended feature boundary for draft PR #283. Akari is used as an interaction-density and workflow reference; FACM keeps its own WinForms/.NET Framework 4.8 architecture, data owners and safety fences. The 2026-09-13 local-player context extension was explicitly authorized to continue development without inserting another incremental Tencent-client test gate; production merge/release boundaries are unchanged.

Durable round-by-round execution status is also tracked in `docs/RUNTIME-COMPANION-PROGRESS.md` so a later task can continue from repository state rather than chat history.

## Implemented in the transient Champion Select Runtime Companion / shared lightweight League workflow

| Capability | Status | FACM implementation boundary |
| --- | --- | --- |
| Narrow side companion | Implemented | 320 logical px width, 420 logical px max height; fixed header/context/Bench and wheel-scroll body; native light scrollbars hidden. |
| Champion context | Implemented | Champion portrait/name, mode/position/patch and source stats where verified data exists; raw internal `#championId` is not accepted as visible identity. |
| Local current-champion recent use | Implemented | Reuses the existing `LeaguePlayerDataService` owner for the local account only. A bounded recent sample projects current-champion games, wins/losses and average K/D/A into the scroll body. Unresolved participant rows are excluded from performance statistics, small samples remain explicit, and champion switches cancel/reject stale work. |
| Grouped rune recommendations | Implemented | OP.GG source schemes remain grouped; primary scheme uses up to six LCU perk icons when `iconPath` exists; Apply stays on `LeagueBuildApplyService`. |
| Summoner spell recommendations | Implemented | Icon-first LCU assets, source alternatives through progressive disclosure, Apply through the existing guarded owner. |
| Skill priority | Implemented | Compact source-derived Q/W/E/R tokens and source evidence; no fabricated ability metadata. |
| Starter items / boots / core build | Implemented | Icon-first LCU assets with readable text/tooltips; core import stays on `LeagueItemSetService`. |
| Counter matchups | Implemented, draft-aware | Existing OP.GG `counters` payload is projected as up to five champion icons. Revealed enemies may be prioritized; when both local/enemy assigned positions are explicitly exposed, the exact-position revealed counter wins priority. Per-counter source games/wins remain aligned and the focused counter may show its OP.GG sample evidence. No extra OP.GG/LCU request, hidden-intent inference or mutation of the Build Advisor owner's source snapshot is introduced. |
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
| Matchmaking stop strategy | Implemented | Shared Gameflow-driven owner exposes `永不 / 固定时间 / 超过队列预估时间`. Fixed stop is bounded to 1-600 s. Estimated mode reads only existing `/lol-matchmaking/v1/search` elapsed/estimate state. Pending stop work is cancelled when phase leaves `Matchmaking`, including `ReadyCheck`; the narrow transport allows DELETE only on the existing matchmaking-search route and post-write success requires search-state reconciliation. |
| Presence modes | Implemented | Existing narrow `PUT /lol-chat/v1/me` owner supports `在线 / 离开 / 勿扰 / 手机在线 / 隐身 / 显示为游戏中`; writes are read back and FACM does not run a background fight-loop against the client. |
| Chat signature | Implemented | Reuses the same fenced presence owner. Signature changes are read-modify-write so availability, gameStatus and unrelated fields survive; empty text clears it, first + settled readback detects client overwrite, and the 512-character bound is only a defensive FACM input guard. |
| Displayed rank metadata | Implemented | Reuses the same fenced presence owner; queue/tier/division values are allowlisted, apex tiers omit division, unrelated Presence fields survive, canonical queue tokens such as `RANKED_SOLO_5x5` are preserved, and first + settled readback detects client overwrite without a rewrite loop. |
| Profile background | Implemented | Dedicated profile writer is hard-fenced to `POST /lol-summoner/v1/current-summoner/summoner-profile`; local game-data supplies champion/skin candidates only on explicit UI demand, writes occur once, and bounded readback verifies `backgroundSkinId`. |
| Profile border / prestige crest | Implemented | Dedicated regalia writer is hard-fenced to `PUT /lol-regalia/v2/current-summoner/regalia`. FACM first reads current regalia, preserves `bannerType`, writes `preferredCrestType=prestige`, preserved `preferredBannerType` and `selectedPrestigeCrest=22`, then uses bounded first + settled readback. Missing evidence fails closed and client overwrite never starts a fight-loop. |
| Last-season banner preference | Implemented, preservation-safe | Reuses a dedicated challenge-preferences writer hard-fenced to `POST /lol-challenges/v1/update-player-preferences`. FACM reconstructs the current title, challenge-token IDs, crest border and prestige-crest level from the local challenge summary, preserves those fields (and `signedJWTPayload` when exposed), changes only `bannerAccent` to the audited value `2`, then performs bounded first + settled readback. Missing preservation evidence fails closed; success requires the banner **and** preserved fields to match. |
| Challenge-token cleanup | Implemented, preservation-safe | Reuses the same challenge-preferences owner. FACM requires current title/banner/crest/prestige/token evidence, preserves all unrelated preference fields, writes `challengeIds=[]` once, and reports success only after bounded readback proves the list is empty while title/banner/crest/prestige remain unchanged. Missing banner/preservation evidence fails closed and restored tokens do not trigger a rewrite loop. |
| Account emote cleanup | Implemented, ownership-safe | Reads `/lol-loadouts/v4/loadouts/scope/account` only on explicit demand, requires exactly one account loadout exposing the complete audited 13-slot emote contract, and PATCHes only `/lol-loadouts/v4/loadouts/{id}` through a dedicated fenced writer. All audited emote slots are set to `itemId=-1`; unrelated loadout slots are omitted. First + settled readback requires the same unique loadout and all audited slots cleared. Ambiguous/incomplete ownership fails closed and client restoration is reported without a rewrite loop. |
| Pin / collapse / drag persistence | Implemented | Shared `AppSettings` + existing LKG recovery, not a private companion settings file. |
| DPI / multi-monitor placement | Implemented | PerMonitorV2-aware placement and working-area clamping, including negative monitor coordinates. |

## Active screenshot-driven parity work

Challenge-token and emote cleanup are now implemented behind explicit user actions and bounded readback. The current batch audits **lobby/profile inspection utilities** against existing FACM surfaces before adding anything new.

The ownership rule is stricter than a feature checklist: if Akari displays information that FACM already owns in `LeagueDashboard`, `LeaguePlayer`, `LeagueLive`, Build Advisor or the existing profile window, FACM should reuse/project that owner rather than create another page-local request loop. Read-only inspection belongs outside the transient 320x420 Champion Select surface when it is not Champion Select context. A new writer is out of scope for this inspection batch unless a separately audited explicit mutation is justified.

After inspection utilities, the remaining screenshot-driven item is login-time signature/display-rank reapply, and only if the existing League connection/chat-ready lifecycle can own a bounded one-shot reapply without a second poller or background fight-loop. Exact status and boundaries live in `docs/RUNTIME-COMPANION-PROGRESS.md`.

## Deliberately not duplicated into this 320x420 transient surface

These are not accidental omissions. They either already belong to another FACM-owned view or would require a materially different runtime/data-ownership design.

| Capability | Decision | Reason |
| --- | --- | --- |
| Automatic match-history scan for every ally/enemy | Not in this task | The new recent-use module is **local player only**. Per-player fan-out during Champion Select would conflict with the lightweight/no-extra-scouting contract; draft context continues to use only the already-read session payload. |
| Deep player scouting / long-term trend pages | Keep in existing FACM player views | Existing player/history services already own deeper history; the companion only projects a bounded local current-champion sample rather than becoming another dashboard. |
| Full post-game analysis | Keep outside Runtime Companion | Post-game is a different lifecycle and should not extend a Champion Select-only popup owner. |
| Persistent in-game overlay / timers | Separate future task | Current popup closes when Champion Select ends. Adding an in-game overlay would require a separately justified lifecycle and presentation owner rather than quietly extending this PR. |
| Hidden enemy identity / intent prediction | Rejected | Tencent/LCU hidden information must fail closed; FACM must not infer or fabricate it. Draft-aware counter ordering only uses enemy champions already revealed by the client and only intersects them with the already-fetched OP.GG counter list. |
| Akari branding / exact visual clone | Rejected | FACM keeps `FacmDesignSystem` semantics, typography and native WinForms architecture. |

## Current acceptance rule

Development may continue on explicitly authorized lightweight follow-ups without stopping for an incremental real-machine test after every batch. Automated source/build gates should still remain green. Before merge/release, one consolidated Tencent-client pass should cover Ranked/Training Champion Select, ordinary ARAM where available, ARAM Mayhem, `退` preserving lobby, minimum-party/start-delay/ReadyCheck-delay automation, all three matchmaking-stop strategies, wheel scrolling, grouped rune/build alternatives, local recent-use context, draft-aware counter ordering, draft rows, Bench, explicit profile-background apply, explicit profile-border/prestige-crest action, explicit last-season-banner apply with title/challenge-token/crest/prestige preservation, explicit challenge-token cleanup with unrelated preference preservation, account emote cleanup with unique-owner/readback behavior, and at least the user's normal desktop DPI.

Production merge/version bump/update-manifest/release still require explicit closeout intent.
