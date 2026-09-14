# Runtime Companion execution progress

This file is the durable implementation checkpoint for draft PR #283 (`feat/runtime-companion-20260910`). It exists so the next execution round can continue from verified repository state instead of reconstructing status from chat history.

Updated: 2026-09-15

## Status legend

- `DONE` — implemented on the PR branch and covered by source/smoke/build contracts where applicable.
- `ACTIVE` — current engineering batch; continue without inserting an incremental manual Tencent-client gate.
- `NEXT` — queued after the active batch.
- `DEFERRED` — deliberately outside the current lightweight implementation boundary.
- `CLOSEOUT` — final consolidated acceptance/merge/release work; requires explicit user authorization where stated.

## DONE — Runtime Companion core

- 320 logical px x 420 logical px maximum transient companion, fixed top context and wheel-scroll details.
- one existing Gameflow/ChampSelect ownership chain; no second observer/session owner.
- champion identity, mode/position/patch/source evidence where verified.
- local-account-only recent current-champion sample through `LeaguePlayerDataService`; no teammate/opponent history fan-out.
- grouped runes, spell/item icons, Q/W/E/R skills, starter/boots/core paths and source alternatives.
- OP.GG counter row from the already-fetched payload only, including revealed-enemy and exact-position prioritization when evidence is explicit.
- ChampSelect countdown and ally/enemy draft context from the already-read session; enemy information remains fail-closed.
- ARAM Bench quick swap, ordinary ARAM balance, Mayhem guide and local rarity filtering.
- guarded rune/spell apply, item-set import and leave-Champion-Select-with-lobby-preserved action.
- persisted drag/pin/collapse state and PerMonitorV2/negative-coordinate working-area clamping.

## DONE — matchmaking automation parity

- minimum party size 1-5; matchmaking start delay 0-60 seconds; ReadyCheck accept delay 0-15 seconds.
- stop strategy: `永不 / 固定时间 / 超过队列预估时间`; fixed stop bounded to 1-600 seconds.
- estimated stop reuses `/lol-matchmaking/v1/search` elapsed/estimate state.
- pending stop cancels when phase leaves `Matchmaking`, including `ReadyCheck`.
- narrow write transport permits matchmaking search POST/DELETE and ReadyCheck accept only; queue stop is successful only after search-state reconciliation.

## DONE — presence / chat-signature / displayed-rank parity

- presence modes remain owned by `LeaguePresenceService`: `在线 / 离开 / 勿扰 / 手机在线 / 隐身 / 显示为游戏中`.
- chat signature and displayed-rank metadata reuse the fenced `PUT /lol-chat/v1/me` owner.
- both paths are read-modify-write, preserve unrelated Presence state, and use bounded first + settled readback without a rewrite loop.
- empty signature clears it; the 512-character guard is FACM-side defensive validation rather than a claimed Riot limit.
- displayed-rank queue/tier/division inputs are allowlisted; canonical mixed-case queue tokens such as `RANKED_SOLO_5x5` are preserved; apex tiers omit division.
- the UI states that displayed-rank metadata does not change real server rank, LP or match history.
- presence/signature/displayed-rank smoke coverage is wired into the host smoke suite.

## DONE — profile background / regalia / challenge preferences

- profile background uses a dedicated writer hard-fenced to `POST /lol-summoner/v1/current-summoner/summoner-profile`, explicit local game-data selection and bounded `backgroundSkinId` readback.
- profile border/prestige crest uses a dedicated writer hard-fenced to `PUT /lol-regalia/v2/current-summoner/regalia`, preserves current banner type, writes the audited prestige-crest state once and verifies bounded readback.
- last-season banner uses the challenge-preferences owner hard-fenced to `POST /lol-challenges/v1/update-player-preferences/`.
- because the challenge route can behave as a replacement write, FACM reconstructs and preserves current title, challenge-token IDs, crest border, prestige-crest level and exposed `signedJWTPayload`, changing only `bannerAccent` to the audited value `2`.
- missing preservation evidence fails closed; missing readback is unverified; preservation drift/client overwrite is overridden; no background fight-loop is introduced.

