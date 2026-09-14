# Runtime Companion live acceptance

This checklist belongs to draft PR #283 and is intentionally pre-release. Passing CI is necessary but does not replace a final real Tencent League client check. Development after 2026-09-13 is explicitly allowed to continue without pausing for another incremental manual test; the items below remain the consolidated closeout checklist.

## Champion Select surface

- Enter Champion Select and verify the Runtime Companion appears once for the episode without stealing focus.
- For Ranked / modes with Build Advisor support, verify champion/context, runes, summoner spells, skill order, starter items, boots and core items render as separate modules for the selected champion; alternative rune/build schemes must remain grouped by scheme rather than flattened into one list.
- Verify the scroll-body `近期使用` module is local-player-only: it should use a bounded recent sample for the currently selected champion, show the sample size, current-champion games, wins/losses and average K/D/A when resolved data exists, and show no fabricated zero-performance row when that champion has no recent resolved games.
- Switch the local selected champion while the recent-history read is in flight and verify stale statistics from the previous champion never appear under the new champion. Teammates/opponents must not trigger automatic match-history requests.
- For ordinary Ranked / Summoner's Rift Build Advisor, verify runes stay grouped by full scheme while the primary scheme uses compact LCU perk icons when available; skill priority uses compact source-derived Q/W/E/R tokens; summoner-spell, starter-item, boots and core-item rows remain icon-first. Missing assets must fall back to readable token/text behavior.
- When the existing OP.GG build payload contains `counters`, verify the compact `克制` row appears after the core build, shows up to five counter champions as LCU champion icons, keeps champion names available through tooltips/text fallback, and disappears entirely when counter data is absent. If an enemy champion that is already revealed by the client also exists in that same OP.GG counter list, verify that counter is moved to the front while the remaining counters retain source order. If both the local player and a revealed enemy have explicit assigned positions, an exact-position source counter may win priority over other revealed source counters and its own OP.GG sample/win evidence should stay aligned. Revealed enemies that are not in the source list must not be invented or injected. This presentation-only ordering must not create another OP.GG request, another ChampSelect read or any LCU write path.
- Verify the existing ChampSelect session payload projects the compact countdown plus `我方阵容 / 对方阵容` rows without a second session request: allied pick intent may be shown, enemy information remains limited to actually exposed client data, and ban counts are informational only.
- In those existing ally/enemy draft rows, verify exposed `Spell1Id` / `Spell2Id` values are rendered only as compact presentation context from the already-read snapshot: common known IDs should show readable labels such as `Flash/Ignite`, a missing slot should be omitted, and an unknown exposed ID should remain explicit as `S<ID>` instead of being guessed. This must not trigger a per-player lookup, a summoner-spell catalog request or another ChampSelect read. A fully anonymous hidden enemy must remain absent even if spell IDs are present internally; spell metadata alone must never reveal or manufacture enemy identity.
- For ARAM Mayhem, do **not** expect Ranked/ordinary-ARAM rune recommendations when the active Mayhem source does not publish a rune module. Verify the mode-specific guide instead renders summoner spells, skill priority, starter items, boots and core build when available, followed by ARAM balance and augment ranking.
- For Mayhem fallback guidance, verify summoner spells, skill priority, starter items, boots and the first core path prefer compact game-data icons while retaining concise text/tooltips; missing images must degrade to readable token/text fallbacks instead of hiding the recommendation.
- For Mayhem augments, verify Riot game-data rarity produces local `全部 / 棱彩 / 黄金 / 白银` filters when those categories are present; changing category must only filter the already-loaded ranking and must not start a new external recommendation request.
- Verify each augment row shows the localized rarity label rather than raw values such as `kGold`, and event-choice/unknown rows remain available through `全部` without being misclassified.
- For ARAM/Mayhem, verify Bench and Mayhem-only sections appear only in the intended queues.
- Verify rune/spell apply and item-set import still use their existing guarded owners.

## One-click leave Champion Select

- Press the compact `退` action only while Champion Select is live.
- Verify the client leaves the current Champion Select.
- Verify the existing lobby/party remains available after the operation.
- Verify FACM does not terminate LeagueClient/LeagueClientUx and does not delete the lobby.
- Record the Tencent-client result and any dodge/queue penalty separately; penalties are owned by League, not FACM.

## Akari-style lightweight automation

