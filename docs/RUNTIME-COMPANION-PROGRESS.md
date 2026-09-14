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
- canonical mixed-case queue tokens such as `RANKED_SOLO_5x5` are preserved through normalization.
- presence/signature/displayed-rank smoke coverage is wired into the host smoke suite.

## DONE — summon/profile background parity

- Akari reference semantics were verified before implementation instead of guessed.
- FACM has a dedicated `ILeagueProfileWriteApi` / `LeagueProfileWriteApiClient` hard-fenced to `POST /lol-summoner/v1/current-summoner/summoner-profile`; it cannot mutate chat, inventory, ChampSelect or arbitrary LCU routes.
- `LeagueProfileCustomizationService` reads the local champion summary only when the explicit profile tool opens, then reads one champion-detail document only after the user chooses that champion. There is no background catalog prefetch or second poller.
- background selection uses the verified `{ "key": "backgroundSkinId", "value": <skinId> }` payload.
- the write is user-directed, occurs once, and is followed by bounded first + settled reads of `GET /lol-summoner/v1/current-summoner/summoner-profile`.
- client overwrite is reported without a rewrite loop; missing verification is reported as unverified rather than falsely claimed successful.
- invalid/non-positive skin IDs fail closed without a write.
- base skins and unique quest-skin tiers are projected from local `/lol-game-data/assets/v1/champions/{id}.json` data; FACM does not claim to grant skin ownership.
- the existing online/presence tool surface links to a lightweight **召唤师外观 → 生涯背景** picker rather than creating a duplicate League session owner.
- parser/payload/readback/fence smoke coverage is wired into the host smoke suite.

## DONE — profile border / prestige-crest regalia parity

- upstream Akari behavior was audited before implementation: it reads `GET /lol-regalia/v2/current-summoner/regalia`, preserves the returned `bannerType`, then writes `preferredCrestType=prestige`, the preserved banner type and `selectedPrestigeCrest=22` through the regalia owner.
- FACM has a dedicated `ILeagueRegaliaWriteApi` / `LeagueRegaliaWriteApiClient` hard-fenced to `PUT /lol-regalia/v2/current-summoner/regalia`; it shares the existing `LeagueClientSessionProvider` and cannot reach chat, matchmaking, ChampSelect, inventory or arbitrary routes.
- `LeagueRegaliaCustomizationService` performs one authoritative pre-read, one user-directed PUT, then bounded first + settled readback verification.
- missing current regalia/banner evidence fails closed without a write; rejected writes are reported as failed; unavailable readback is reported as unverified; client overwrite is reported as overridden without a rewrite loop.
- the existing **召唤师外观** window exposes **资料边框 → 隐藏等级边框** alongside the background picker rather than creating a separate session or polling owner.
- parser/payload/readback/override/fence smoke coverage is wired into the main host smoke suite.

## DONE — last-season banner preference parity

- the upstream Akari action was verified: it calls `POST /lol-challenges/v1/update-player-preferences/` with fixed `bannerAccent=2` for the screenshot-driven “switch to last season banner” action.
- the Akari challenge preference type defines the replacement document as `bannerAccent`, `title`, `challengeIds`, `crestBorder`, and `prestigeCrestBorderLevel`.
- independent LCU implementations and generated challenge API models were audited before closeout. They show that treating this route as a single-field patch can erase unrelated challenge-profile preferences on clients where the route behaves as a replacement write.
- FACM therefore does **not** copy Akari's single-field POST literally. `LeagueChallengePreferencesService` first reads `/lol-challenges/v1/summary-player-data/local-player`, reconstructs the current title, challenge-token IDs, crest border and prestige-crest level, then changes only `bannerAccent` in the outgoing preference document. If `signedJWTPayload` is exposed by the current summary it is preserved as well.
- title sentinel `-1` is normalized to the empty-title representation rather than echoed back as an invalid title ID.
- missing title/challenge/crest/prestige preservation evidence fails closed without a write instead of risking a destructive replacement update.
- the dedicated `ILeagueChallengePreferencesWriteApi` remains hard-fenced to the exact POST route and cannot write chat, regalia, matchmaking, ChampSelect or arbitrary LCU paths.
- apply remains one user-directed POST followed by bounded first + settled summary readback. A result is called `success` only when `bannerAccent=2` is observed **and** title/challenge tokens/crest/prestige state still matches the pre-write snapshot. Missing evidence is `unverified`; drift/client overwrite is `overridden`; there is no rewrite loop.
- smoke coverage models replacement semantics deliberately: omission of preserved fields would clear the fake profile and fail the suite; incomplete pre-read fails closed; preservation drift cannot be reported as success; endpoint/method fencing remains covered.

