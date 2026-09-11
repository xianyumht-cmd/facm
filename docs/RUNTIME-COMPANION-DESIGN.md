# FACM Runtime Companion Design Contract

## Purpose

Modernize the existing Champion Select assistant into a compact Runtime Companion that appears beside League when context makes it useful. The target is not a miniature FACM dashboard. It is a quiet, dense, context-aware desktop tool that keeps the current 3.5 lightweight architecture and uses existing FACM runtime/data owners.

This document is the durable execution contract for the multi-stage Runtime Companion task. It is not a release request and does not authorize a production release or merge by itself.

## Execution prompt / standing implementation contract

You are implementing the FACM Runtime Companion modernization in `xianyumht-cmd/facm`.

Work from the canonical 3.5.x lightweight product: WinForms, .NET Framework 4.8, single `FACM.exe`. Read `AGENTS.md` and the canonical knowledge documents before repository changes. Keep the entire task on one short-lived task branch and one PR. Continue the phases in order. Do not create `v2`, `final`, `handoff`, `test`, or packaging branches.

Preserve all existing ownership boundaries. The Runtime Companion consumes the existing shared League Gameflow state. It must not create a second Gameflow poller or League session. UI code must not invent new direct LCU writes. Inline actions may call an existing FACM write owner only after its existing confirmation/context/postcondition contract is preserved. A recommendation without an existing safe write owner is display-only.

Akari is an interaction-density, window-behavior, and appropriate feature-workflow reference. FACM owns the visual language and architecture; Akari parity is adopted only when the capability fits FACM lightweight ownership and can be implemented safely. External GitHub UI/UX agents or skills are advisory review standards only; they are not runtime dependencies and web/CSS implementation guidance does not override WinForms constraints.

Use `github/awesome-copilot` `gem-designer` as the anti-template review standard and its UI/UX design guidance for hierarchy, state coverage, accessibility, and disciplined spacing. In particular, reject generic AI defaults: interchangeable SaaS card grids, wrappers without semantic purpose, pill clusters, purple/blue dual-accent styling, gratuitous gradients or glassmorphism, excessive rounding, ornamental icons, filler copy, and motion without hierarchy or feedback value.

Execute sequentially:

1. RUNTIME-COMPANION-0: establish this design/behavior contract and deterministic geometry/state expectations.
2. RUNTIME-COMPANION-1: replace the old 660 px horizontal assistant shell with a narrow vertical companion while preserving existing Bench/Mayhem behavior and data ownership.
3. RUNTIME-COMPANION-2: separate orchestration/presentation through a Runtime Companion snapshot/controller boundary without replacing existing services.
4. RUNTIME-COMPANION-3: implement context-specific Akari-style information hierarchy using existing build/live/Mayhem data sources and progressive disclosure.
5. RUNTIME-COMPANION-4: wire only existing safe FACM write owners into explicit inline actions with loading/success/partial/blocked/error feedback.
6. RUNTIME-COMPANION-5: complete DPI/multi-monitor/live-game UX, deterministic smoke/CI, canonical knowledge reconciliation, and one Windows review candidate.

Do not publish a new production version, edit `release/3.5-request.json`, enable online update metadata, merge the task PR, or perform destructive Git cleanup without explicit user intent at the appropriate closeout point.

## Visual thesis

A narrow real-time League side companion: compact, calm, icon-first, immediately actionable, and visually native to the existing FACM design system.

The user should read the current champion/context and the next useful decision in one glance. Density comes from alignment and progressive disclosure, not from wrapping every datum in a card.

## Source of truth and precedence

1. FACM runtime ownership, safety and repository rules.
2. `FacmDesignSystem`, `FacmThemeRuntime`, `FacmWindowChrome`, and shared WinForms primitives.
3. Existing League/Mayhem data and write owners.
4. This Runtime Companion design contract.
5. Akari interaction/window reference.
6. External GitHub design-agent/skill guidance as an audit reference.

External references must never cause a WinUI/WPF/web migration, a second theme engine, a second Gameflow owner, or a page-local transport/write path.

## Interaction model borrowed from Akari

Adopt:

- narrow, vertical side-window geometry;
- fixed identity/context area with scrollable detail content;
- high information density without equal-card grids;
- icon-led recognition with concise supporting text;
- actions placed next to the information they affect;
- `show more` / collapse patterns for secondary detail;
- explicit pin/collapse/close control;
- default presentation of the most useful recommendation instead of all alternatives.

Do not copy:

- Akari brand colors or typography;
- product-specific filter rows when LCU can already infer context;
- OP.GG/Akari brand hierarchy;
- web layout implementation techniques that do not map cleanly to native WinForms.

