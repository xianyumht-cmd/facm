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
- manual **展示段位** controls reuse the same fenced Presence owner and only change `lol.rankedLeagueQueue`, `lol.rankedLeagueTier` and, for non-apex tiers, `lol.rankedLeagueDivision`.
- displayed-rank queue/tier/division inputs are allowlisted. Unknown values fail closed without a write.
- `MASTER / GRANDMASTER / CHALLENGER` omit the division field rather than preserving a stale division.
- displayed-rank writes preserve chat signature, availability, gameStatus and unrelated Presence metadata, then use first + settled readback verification. Client overwrite is reported without a rewrite loop.
- the UI states explicitly that this changes only chat-card display metadata and does **not** change server rank, LP or match history.
- canonical mixed-case queue tokens such as `RANKED_SOLO_5x5` are preserved through normalization; the host smoke no longer fails by uppercasing `x` into a non-allowlisted token.
- presence/signature/displayed-rank smoke coverage is wired into the host smoke suite.

## DONE — summon/profile background parity

- Akari reference semantics were verified before implementation instead of guessed.
- FACM now has a dedicated `ILeagueProfileWriteApi` / `LeagueProfileWriteApiClient` hard-fenced to `POST /lol-summoner/v1/current-summoner/summoner-profile`; it cannot mutate chat, inventory, ChampSelect or arbitrary LCU routes.
- `LeagueProfileCustomizationService` reads the local champion summary only when the explicit profile tool opens, then reads one champion-detail document only after the user chooses that champion. There is no background catalog prefetch or second poller.
- background selection uses the verified `{ "key": "backgroundSkinId", "value": <skinId> }` payload.
- the write is user-directed, occurs once, and is followed by bounded first + settled reads of `GET /lol-summoner/v1/current-summoner/summoner-profile`.
- client overwrite is reported without a rewrite loop; missing verification is reported as unverified rather than falsely claimed successful.
- invalid/non-positive skin IDs fail closed without a write.
- base skins and unique quest-skin tiers are projected from local `/lol-game-data/assets/v1/champions/{id}.json` data; FACM does not claim to grant skin ownership.
- the existing online/presence tool surface now links to a lightweight **召唤师外观 → 生涯背景** picker rather than creating a duplicate League session owner.
- parser/payload/readback/fence smoke coverage is wired into the host smoke suite.

## DONE — profile border / prestige-crest regalia parity

- upstream Akari behavior was audited before implementation: it reads `GET /lol-regalia/v2/current-summoner/regalia`, preserves the returned `bannerType`, then writes `preferredCrestType=prestige`, the preserved banner type and `selectedPrestigeCrest=22` through the regalia owner.
- FACM now has a dedicated `ILeagueRegaliaWriteApi` / `LeagueRegaliaWriteApiClient` hard-fenced to `PUT /lol-regalia/v2/current-summoner/regalia`; it shares the existing `LeagueClientSessionProvider` and cannot reach chat, matchmaking, ChampSelect, inventory or arbitrary routes.
- `LeagueRegaliaCustomizationService` performs one authoritative pre-read, one user-directed PUT, then bounded first + settled readback verification.
- missing current regalia/banner evidence fails closed without a write; rejected writes are reported as failed; unavailable readback is reported as unverified; client overwrite is reported as overridden without a rewrite loop.
- the existing **召唤师外观** window now exposes **资料边框 → 隐藏等级边框** alongside the existing background picker rather than creating a separate session or polling owner.
- parser/payload/readback/override/fence smoke coverage is wired into the main host smoke suite.
- Windows Build #1882 and UI Text Contract #990 both passed at head `5e5e1b3a797a1146ac7a0c2f062329e9924c01a4` after the UI integration.

## ACTIVE — last-season banner preference audit

Verified upstream reference route:

- `POST /lol-challenges/v1/update-player-preferences/` with `bannerAccent` in the challenge preferences document.
- Akari currently uses a fixed `bannerAccent` value for its screenshot-driven banner action.

The audited Akari helper exposes the write path but not a matching authoritative preference read endpoint. FACM must therefore either find an independent authoritative client document that proves the resulting banner state or present the operation honestly as accepted/unverified. It must not call a 2xx write “verified applied” without evidence.

## NEXT — remaining Akari toolbox/profile audit

1. last-season banner preference only with honest accepted/unverified semantics if no authoritative readback is available;
2. challenge-token cleanup after its exact challenge-preference ownership and preservation semantics are verified;
3. emote cleanup only after account-scope loadout selection and exact mutation/readback semantics are verified;
4. lobby/profile inspection utilities;
5. Akari-style `登录时重设签名` / displayed-rank reapply only if FACM can reuse the shared League connection/chat-ready lifecycle without adding a second poller or a background fight-loop.

Verified upstream reference routes so far:

- profile background: `GET/POST /lol-summoner/v1/current-summoner/summoner-profile`;
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