## DONE — challenge-token cleanup parity

- Akari's remove-token action was audited: it sends `challengeIds: []` through the same challenge-preferences route and forwards its current banner selection.
- FACM reuses the preservation-safe `LeagueChallengePreferencesService` and the existing exact-route writer; there is no second preference/session owner.
- before a token cleanup write, FACM reconstructs the current title, banner accent, crest border, prestige-crest level and current challenge IDs; if the current numeric banner or any required preservation evidence is missing, it fails closed with zero POSTs.
- the outgoing replacement-safe document preserves title/banner/crest/prestige and exposed `signedJWTPayload`, and changes only the selected challenge-token list to `[]`.
- cleanup is one explicit user-directed POST followed by bounded first + settled summary readback. Success requires an empty challenge-token list while title/banner/crest/prestige remain unchanged.
- missing readback is `unverified`; restored tokens or preservation drift are `overridden`; FACM never enters a rewrite loop.
- banner-change and token-cleanup preservation checks are separate, preventing the banner flow from incorrectly comparing the new requested banner against the old pre-read banner.
- **召唤师外观 → 挑战徽章 → 清除徽章** exposes the action without introducing background polling.
- replacement-style fake coverage verifies exactly one POST, bounded readback, banner-evidence fail-closed behavior, unrelated-field preservation, client token restoration and endpoint fencing.
- validated token-cleanup functional head `b50549a2df4f84a3a8c9819177e25b8c06a5ef8c`: UI Text Contract #1011 PASS, Mayhem Source Probe #685 PASS, Windows Build #1903 PASS.

## DONE — account emote cleanup parity

- Akari's emote cleanup path was audited before implementation: it reads `GET /lol-loadouts/v4/loadouts/scope/account`, takes an account loadout ID, and PATCHes `/lol-loadouts/v4/loadouts/{id}` with a `loadout` document whose emote slots use `inventoryType=EMOTE` and `itemId=-1`.
- generated/public LCU API models independently confirm the PATCH route and `{ "loadout": ... }` body wrapper.
- FACM intentionally does **not** copy Akari's `data[0]` selection literally. `LeagueEmoteLoadoutService` requires exactly one account-scoped loadout with the complete audited emote-slot contract; zero, incomplete or multiple candidates fail closed without a write.
- the audited slot set is `EMOTES_ACE`, `EMOTES_FIRST_BLOOD`, `EMOTES_VICTORY`, `EMOTES_WHEEL_CENTER`, `EMOTES_WHEEL_UPPER`, `EMOTES_WHEEL_RIGHT`, `EMOTES_WHEEL_UPPER_RIGHT`, `EMOTES_WHEEL_UPPER_LEFT`, `EMOTES_WHEEL_LOWER`, `EMOTES_START`, `EMOTES_WHEEL_LEFT`, `EMOTES_WHEEL_LOWER_RIGHT`, and `EMOTES_WHEEL_LOWER_LEFT`.
- the outgoing payload touches only those 13 slots; unrelated account-loadout slots are not included in the PATCH document.
- `ILeagueEmoteLoadoutWriteApi` / `LeagueEmoteLoadoutWriteApiClient` shares the unique `LeagueClientSessionProvider` and is hard-fenced to PATCH plus one validated `/lol-loadouts/v4/loadouts/{id}` segment. Path traversal, query-string escape and wrong-method inputs are rejected.
- cleanup emits exactly one user-directed PATCH and then performs bounded first + settled reads of the account-scope collection. Success requires the same uniquely resolved loadout ID and all 13 audited slots at `itemId=-1`.
- missing/ambiguous ownership or readback fails closed/unverified; if the client restores an emote, FACM reports `overridden` and does not fight it with repeated writes.
- **召唤师外观 → 表情轮盘 → 清空表情** exposes the action; no background poller or second LCU connection is introduced.
- host smoke covers unique/ambiguous owner selection, narrow payload, unrelated-slot preservation, one-write/bounded-readback behavior, incomplete loadout fail-closed behavior, client restoration and writer fencing.
- validated emote functional head `befde045d3f4d1fdefb74024dc2031f33cdf7f84`: UI Text Contract #1020 PASS, Mayhem Source Probe #694 PASS, Windows Build #1912 PASS. Windows #1912 completed PetHost self-test, lightweight Release build, FACM.exe verification, signing step, package creation and artifact upload successfully.