## DONE — challenge-token cleanup parity

- Akari's remove-token action was audited and FACM reuses the preservation-safe challenge-preferences owner rather than copying a narrow destructive payload.
- current title/banner/crest/prestige/token evidence is required before the write; unrelated preference fields and exposed `signedJWTPayload` are preserved.
- one user-directed POST changes only the challenge-token list to `[]`; bounded readback must prove the list is empty while preserved fields remain unchanged.
- missing evidence fails closed; restored tokens or preservation drift are overridden; there is no rewrite loop.
- **召唤师外观 → 挑战徽章 → 清除徽章** exposes the action.
- validated functional head `b50549a2df4f84a3a8c9819177e25b8c06a5ef8c`: UI Text Contract #1011 PASS, Mayhem Source Probe #685 PASS, Windows Build #1903 PASS.

## DONE — account emote cleanup parity

- Akari's account-loadout route and public/generated LCU models were audited before implementation.
- FACM reads `GET /lol-loadouts/v4/loadouts/scope/account` only on explicit demand and requires exactly one candidate exposing the complete audited 13-slot emote contract; zero/incomplete/multiple candidates fail closed.
- `ILeagueEmoteLoadoutWriteApi` / `LeagueEmoteLoadoutWriteApiClient` is hard-fenced to `PATCH /lol-loadouts/v4/loadouts/{id}` with a validated single ID segment.
- the outgoing `{ "loadout": ... }` document touches only the 13 audited emote slots and sets each to `inventoryType=EMOTE`, `itemId=-1`; unrelated loadout slots are omitted.
- one PATCH is followed by bounded first + settled reads; success requires the same unique loadout and all audited slots cleared. Client restoration is reported without another write.
- **召唤师外观 → 表情轮盘 → 清空表情** exposes the action.
- validated functional head `befde045d3f4d1fdefb74024dc2031f33cdf7f84`: UI Text Contract #1020 PASS, Mayhem Source Probe #694 PASS, Windows Build #1912 PASS.

## DONE — lobby / arbitrary-game inspection ownership audit

- Akari `LobbyTool` is not read-only: it POSTs party/self eligibility and then explicitly creates a queue lobby with `POST /lol-lobby/v2/lobby { queueId }`. FACM did not smuggle that new mutation into a read-only inspection batch.
- Akari `GameView` is cross-source: `ConnectedMatchPreviewer` prefers SGP and falls back to LCU summary `/lol-match-history/v1/games/{gameId}` plus timeline `/lol-match-history/v1/game-timelines/{gameId}`.
- FACM already owns signed-in-local-player history in `LeaguePlayerDataService` but has no equivalent arbitrary-game cross-source preview owner. A reduced LCU-only clone was deliberately not added.

## DONE — login-time signature / displayed-rank lifecycle audit; implementation deferred

- Akari's current behavior is verified: existing connected/chat-me state drives a one-shot automation, missing `chat.me` or disconnect resets the episode, chat state settles for 2 seconds, and enabled signature/rank operations run once. A manual operation interrupts the pending automation.
- FACM's current `LeagueClientSessionProvider` provides on-demand LCU discovery/invalidation but no shared `chat.me` readiness event. The Dashboard's existing Gameflow stream is game lifecycle state, not chat readiness.
- `LeaguePresenceService` is already the correct fenced write owner, but wiring login-time behavior today would require either a new chat poller or an approximation based on unrelated recurring Gameflow state.
- both options violate the current single-owner/lightweight boundary. Therefore no automatic login-time reapply was added in this PR. Manual signature/rank functionality remains unchanged and safe.
- this item may be revisited only when FACM has an explicit shared client/chat-ready lifecycle signal that can own a bounded once-per-session settle task without a second poller or rewrite loop.

## DONE — exact-position counter evidence readability