- Auto matchmaking minimum party size: test at the boundary below and at the configured value.
- Matchmaking start delay: test 0 seconds and a non-zero value, then leave Lobby during the delay and verify cancellation.
- ReadyCheck auto-accept delay: test 0 seconds and a non-zero value, then leave ReadyCheck/disable auto accept during the delay and verify cancellation.
- Matchmaking stop strategy `永不`: verify FACM never sends the automatic search DELETE even when a queue runs longer than its estimate.
- Matchmaking stop strategy `固定时间`: configure a short non-zero value, start matchmaking, verify FACM stops the current search only after the configured duration, and verify leaving Matchmaking before the timer expires cancels the pending stop.
- Matchmaking stop strategy `超过队列预估时间`: verify FACM uses the client's existing matchmaking search elapsed/estimated fields and stops only after elapsed time reaches/exceeds a valid positive estimate. Missing/zero/malformed estimates must fail closed and leave the queue untouched.
- For either automatic stop mode, transition to `ReadyCheck` before a pending stop and verify the old Matchmaking stop is cancelled immediately; FACM must never DELETE the search route from ReadyCheck.
- Verify a successful automatic stop is based on post-write `/lol-matchmaking/v1/search` reconciliation (`isCurrentlyInQueue=false`), not on HTTP 2xx alone.
- Confirm minimum-party, start-delay, accept-delay and matchmaking-stop settings survive restart through the shared AppSettings/LKG path.

## Presence and summoner profile

- Open the existing online/presence surface and verify the six existing presence modes still work without creating a second League session owner.
- Set a non-empty chat signature, then clear it with an empty value. In both directions, verify availability, gameStatus and unrelated Presence fields survive; if the client restores another value, FACM must report the overwrite and must not enter a rewrite loop.
- Apply a displayed-rank queue/tier/division combination and verify only chat/social-card display metadata changes. Actual server rank, LP and match history must remain unchanged. For `MASTER / GRANDMASTER / CHALLENGER`, no stale division should be retained.
- Open **召唤师外观**, choose an explicit champion/skin and apply the profile background. Verify the client reports the selected `backgroundSkinId`; FACM must not claim to unlock or purchase a skin.
- Run **隐藏等级边框** and verify current banner type is preserved while the requested prestige-crest state is read back. If the client overwrites it, FACM must report that state instead of repeatedly writing.
- Before **切换为上赛季旗帜**, record the current challenge title, selected challenge tokens, crest border and prestige-crest level. Run the action once and verify the resulting banner uses the audited last-season accent while all recorded unrelated challenge-profile preferences remain unchanged.
- If the current challenge summary cannot provide enough title/token/crest/prestige evidence to reconstruct the replacement preference document safely, the banner action must fail closed without sending a destructive partial POST.
- If banner readback is missing, call the result unverified; if the banner or a preserved challenge-profile field drifts, report overridden. HTTP 2xx alone is not enough to call the operation verified success.
- Before **清除徽章**, record the current title, banner accent, crest border, prestige-crest level and selected challenge-token list. Run the action once and verify the resulting challenge-token list is empty while title/banner/crest/prestige remain unchanged.
- If the current challenge summary cannot provide the numeric banner or another required preservation field, **清除徽章** must fail closed with zero preference POSTs. If the client restores one or more tokens or changes a preserved field during settled readback, report overridden instead of starting a rewrite loop.
- Before **清空表情**, verify the account-scope loadout query resolves exactly one loadout containing the complete audited 13-slot emote contract. Zero, incomplete or multiple candidates must leave the button operation unavailable and send no PATCH.
- Run **清空表情** once and verify all audited `EMOTES_*` slots read back with `itemId=-1` on the same uniquely resolved loadout. The request must not include unrelated account-loadout slots such as companion/customization entries.
- If account-loadout readback disappears or becomes ambiguous, report unverified. If the League client restores any audited emote slot after the first successful readback, report overridden; FACM must not repeat the PATCH in a fight-loop.
- None of the profile/presence actions may start a background fight-loop against LeagueClient.

## Window behavior

- Drag, pin/unpin and collapse/expand, restart FACM and verify persisted state.
- Test 100%, 125%, 150% and a secondary monitor if available; the window must remain on-screen and unclipped.
- Verify compactness does not hide the Mayhem build modules behind an unsupported Build Advisor shell, and that the champion title resolves to a real champion name instead of a raw internal `#ID` token.
- At the 320 px baseline, verify left-side section captions never overlap the first character/icon of recommendation content in Ranked or Mayhem.
- At the 320 x 420 logical baseline, the window stays deliberately short: no individual module or `更多` control may be vertically clipped, while lower recent-use/build/ARAM/augment modules remain reachable through the existing body wheel scroll. Bench/header/context stay fixed and the hidden native scrollbar must not reappear.
- In Mayhem with Bench enabled, every clickable Bench champion portrait must be fully visible vertically. The 44 x 44 decoded champion image is intentionally zoomed into the compact 44 x 38 button instead of being assigned as an unscaled `Button.Image`, and the fixed Bench host must have enough height for its title + controls + margins.

