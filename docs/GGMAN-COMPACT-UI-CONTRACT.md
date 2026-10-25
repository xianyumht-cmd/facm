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
