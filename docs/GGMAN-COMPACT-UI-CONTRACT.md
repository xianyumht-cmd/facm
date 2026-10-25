# GGman Compact Surface — Approved UI and Live Pilot Contract

**Status:** visual baseline approved by owner on 2026-10-24; live business integration remains a draft pilot, not a signed release.

## Locked surface

- Do not replace or restyle the existing layered floating orb. The standard `LayeredFloatingBall` renderer and existing `MainForm` drag/toggle/desktop focus logic remain the owner.
- Home visual geometry: **380×285 logical pixels**. Detail visual geometry: **548×478 logical pixels**. Orb: **56×56**. DPI scaling/clamping is permitted solely to preserve accessibility on actual Windows displays; do not introduce user-driven free resize.
- Colors, typography, card spacing, corner radii, icon arrangement, element order, TOP10 location, tier badge placement, and page/back interaction are frozen to the `NativeCompactPreviewPanelForm` reference (`src/FACM/Presentation/Glass/NativeCompactPreview.cs`).
- Rendering the approved surface is not a valid reason to alter the existing signed GGman shell without an explicit release review. A pixel change needs renewed owner acceptance. Replacing demo labels with genuine current status is permitted; changing geometry is not.
- Visual evidence: native offline home/Mayhem screenshots from candidate workflow. No browser/Edge resize surrogate.

## Functional binding rules

1. Use the existing host, `LeagueClientModule`, `LeagueHubUiBridge`, gameflow/subscriber owner, and existing Mayhem parser. Never create a second continuous Gameflow polling loop, extra LCU session or new write route.
2. Real-mode decisions are fail-closed. Read a session/lobby on user open (bounded read-only GET); never present Mayhem tiers in ARAM, Summoner's Rift, or unknown queue. Read selected champion from the local player's ChampSelect cell, not an arbitrary roster slot.
3. TOP10 is one **complete** ranked list, version tagged, unique, ordered, and cached per mode/source; no per-avatar network request. Prefer reachable CN source and bounded fallback before international CDNs. Do not mark a ranking 'latest' without official CN-patch verification.
4. Missing/failed/stale data must show an honest empty state. The fake preview rankings and tiers must never flow into a live build. Do not claim full champion coverage when only top-ten stats are available.
5. Hero detail work and global ranking work must have independent cancellation. Never let an older hero or mode override a new selection. Stop outstanding work when the panel is hidden or destroyed.
6. Existing detailed functional forms remain callable as a transitional compatibility path; this is not yet pixel-matched completion of all deeply nested pages. Future in-panel functional integration must preserve the approved compact visual shell.
7. No external account identifiers or tokens in logs. The live pilot runs the normal GGman host and **may run existing account/cloud operations**, unlike the offline preview. Operators must close the official GGman first and never confuse the two entry scripts.

## Pilot entry and gates

- Offline immutable UI: `Start-Compact-Preview.cmd`.
- Opt-in live pilot: `Start-Compact-Live-Pilot.cmd` (sets `GGMAN_COMPACT_LIVE_PILOT=1`, invokes `--compact-live-pilot`; compiled only in the preview candidate).
- All work remains on Draft PR #355; main, signed releases, updater manifest and user's installed official GGman are unchanged.
- CI required: standard Windows Build, UI text contract, Mayhem live source probe, native painter screenshots, explicit queue/champion mode isolation tests and live-page paint smoke.
- Human field gates pending: Win10 19045 100/125/150/200% scaling; no white screen, clamping at all screen edges, exact focus and second-click behavior, actual Tencent League ChampSelect mode/champion projection, tier/version evidence, China network without VPN, and no ghost background processes after exit.

## Not yet completed

Full real data sources for non-Mayhem mode tiers, continuously updated in-match TOP10 display, actual bench champion avatars/swap actions, complete compact in-panel feature implementations, performance measurements on non-VPN residential networks, and authoritative verification of 26.xx patch freshness. These are separate stages and must not be represented as shipped in the initial live candidate.

## 2026-10-24 next-phase performance and bench integration (draft)

- Add persistent compact Mayhem TOP10 public-data cache at the current user's local app-data path, separate from user/account settings. Cache schema and source/mode/patch/ten-unique-ranked-rows are validated on read, with atomic replacement on write. First 20 minutes are fresh local data, 20 minutes to 24 hours are explicitly labelled *historical cache / not verified*, beyond 24 hours is rejected. The banner never asserts the CN latest patch.
- Start one lightweight mode-specific ranking prefetch immediately after Mayhem mode identification; the Mayhem page consumes the in-flight result rather than starting four per-avatar requests or unnecessarily cancelling it.
- Replace four hard-coded preview hero names in the live panel with actual LCU `benchChampions` IDs, resolved through one local LCU champion summary request. Unknown / absent bench heroes become an em dash; no false T0 badges. The offline preview fixture remains untouched.
- CI smoke covers serialized cache round-trip / corruption / expiry, duplicate bench IDs, valid local champion mapping and ranking-mode isolation. None of these checks is a measurement of unproxied mainland network latency.
- Still pending: genuine Win10 / LOL lobby test, source patch vs national patch confirmation, full-rank all-champion tier coverage, live match overlay, deep-page conversion to compact native surface, test with VPN disabled.