## Latest live observations

- Tencent live review covered a Summoner's Rift / training Champion Select and ARAM Mayhem.
- The 320 px horizontal geometry is accepted; no further width increase is requested.
- The previous 560 px logical height still felt too tall beside the League client. The current review baseline is 420 px maximum logical height, a 25% reduction, with content reached by the existing body wheel scroll rather than by shrinking text or restoring native scrollbars.
- The `更多 2` progressive-disclosure button was visibly clipped vertically on live Windows. Its compact geometry is now 58 x 24 at y=33 with centered text/padding so the glyph baseline remains fully visible under DPI scaling.
- The Ranked/Summoner's Rift clipping was traced to a 12 px caption/content column overlap in the compact recommendation rows and is already corrected.
- The Mayhem bottom clipping was traced to body vertical padding exceeding the exact first-viewport budget when Bench + five guide rows + ARAM balance were visible; with the intentionally shorter window, lower modules scroll instead of being forced into the first viewport.
- The Mayhem Bench crop was traced to two simultaneous geometry causes: the old 58 px Bench host was shorter than its title/button/padding stack, and a decoded 44 x 44 bitmap was assigned unscaled to a 44 x 38 `Button.Image`. The current implementation uses a 64 logical px fixed Bench host, reduced dead vertical padding, and a zoomed background image inside the 44 x 38 button. A deterministic fit check protects the fixed Bench stack.
- The ordinary Build Advisor presentation now projects existing LCU catalog `iconPath` data into grouped rune icons, summoner-spell/item icons and counter-champion icons. Skill priority remains source-derived and is rendered as compact Q/W/E/R tokens instead of inventing ability metadata.
- **2026-09-12 live retest accepted the Bench portrait correction and the icon-first presentation that preceded the latest context extension as visually good.** No further width/height increase was requested from that pass.
- OP.GG `counters` are no longer hidden: the display-only `克制` row reuses champion-summary icon data, caps the row at five champions, and introduces no new network or write owner.
- The 2026-09-13 draft-aware counter extension intersects only the already-fetched OP.GG counter row with enemy champions already revealed in the same ChampSelect snapshot. Matching counters are reordered only in the cloned Runtime Companion presentation; when explicit positions are available an exact-position revealed source counter gets priority, and its source sample evidence remains aligned. The Build Advisor owner's source snapshot and source ordering remain untouched outside this surface. No hidden pick intent is used.
- The existing lightweight ChampSelect read projects the selection countdown, ally/enemy draft rows and ban counts. This reuses the same session payload and does not create another Gameflow observer, another ChampSelect GET, or automatic per-player match-history/scouting fan-out.
- Allied draft rows may use local-team pick intent. Enemy rows fail closed to information actually exposed by the client. The local player tooltip uses `你` rather than product branding.
- The 2026-09-13 local recent-use extension reuses the already-owned `LeaguePlayerDataService` and reads history only for the signed-in local account. It does not add teammate/opponent history lookup, another Gameflow owner or another ChampSelect read. Its result is generation-fenced to the current local champion and rendered progressively in the scroll body.
- The screenshot-driven automation extension now adds `永不 / 固定时间 / 超过队列预估时间` to the existing next-game settings. It consumes the shared `LeagueDashboardModule` Gameflow stream, so it adds no second Gameflow monitor. The stop writer is the existing matchmaking transport with only `DELETE /lol-lobby/v2/lobby/matchmaking/search` newly allowlisted; arbitrary DELETE targets remain rejected. ReadyCheck is explicitly outside the armed Matchmaking phase despite sharing the Queueing activity bucket.
- Deep match history, long-term trends and richer post-game analysis stay in their existing FACM-owned views instead of being duplicated into the 320 px transient surface. In-game overlay ownership is likewise not introduced by this Champion Select-only task.
- The same correction series removes the remaining raw `#championId` fallback from the visible champion title; unresolved names stay in the localized resolving state until game-data resolves them.
- The deterministic smoke contract matches the intentionally scrollable 420 px surface: it protects a useful body viewport and the full `更多` control bounds, but no longer requires all build/ARAM/augment/draft/recent-use content to fit in the first viewport.

## Consolidated candidate rule

Do not create an intermediate user-test requirement after every lightweight development batch. Keep this file as the final batched Tencent-client checklist. Product-source commits may continue to accumulate on the draft task branch while automated gates stay authoritative for code regressions.

No production version bump, online manifest change, merge, release or destructive cleanup is authorized by continuing development.

## Closeout rule

Do not merge, release or bump the production version until the consolidated live Tencent-client checks above are eventually accepted and the closeout head has green Windows Build, UI Text Contract and Mayhem Source Probe checks.