- the draft-aware counter projection still uses only the already-fetched OP.GG counter list and enemy champions already revealed by the same ChampSelect snapshot.
- when both local and revealed-enemy positions are explicit and equal, that source counter remains first and its existing win/sample statistics stay aligned with its label/icon.
- the focused evidence now prefixes the verified normalized position token (`TOP / JUNGLE / MID / ADC / SUPPORT`) before the existing OP.GG win/sample evidence. This makes the exact-position reason visible without fabricating a matchup score.
- revealed counters with incomplete position evidence still reorder only by revealed-source membership and do not receive an invented position marker.
- dedicated projection smoke verifies ordering, icon/stat alignment, exact-position evidence and the no-position fail-closed case. It is wired into the main Host smoke suite.

## ACTIVE — Runtime Companion compact team spell presentation audit

- ChampSelect player rows already carry `Spell1Id` / `Spell2Id` through `LeagueLivePlayerRow`; no new per-player lookup is required to know the IDs.
- the existing Build Advisor owns a cached Riot game-data catalog containing spell names/icons, but that catalog is currently private to `LeagueBuildAdvisorDataService` and is not exposed through the Runtime Companion snapshot/controller.
- do **not** add a Form-owned `/lol-game-data/assets/v1/summoner-spells.json` request merely to decorate team rows.
- next engineering step: determine the smallest safe read-only projection of already-loaded spell metadata from the existing Build Advisor owner. If that cannot be exposed without widening ownership or forcing a catalog fetch, keep the team-row enhancement text/fail-closed rather than adding network fan-out.

## NEXT — remaining lightweight follow-ups

1. compact ally/enemy spell presentation only through already-owned/cached metadata, with no per-player history or catalog fan-out;
2. continue compact player/team readability improvements that use the existing ChampSelect snapshot;
3. optional arbitrary cross-source game-ID preview only as a separately scoped future feature;
4. queue-ID lobby creation only as a separately authorized mutation task with narrow fencing and reconciliation.

Verified upstream/reference routes so far:

- profile background: `GET/POST /lol-summoner/v1/current-summoner/summoner-profile`;
- banner preference / challenge tokens: `POST /lol-challenges/v1/update-player-preferences/` with preservation-safe preference semantics required by FACM;
- regalia read/write: `GET/PUT /lol-regalia/v2/current-summoner/regalia`;
- chat-card displayed rank: `PUT /lol-chat/v1/me` with `lol.rankedLeagueQueue`, `lol.rankedLeagueTier`, optional `lol.rankedLeagueDivision`;
- account emote loadout: `GET /lol-loadouts/v4/loadouts/scope/account`, then `PATCH /lol-loadouts/v4/loadouts/{id}` for the uniquely identified account emote loadout;
- Akari lobby eligibility / creation: `POST /lol-lobby/v2/eligibility/party`, `POST /lol-lobby/v2/eligibility/self`, then explicit `POST /lol-lobby/v2/lobby { queueId }`;
- Akari LCU game preview: `GET /lol-match-history/v1/games/{gameId}` plus `GET /lol-match-history/v1/game-timelines/{gameId}`, with SGP preferred when available.

## DEFERRED boundaries

- automatic match-history scan for every ally/enemy.
- hidden enemy identity or intent inference.
- deep scouting/long-term trends/full post-game analysis inside the transient companion.
- persistent in-game overlay/timer runtime.
- Akari branding/exact visual clone.
- automatic login-time signature/display-rank reapply until an explicit shared chat-ready lifecycle exists.
- arbitrary cross-source game preview and queue-ID lobby creation until separately scoped/authorized.

## CLOSEOUT

Do not merge PR #283, bump the production version, modify `online/version.json`, publish a production release, or delete the legacy rollback assistant from an incremental round.

When feature work is complete, run one consolidated real Tencent-client acceptance pass using `docs/RUNTIME-COMPANION-LIVE-ACCEPTANCE.md`. Merge/release remains an explicit user closeout decision.