## 2026-10-25 — Full-source roster and event-driven ChampSelect refresh

- The original home/detail sizes, palette, corner radii, orb behavior, touch regions and page hierarchy remain locked. No editable resizing or browser-host window.
- Source-backed expanded rankings are now optional on `MayhemRankingSnapshot.AllChampions`. Validity requires 11–200 sequential, unique ranked rows, win-rate bounds, a patch-labelled complete TOP10 from the *same* source, and exact first-ten agreement. A corrupted expanded list must not invalidate a previously complete ten-row ranking.
- ARAMGG parsing may retain its complete standings; Hexdata may retain all valid champion rows. Neither data provider is allowed to supply an invented T0/T1 grade if the upstream exposes only rank. The same pre-existing badge slot displays factual `#N` instead. Other modes remain hidden unless their own grounded data exists.
- Expanded rows are stored in the same small, validated, atomic, age-bounded public cache; schema remains backward compatible with existing ten-only records. The first complete TOP10 paints without waiting on an entire external roster; a second bounded CN-only retrieval can fill remaining entries asynchronously. It can never silently replace a newer patch with older expanded source data.
- The existing `MainForm.ApplyGameflowState` observer drives updates in the opt-in pilot. No second Gameflow monitoring loop was created. While ChampSelect is observed, only bounded local reads are performed, and champion name metadata is reused for ten minutes. Manual preview selection is not reset merely because an unchanged live champion was observed again.
- Confidence boundary: exact network timing without VPN, match-overlay UX, all-champion tier labels beyond sources that expose actual tiers, version parity with the live Chinese client, and deep-page compact replacement remain subject to future test and implementation.

## 2026-10-25 — Mode isolation and correlated diagnostics

- The frozen surface remains unchanged; standard ARAM uses the existing ranking layout, without a newly resizable browser/overlay.
- The ordinary ARAM provider uses the distinct public `/en/champions-tier/aram` page. It requires an unambiguous ARAM-only title, parsable patch, ten sequential/unique rank rows, valid tier labels, and bounded win-rate values. Parsing a Mayhem or Ranked page as ordinary ARAM is forbidden.
- Summoner's Rift global tiering is deliberately disabled until player-role selection and role-specific champion ranking are verified; the same champion can appear at multiple roles in a ranked table, so a global hero grade would be misleading.
- In-game Gameflow suppression remains owned by `DesktopEntryGameflowPolicy`. The pilot must not unexpectedly reopen its floating entry or display a new topmost window while playing.
- `CompactTraceLog` writes bounded, low-volume structured `compact-YYYYMMDD.log` events with an ephemeral correlation ID shared with warning summaries in `facm-YYYYMMDD.log`. It never stores QQ/account identifiers, tokens, IPs, full URLs, player names, or LCU response bodies.
- The existing one-click diagnostic exporter now includes both log families for the current and prior day, with the same redaction, allowlist, ZIP size and skip rules. Logs are local only; no new telemetry upload occurs.
- An offline parser and diagnostic export smoke suite must pass before distributing a live candidate. A parser fixture does not prove the third-party site's live markup or non-VPN latency. Real-game and mainland-network validation remain separate mandatory acceptance gates.

## 2026-10-25 — Role-aware Summoner's Rift read-only pilot

- Existing 56x56 floating orb, 380x285 launcher and 548x478 detail surface remain frozen, including click-outside dismissal, fixed sizes, colors and geometry.
- A champion-select role is accepted **only** from the local player's `myTeam` item keyed by `localPlayerCellId`. Valid assignments are top/jungle/middle/bottom/utility (mapped to support); missing/fill/unknown roles and other players' positions must not activate a Rift ranking.
- The read-only `CompactRiftRankingService` selects a fixed allowlisted OP.GG `/lol/champions?position=<role>` page. Parsing requires a role-specific document title/heading, patch and at least ten sequential ranked champion rows with bounded winrates. It explicitly rejects ARAM/Mayhem pages; missing or changed upstream table markup fails closed. Tiers are only taken from actual source grade tokens, otherwise factual `#rank` is rendered. Source data is *not* claimed to match current Chinese-client patch without field verification.
- Every received ranking carries `ModeKey=rift:<role>`; the approved view rejects a snapshot from any other mode or Rift position. Position changes cancel the previous ranking request and clear old UI state without changing the approved geometry.
- Existing 2-second ChampSelect Gameflow notifications drive local role updates (no new polling loop). The existing `DesktopEntryGameflowPolicy` continues to hide the ball/panel in active games. No unsolicited overlay or focus-stealing popup is introduced in this phase.
- Component/step/outcome timings continue into bounded `compact-YYYYMMDD.log` using per-operation correlation IDs shared with `facm-YYYYMMDD.log`. The existing one-click diagnostic ZIP includes both, with redaction. Sensitive LCU bodies, account IDs, names and tokens never enter these traces.
- Offline regression must check role normalization, absence of local cell, wrong-lane page rejection, Rift/ARAM/Mayhem rendering isolation and unchanged geometry. Actual live-page parse success and China non-VPN timings **require** a real Windows user acceptance check and are not established by deterministic fixture tests.
