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