## DONE — lobby / arbitrary-game inspection ownership audit

- Akari's `LobbyTool` is **not** a read-only inspection utility. It POSTs `/lol-lobby/v2/eligibility/party` and `/lol-lobby/v2/eligibility/self` to classify queue IDs, then explicitly creates a lobby with `POST /lol-lobby/v2/lobby { queueId }`.
- that lobby action is therefore a new mutation, not something FACM should smuggle into a read-only inspection pass. Existing FACM matchmaking/lobby owners remain authoritative until a separate explicit lobby-creation task audits mutation fencing and post-write reconciliation.
- Akari's `GameView` accepts an arbitrary game ID and delegates to `ConnectedMatchPreviewer`. The previewer chooses SGP when supported, otherwise LCU. Its LCU summary route is `GET /lol-match-history/v1/games/{gameId}` and its LCU detail route is `GET /lol-match-history/v1/game-timelines/{gameId}`.
- FACM already owns local-player match history in `LeaguePlayerDataService` and already uses `/lol-match-history/v1/games/{gameId}` only to enrich incomplete rows from the signed-in local player's bounded recent-history page. It has no equivalent arbitrary-game SGP/timeline preview owner today.
- a superficial LCU-only “GameView clone” would regress Akari's source fallback behavior and duplicate player/history ownership. No product code was added in this audit. Arbitrary cross-source game preview remains a separately scoped future enhancement if explicitly desired.

## ACTIVE — login-time signature / displayed-rank reapply lifecycle audit

- Akari's current implementation is now verified: it watches existing connected/chat-me state, resets its one-shot flag on disconnect or missing `chat.me`, waits 2 seconds for chat state to settle, then applies enabled status-message and displayed-rank automations once. Manual apply interrupts the pending automation. It does not continuously rewrite the fields.
- FACM already has the fenced, read-modify-write `LeaguePresenceService` needed for manual signature/display-rank changes, but its shared `AppSettings` currently does not persist a signature/rank reapply policy/value set and the existing Dashboard Gameflow owner is not equivalent to Akari's explicit chat-ready signal.
- next engineering decision: either identify a shared FACM client/chat readiness event that can own a once-per-session settle task without a new poller, or leave login-time reapply deferred. Do not approximate chat-ready with an unrelated recurring Gameflow loop.

## NEXT — remaining Akari toolbox/profile audit

1. login-time signature/displayed-rank reapply only if an existing shared chat/client lifecycle can own a bounded once-per-session settle task;
2. optional arbitrary cross-source game-ID preview only as a separately scoped future feature, not as an incomplete LCU-only clone;
3. queue-ID lobby creation only as a separately authorized mutation task with narrow fencing and reconciliation.

Verified upstream/reference routes so far:

- profile background: `GET/POST /lol-summoner/v1/current-summoner/summoner-profile`;
- banner preference / challenge tokens: `POST /lol-challenges/v1/update-player-preferences/` with preservation-safe preference semantics required by FACM;
- regalia read/write: `GET/PUT /lol-regalia/v2/current-summoner/regalia`;
- chat-card displayed rank: `PUT /lol-chat/v1/me` with `lol.rankedLeagueQueue`, `lol.rankedLeagueTier`, optional `lol.rankedLeagueDivision`;
- account emote loadout: `GET /lol-loadouts/v4/loadouts/scope/account`, then `PATCH /lol-loadouts/v4/loadouts/{id}` for the uniquely identified account emote loadout;
- Akari lobby eligibility / creation: `POST /lol-lobby/v2/eligibility/party`, `POST /lol-lobby/v2/eligibility/self`, then explicit `POST /lol-lobby/v2/lobby { queueId }`;
- Akari LCU game preview: `GET /lol-match-history/v1/games/{gameId}` plus `GET /lol-match-history/v1/game-timelines/{gameId}`, with SGP preferred when available.

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
