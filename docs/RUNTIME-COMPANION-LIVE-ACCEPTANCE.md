# Runtime Companion live acceptance

This checklist belongs to draft PR #283 and is intentionally pre-release. Passing CI is necessary but does not replace a real Tencent League client check.

## Champion Select surface

- Enter Champion Select and verify the Runtime Companion appears once for the episode without stealing focus.
- For Ranked / modes with Build Advisor support, verify champion/context, runes, summoner spells, skill order, starter items, boots and core items render as separate modules for the selected champion; alternative rune/build schemes must remain grouped by scheme rather than flattened into one list.
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
- ReadyCheck accept delay: test 0 seconds and a non-zero value, then leave ReadyCheck/disable auto accept during the delay and verify cancellation.
- Confirm settings survive restart through the shared AppSettings/LKG path.

## Window behavior

- Drag, pin/unpin and collapse/expand, restart FACM and verify persisted state.
- Test 100%, 125%, 150% and a secondary monitor if available; the window must remain on-screen and unclipped.
- Verify compactness does not hide the Mayhem build modules behind an unsupported Build Advisor shell, and that the champion title resolves to a real champion name instead of a raw internal `#ID` token.
- At the 320 px baseline, verify left-side section captions never overlap the first character/icon of recommendation content in Ranked or Mayhem.
- At the 320 x 420 logical baseline, the window stays deliberately short: no individual module or `更多` control may be vertically clipped, while lower build/ARAM/augment modules remain reachable through the existing body wheel scroll. Bench/header/context stay fixed and the hidden native scrollbar must not reappear.

## Latest live observations

- Tencent live review covered a Summoner's Rift / training Champion Select and ARAM Mayhem.
- The 320 px horizontal geometry is accepted; no further width increase is requested.
- The previous 560 px logical height still felt too tall beside the League client. The next review baseline is 420 px maximum logical height, a 25% reduction, with content reached by the existing body wheel scroll rather than by shrinking text or restoring native scrollbars.
- The `更多 2` progressive-disclosure button was visibly clipped vertically on live Windows. Its compact geometry is now 58 x 24 at y=33 with centered text/padding so the glyph baseline remains fully visible under DPI scaling.
- The Ranked/Summoner's Rift clipping was traced to a 12 px caption/content column overlap in the compact recommendation rows and is already corrected.
- The Mayhem bottom clipping was traced to body vertical padding exceeding the exact first-viewport budget when Bench + five guide rows + ARAM balance were visible; with the new intentionally shorter window, lower modules are expected to scroll instead of being forced into the first viewport.
- The same correction pass removes the remaining raw `#championId` fallback from the visible champion title; unresolved names stay in the localized resolving state until game-data resolves them.
- The deterministic smoke contract now matches the intentionally scrollable 420 px surface: it protects a useful body viewport and the full `更多` control bounds, but no longer requires all Mayhem build + ARAM content to fit in the first viewport.
- Review candidate head `838c83c5476419604043216f3decc96094ce6083` passed UI Text Contract #903, Mayhem Source Probe #566 and Windows Build #1795. The newer compact-height candidate requires fresh CI before it is treated as the next live-test build.
- Windows Build #1795 produced FACM 3.5.38, 2,067,352 bytes, SHA-256 `E10FD41373ADB914CCBEC7F1F2C3E2147D6474B9E6AC3E6EF30D19DBA73FFA92`.

## Closeout rule

Do not merge, release or bump the production version until the live Tencent-client checks above are accepted and the current PR head has green Windows Build, UI Text Contract and Mayhem Source Probe checks.