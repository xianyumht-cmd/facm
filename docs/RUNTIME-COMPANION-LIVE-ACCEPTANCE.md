# Runtime Companion live acceptance

This checklist belongs to draft PR #283 and is intentionally pre-release. Passing CI is necessary but does not replace a real Tencent League client check.

## Champion Select surface

- Enter Champion Select and verify the Runtime Companion appears once for the episode without stealing focus.
- For Ranked / modes with Build Advisor support, verify champion/context, runes, summoner spells, skill order, starter items, boots and core items render as separate modules for the selected champion; alternative rune/build schemes must remain grouped by scheme rather than flattened into one list.
- For ordinary Ranked / Summoner's Rift Build Advisor, verify runes stay grouped by full scheme while the primary scheme uses compact LCU perk icons when available; skill priority uses compact source-derived Q/W/E/R tokens; summoner-spell, starter-item, boots and core-item rows remain icon-first. Missing assets must fall back to readable token/text behavior.
- When the existing OP.GG build payload contains `counters`, verify the compact `克制` row appears after the core build, shows up to five counter champions as LCU champion icons, keeps champion names available through tooltips/text fallback, and disappears entirely when counter data is absent. This display-only row must not create another OP.GG request or any LCU write path.
- Verify the existing ChampSelect session payload projects the compact countdown plus `我方阵容 / 对方阵容` rows without a second session request: allied pick intent may be shown, enemy information remains limited to actually exposed client data, and ban counts are informational only.
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
- Confirm settings survive restart through the shared AppSettings/LKG path.

## Window behavior

- Drag, pin/unpin and collapse/expand, restart FACM and verify persisted state.
- Test 100%, 125%, 150% and a secondary monitor if available; the window must remain on-screen and unclipped.
- Verify compactness does not hide the Mayhem build modules behind an unsupported Build Advisor shell, and that the champion title resolves to a real champion name instead of a raw internal `#ID` token.
- At the 320 px baseline, verify left-side section captions never overlap the first character/icon of recommendation content in Ranked or Mayhem.
- At the 320 x 420 logical baseline, the window stays deliberately short: no individual module or `更多` control may be vertically clipped, while lower build/ARAM/augment modules remain reachable through the existing body wheel scroll. Bench/header/context stay fixed and the hidden native scrollbar must not reappear.
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
- **2026-09-12 live retest accepted the Bench portrait correction and the icon-first presentation that preceded this final batch as visually good.** No further width/height increase was requested from that pass.
- OP.GG `counters` are no longer hidden: the display-only `克制` row reuses champion-summary icon data, caps the row at five champions, and introduces no new network or write owner.
- The existing lightweight ChampSelect read now also projects the selection countdown, ally/enemy draft rows and ban counts. This reuses the same session payload and does not create another Gameflow observer, another ChampSelect GET, or automatic per-player match-history/scouting fan-out.
- Allied draft rows may use local-team pick intent. Enemy rows fail closed to information actually exposed by the client. The local player tooltip uses `你` rather than product branding.
- Deep match history, long-term trends and richer post-game analysis stay in their existing FACM-owned views instead of being duplicated into the 320 px transient surface. In-game overlay ownership is likewise not introduced by this Champion Select-only task.
- The same correction series removes the remaining raw `#championId` fallback from the visible champion title; unresolved names stay in the localized resolving state until game-data resolves them.
- The deterministic smoke contract matches the intentionally scrollable 420 px surface: it protects a useful body viewport and the full `更多` control bounds, but no longer requires all build/ARAM/augment/draft content to fit in the first viewport.

## Final batched candidate before consolidated live test

The implementation batch is feature-frozen for one consolidated real-machine check rather than incremental per-change testing. Product source milestones in this batch are:

- `d181e4e54b17afc521ff59d1760232883f752d7d` — counter-matchup row and champion icons;
- `5b2cac80cfe7dc6c9b52853ec74d788be923072c` — countdown plus ally/enemy draft-context projection from the existing session read;
- `6a09f13091010ed716c61b110642042a97e14bd4` — grouped rune icons and compact skill-priority tokens;
- `1fd7df4febb12520f314939791aaa2299b0d6b7a` — final draft-context copy/tooltip polish.

No production version bump, online manifest change, merge, release or destructive cleanup is authorized by this feature freeze.

## Closeout rule

Do not merge, release or bump the production version until the consolidated live Tencent-client checks above are accepted and the current PR head has green Windows Build, UI Text Contract and Mayhem Source Probe checks.
