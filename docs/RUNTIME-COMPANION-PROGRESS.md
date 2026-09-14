# Runtime Companion execution progress

This file is the durable implementation checkpoint for draft PR #283 (`feat/runtime-companion-20260910`). It exists so the next execution round can continue from verified repository state instead of reconstructing status from chat history.

Updated: 2026-09-14

## Status legend

- `DONE` — implemented on the PR branch and covered by source/smoke/build contracts where applicable.
- `ACTIVE` — current engineering batch; continue without inserting an incremental manual Tencent-client gate.
- `NEXT` — queued after the active batch.
- `DEFERRED` — deliberately not part of the current lightweight implementation boundary.
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

- minimum party size 1-5.
- matchmaking start delay 0-60 seconds.
- ReadyCheck accept delay 0-15 seconds.
- stop strategy: `永不 / 固定时间 / 超过队列预估时间`.
- fixed stop bounded to 1-600 seconds.
- estimated stop reuses `/lol-matchmaking/v1/search` elapsed/estimate state.
- pending stop cancels when phase leaves `Matchmaking`, including `ReadyCheck`.
- narrow write transport permits matchmaking search POST/DELETE and ReadyCheck accept only; queue stop is considered successful only after search-state reconciliation.

## DONE — presence / chat-signature / displayed-rank parity

- existing presence modes remain owned by `LeaguePresenceService`: `在线 / 离开 / 勿扰 / 手机在线 / 隐身 / 显示为游戏中`.
- explicit chat-signature editor reuses the same fenced `PUT /lol-chat/v1/me` owner.
- signature update is read-modify-write, preserving availability, gameStatus and unrelated presence fields.
- empty text clears the signature.
- first + settled readback verification detects League-client overwrite without entering a rewrite loop.
- 512-character input/service guard is defensive FACM input validation, not a claimed Riot account limit.
- manual **展示段位** controls now reuse the same fenced Presence owner and only change `lol.rankedLeagueQueue`, `lol.rankedLeagueTier` and, for non-apex tiers, `lol.rankedLeagueDivision`.
- displayed-rank queue/tier/division inputs are allowlisted. Unknown values fail closed without a write.
- `MASTER / GRANDMASTER / CHALLENGER` omit the division field rather than preserving a stale division.
- displayed-rank writes preserve chat signature, availability, gameStatus and unrelated Presence metadata, then use first + settled readback verification. Client overwrite is reported without a rewrite loop.
- the UI states explicitly that this changes only chat-card display metadata and does **not** change server rank, LP or match history.
- presence/signature/displayed-rank smoke coverage is wired into the host smoke suite.

## ACTIVE — summon/profile background owner

Akari source audit verified the reference implementation uses the current-summoner profile endpoint rather than the chat Presence endpoint:

- write endpoint: `POST /lol-summoner/v1/current-summoner/summoner-profile`;
- skin payload: `{ "key": "backgroundSkinId", "value": <skinId> }`;
- augment payload: `{ "key": "backgroundSkinAugments", "value": <augmentId> }`;
- read endpoint available for verification: `GET /lol-summoner/v1/current-summoner/summoner-profile`.

FACM must implement this through a new **dedicated narrow profile writer**, not by expanding the generic client writer or the `/lol-chat/v1/me` Presence writer. Reuse existing game-data/champion catalog ownership where practical; the profile form must not create a new background polling owner.

## NEXT — remaining Akari toolbox/profile audit

1. season/ranked banner via verified challenge preferences owner;
2. profile border/prestige crest via a dedicated regalia owner;
3. challenge-token cleanup and emote cleanup only after their exact ownership/readback semantics are verified;
4. lobby/profile inspection utilities;
5. Akari-style `登录时重设签名` / displayed-rank reapply only if FACM can reuse the shared League connection/chat-ready lifecycle without adding a second poller or a background fight-loop.

Verified upstream reference routes so far:

- banner preference: `POST /lol-challenges/v1/update-player-preferences/`;
- regalia read/write: `GET/PUT /lol-regalia/v2/current-summoner/regalia`;
- chat-card displayed rank: `PUT /lol-chat/v1/me` with `lol.rankedLeagueQueue`, `lol.rankedLeagueTier`, optional `lol.rankedLeagueDivision`.

## NEXT — Runtime Companion presentation follow-ups

- enrich compact team rows with already-exposed summoner-spell presentation without per-player history/network fan-out.
- make focused exact-position matchup evidence visually clearer without inventing a matchup score.
- reuse already-loaded catalog/icon metadata rather than introducing a Form-owned catalog request.
- continue compact player/team readability improvements before adding any external data source.

## DEFERRED boundaries

- automatic match-history scan for every ally/enemy.
- hidden enemy identity or intent inference.
- deep scouting/long-term trends/full post-game analysis inside the transient companion.
- persistent in-game overlay/timer runtime.
- Akari branding/exact visual clone.
- automatic signature or displayed-rank rewrite loop; login-time reapply stays deferred until lifecycle ownership is explicit and fail-safe.

## CLOSEOUT

Do not merge PR #283, bump the production version, modify `online/version.json`, publish a production release, or delete the legacy rollback assistant from an incremental round.

When feature work is complete, run one consolidated real Tencent-client acceptance pass using `docs/RUNTIME-COMPANION-LIVE-ACCEPTANCE.md`. Merge/release remains an explicit user closeout decision.