## Window geometry

Baseline at 100% DPI after live Tencent-client review feedback:

- target client width: 320 px;
- normal design range: approximately 300-340 px;
- normal expanded height cap: 560 px;
- small working areas additionally cap expanded height to about 70% of the monitor working-area height so the companion does not become a near-full-height sidebar;
- minimum preferred expanded height: approximately 420 px, but small-screen/DPI fitting may go lower rather than overflow the working area;
- header: approximately 36 px logical height;
- champion/context region: approximately 82 px logical height;
- Bench strip: approximately 58 px logical height when present;
- collapsed height: header-only;
- initial placement: upper-right of the active/League display using the existing episode popup owner;
- final geometry must be applied before first visible paint to avoid compositor residue.

The compact revision is intentionally smaller than the earlier 388x720 review candidate. Recommendation rows, alternative rows, champion icon, action buttons and the Mayhem augment surface are all reduced together; this is not an outer-window-only scale change. Mayhem augments use bounded local paging inside FACM-native rows rather than a native Details table or a system scrollbar.

The fixed top region must not scroll. The detail body may scroll vertically by wheel/input, but the transient companion must not expose a native light-themed WinForms scrollbar or a horizontal scrollbar at supported widths.

### Mode-specific information completeness

A non-null Build Advisor snapshot is not equivalent to a usable recommendation. For unsupported modes such as ARAM Mayhem, Runtime Companion must continue projecting the mode-specific guide when `HasBuild` is false. Mayhem should expose verified summoner spells, skill priority, starter items, boots and core build when those fields are available, followed by ARAM balance and augment ranking. Ordinary ranked/ARAM rune recommendations must not be fabricated into Mayhem when the active Mayhem source does not publish rune data.
When Riot game-data supplies augment rarity, the compact ranking may locally filter `全部 / 棱彩 / 黄金 / 白银`; switching the filter must not trigger a new external recommendation request. Event-choice or unknown rarity remains available through `全部` only.
For Mayhem fallback guidance, summoner spells, skill priority and item rows should prefer compact game-data icons with concise text retained as evidence/tooltips; icon loading remains presentation-only and must reuse the existing guide asset loader rather than introduce a new network owner.

### Live readability acceptance

The 320 px runtime surface must remain readable rather than merely fit. Primary recommendation text gets the first line(s), source metrics are secondary, and full detail may use tooltip/progressive disclosure. Raw champion ids such as `#904` are not a valid user-facing identity.

Native light WinForms scrollbars and Details-view table headers are not allowed on this dark transient surface. Bench overflow uses local paging. Mayhem augments use compact FACM-native icon rows with local paging and omit unavailable metrics instead of filling narrow columns with repeated placeholder dashes.

DPI targets for validation: 100%, 125%, 150%, 200%. Multi-monitor restore must clamp to the current working area and must never restore off-screen. DPI scaling may increase physical pixels, but the working-area proportional cap remains authoritative so compact displays do not regress into a full-height panel.

## Window lifecycle

Retain the current `LeagueHubModule` episode ownership unless a later phase has evidence that a smaller change is safer:

- one presentation per Champion Select episode;
- automatic popup does not create a second Gameflow observer;
- user close means dismiss for the current episode, not exit FACM;
- leaving Champion Select closes the automatic popup;
- opening the unified LOL Hub Live view remains compatible with automatic-popup ownership;
- automatic appearance must not steal keyboard focus from League;
- no taskbar entry for the transient companion.

Pin/collapse preferences may be persisted only through the existing settings owner when introduced. Do not create a private settings file from the Form.

## Information hierarchy

The final context-aware body should converge toward this order when the data applies:

1. champion identity + mode/position/patch context;
2. runes;
3. summoner spells;
4. skill priority/order;
5. starter items;
6. boots;
7. core build;
8. mode-specific sections such as Mayhem augments or ARAM balance;
9. Bench quick-swap strip only when Bench is genuinely available;
10. secondary metrics/details behind progressive disclosure.

Do not render empty decorative sections. If a section has no meaningful data for the current mode, omit it instead of showing repeated `N/A` cards.

## Mode-specific behavior

- Summoner's Rift / normal ranked: build-oriented context when existing services can provide it. Do not show Mayhem-only content.
- ARAM: ARAM-relevant build/balance context and Bench when available.
- Mayhem: Mayhem-specific build plus ranked augment recommendations and Bench when available.
- Champion not yet resolved: compact waiting/context state rather than a grid of empty placeholders.
- InGame: later phases may collapse or hide according to existing Gameflow visibility decisions; do not introduce a new in-game poller.

