# GGman Glass — Owner-Approved Visual Direction

Status: **visual direction accepted 2026-10-21; desktop implementation not yet accepted.**
Source reference: [`docs/design/GGman-Glass-Prototype.html`](design/GGman-Glass-Prototype.html) (49,991 UTF-8 bytes, source SHA-256 `57407b7eb56181ec83f96c646ffd144db6eb9075f377288304f489218bfe9460`), preserved from the exact owner-reviewed interactive artifact. The HTML is a **demo**, not a production desktop implementation. Do not represent this document as an implemented desktop build.

## Approved design identity

Preserve the presented prototype rather than paraphrasing it as "iOS-style buttons":

- Classic iOS clarity with modern translucent surface layering, restrained highlights, specular border edges, saturation and soft shadows; glass exists in a coherent composition, not four uniformly rounded tiles.
- Desktop workbench has one elevated, rounded window with consistent sidebar and header, generous breathing space, a visually prioritized game-status hero, readable grouped controls, quiet secondary information, and familiar stateful interactions.
- Lightweight floating quick control center is a separate transient surface with real navigation; the full workbench is a persistent task window, not an outside-click-dismissed transient flyout.
- Dark and light variants preserve readable text, meaningful status hierarchy and intentional blur/shadow levels. Use system font fallback for Chinese; do not bundle unlicensed Apple fonts.
- Use a coherent SVG icon language, not Unicode letter/symbol placeholders. No fake live activity, game statistics, player rank, CloudBase status or network results.
- Responsive resizing, reduced-motion support, focus states, Escape and keyboard flows are part of the appearance contract, not later polish.

The accepted prototype includes **six demonstration routes**: overview, matches, guides, automation, profile and settings. This is an **information architecture presentation** and does not delete or merge the existing nine `LeagueHubNavigation` business routes. In implementation, preserve all existing routes, safety confirmation and feature reachability. Proposed prototype-to-domain mappings must explicitly allow the existing `dashboard`, `player`, `live`, `mayhem`, `profile`, `recommendation`, `efficiency`, `repair` and `presence` views.

## Proposed implementation architecture — not yet approved for rollout

1. Retain the .NET Framework 4.8 WinForms process as the authority for the floating ball, tray, process lifetime, game/LCU modules, update signing, CloudBase authentication and config sync. Do not introduce parallel services, pollers, or a second cloud client.
2. For maximal visual fidelity, **evaluate** locally hosted WebView2 as the on-demand main workbench renderer, retaining HTML/CSS tokens and composition. Keep the floating entry cheap and native until the user opens the larger workspace. This is an engineering hypothesis, not a completed framework decision.
3. Depend on the installed Evergreen WebView2 Runtime only after explicit runtime detection; never fetch untrusted remote UI content, navigate arbitrary URLs, or auto-install silently. Provide a native fallback when absent. No API token, auth cookie or CloudBase credential may be forwarded to page JavaScript.
4. Use a narrowly scoped JSON command bridge with explicit command allowlist, identity/session validation, cancellation and host-owned confirmations. All game writes, ESC/YAML, cloud uploads and updates remain in the existing host. Render text safely; no arbitrary eval, local origin access, or broad file-system privileges.
5. Design cards for status and command flows (read-only first), then wire existing actions one view at a time. Ensure historical nine routes remain discoverable and accessible. Keep game-time companion/ChampSelect visual paths native until measured; no browser process should be created on every gameflow event.
6. Preserve the host's established UI-text translation, theme IDs, cloud-authoritative settings and unsaved-draft guards. Switching pages and closing must not discard pending edits or inadvertently upload local changes.

## First integration acceptance gate

Before further page migration or formal publication, produce an isolated candidate and measure:

- Screenshot comparison at the same content, 100/125/150/200% scale, Win10 19045, dark/light and compact sizing, with focus indicators and Chinese text intact. Confirm the CSS browser preview's blurred desktop simulation is **not** misrepresented as native Windows backdrop/refraction.
- Idle baseline vs workbench-open vs workbench-closed memory **across the whole WebView2 process tree**, cold/warm startup, UI response, GPU use and real League lobby/game-state impact; document measured numbers and set owner-reviewed budgets, not invented thresholds.
- Opening/closing quick control, true outside-click dismissal only for transient surfaces, tray reactivation, modal ownership and unsaved-draft navigation.
- No remote fetch or silent runtime install. Missing WebView2 runtime must return to a working native shell. Correct resource release and no zombie browser processes after close.
- One consolidated **native** design and functional review for this candidate. CI green alone is not visual approval.

## What the review did *not* approve

The rejected WPF four-tile experiment in [PR #353](https://github.com/xianyumht-cmd/facm/pull/353) must not become the new baseline. The accepted prototype uses demo-only data; approval of visual direction does not authorize deployment, merge, updater changes, CloudBase writes, automatic League operations or replacement of source contracts.

## Design source retention

The original HTML is committed at `docs/design/GGman-Glass-Prototype.html` and identified by its byte count and source checksum above. The integration pilot must use this committed artifact as its starting baseline, with demo-only state clearly isolated from production bindings. Do not re-create the approved visual from a loose prose interpretation.