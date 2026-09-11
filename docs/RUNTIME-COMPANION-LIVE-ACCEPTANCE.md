# Runtime Companion live acceptance

This checklist belongs to draft PR #283 and is intentionally pre-release. Passing CI is necessary but does not replace a real Tencent League client check.

## Champion Select surface

- Enter Champion Select and verify the Runtime Companion appears once for the episode without stealing focus.
- Verify champion/context, runes, summoner spells, skill order, starter items, boots and core items render correctly for the selected champion.
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

## Closeout rule

Do not merge, release or bump the production version until the live Tencent-client checks above are accepted and the current PR head has green Windows Build, UI Text Contract and Mayhem Source Probe checks.