## Visual rules

Use only shared FACM semantic colors for normal product chrome/content:

- `Canvas`, `CanvasRaised`;
- `Surface`, `SurfaceRaised`, `SurfaceHover`;
- `Border`, `BorderSoft`;
- `Text`, `TextMuted`;
- `Accent`, `Success`, `Warning`, `Error`, `Disabled`.

New visible Runtime Companion code must not establish a private `Color.FromArgb(...)` palette. Domain images/icons may retain their real source colors.

Use the existing shared geometry limits. Do not revive large glass radii. Prefer dividers, alignment, spacing, and typography before adding another container.

One clear accent is enough. Danger color is reserved for destructive/error semantics. Status colors must not be used as decoration.

## Anti-AI-template rejection gate

Fail visual review if the companion introduces any of these without a concrete semantic need:

- repeated equal-size card matrix;
- large blue/purple gradients;
- glass/glow blobs;
- pills as the default control for every choice;
- large heading + large whitespace that hides useful data in a small companion;
- decorative icon next to every label;
- multiple competing accent colors;
- rounded wrappers around ordinary text rows;
- filler explanatory copy instead of current League context;
- animations that do not communicate state or feedback.

## Interaction states

Every actionable control introduced by this task must define the states that apply:

- default;
- hover;
- keyboard focus where keyboard reachability is appropriate;
- pressed/active;
- disabled;
- loading/busy;
- success;
- blocked/partial/error where the operation can fail.

Do not report success from an LCU HTTP response alone when an existing owner already requires postcondition verification.

## Existing write-owner boundary

Runtime Companion UI must not call raw LCU write methods because it wants an Akari-style `Apply` button.

Known example: `LeagueBuildApplyService` already owns rune/summoner-spell apply planning, phase/champion/queue revalidation, writes, and settled postcondition verification. A future inline action must route through that owner rather than reimplementing writes in the Form.

Other actions must be audited one by one. No existing safe owner means display-only until a separately justified owner is added in the proper module/service layer.

## Explicit quit-current-Champion-Select action

Akari-style workflow parity includes an explicit one-click action that leaves the current Champion Select episode while preserving the existing lobby/party. FACM implements this through a dedicated single-route owner for `POST /lol-lobby-team-builder/champ-select/v1/session/quit`; the Runtime Companion Form never owns the endpoint. The transaction preflights ChampSelect, sends exactly one POST, then verifies that Gameflow left ChampSelect and `/lol-lobby/v2/lobby` still exists. It must never fall back to killing LeagueClient processes or deleting the lobby. The button remains disabled outside a live ChampSelect session and its tooltip makes clear that League's own dodge penalties can still apply.

## Performance rules

- no second Gameflow monitor;
- no second League client/session owner;
- preserve existing Bench and Mayhem cache/network paths;
- use champion/mode/position/patch fingerprints before refreshing external recommendation data;
- cancel stale async work when champion/context changes;
- do not add arbitrary first-action sleeps;
- collapsed/hidden surfaces should avoid non-essential visual refresh work;
- image failure must leave readable text/content rather than blocking the whole recommendation.

## RUNTIME-COMPANION-1 acceptance contract

The first implementation phase changes shell/layout only and must preserve the current feature boundary:

- current Bench availability logic remains authoritative;
- quick-swap uses the existing `LeagueBenchQuickPickService`;
- current automatic Mayhem guide still uses `MayhemAutomaticGuideService`;
- current generation/cancellation behavior remains intact;
- current `ShowWithoutActivation` behavior remains intact;
- current episode ownership remains in `LeagueHubModule`;
- no new external data source and no new League write path;
- shell width moves from the historical 660 px horizontal strip to the narrow companion range;
- fixed header/context and vertically scrollable detail region replace the historical expand-from-horizontal-strip presentation;
- theme colors come from `FacmDesignSystem`;
- final geometry is established before first visible paint;
- deterministic smoke covers key geometry and projection invariants.

## Later-phase acceptance direction

A final candidate is not ready only because it looks modern. It must also prove:

- all relevant modes render meaningful data without empty decorative sections;
- primary recommendations remain readable at the 320 px baseline and no native white scrollbar/table chrome appears;
- pin/collapse/close behavior remains predictable;
- repeated Champion Select episodes do not leak Forms, timers, requests, bitmaps, or event handlers;
- context changes cannot apply a stale build to another champion or queue;
- direct/cached data paths preserve their current fallbacks;
- WinForms remains responsive under slow or unavailable external data;
- CI passes before a review candidate is handed off;
- live Tencent-client review confirms 100%, 125%, 150%, and 200% DPI behavior before closeout.
