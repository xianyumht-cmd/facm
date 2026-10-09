# GGman Product Experience Specification (design baseline, not approved for release)

Status: **Review candidate — 2026-10-09**. Applies to the maintained **GGman 3.5.x lightweight / .NET Framework 4.8 / WinForms / single GGman.exe** product. This document defines product and interaction intent; source code and current real-client behavior remain authoritative until a review candidate is approved. Do not change release metadata, merge, or publish from this document alone.

## Product thesis

GGman should feel like a compact, trustworthy Windows utility built around League of Legends workflows, rather than a collection of unrelated feature windows. The user should be able to recognize what is connected, what can safely be done now, what happened after an action, and how to recover from failure.

Product audiences:
- First-time recipients who have never seen the application and need an obvious starting point.
- Frequent players who want one or two high-frequency actions without leaving the game.
- Advanced users configuring automation, diagnostics, desktop pets, privacy and local account history.

Non-goals: rewrite the application to WinUI/Electron, replace League/Mayhem data owners, invent unverified match data, force cloud sign-in, add game injection/memory reading, and replace working flows merely to satisfy cosmetic consistency.

## Window system and ownership

1. **Floating ball / tray** — passive process entry. Left click toggles quick access; drag moves the entry with a movement threshold; right click exposes an accessible native context menu. Explicit hide always preserves a tray recovery path. Esc only dismisses a transient surface. Closing a window is not exiting the process.
2. **Quick access flyout** — approximately 400 logical px wide, contextual, nonblocking. Four first-class actions: Workbench, Match History, Guides, Tools. Connection state, settings and update status are secondary. Initial users can always discover Workbench. Quick access does not host large forms or complex settings.
3. **Main workbench** — approximately 1120 logical px preferred width. One persistent left navigation column and one content canvas. Navigation: Overview, Matches, Guides, Automation, Tools, My GGman; Settings sits at the bottom. Contextual actions are placed in the page body only when relevant. No competing persistent right dock or global second-level tab strip. A page may use local tabs or filters when content demands it.
4. **Champion-select companion** — approximately 320–340 logical px wide, vertical, transient. Champion identity and game mode stay prominent; recommendation, spell, item and Mayhem sections show only data that actually exist for the current mode. Never steal keyboard focus on automatic appearance; never silently issue LCU writes. Close/dismiss affects this window episode, not League itself. Existing pin/collapse and position behavior must remain compatible.
5. **Dialogs / update center / diagnostic windows** — consistent shell, native keyboard behavior, typed status and confirmation semantics. Updates must show check -> download -> verify -> apply -> restart and failure recovery separately.

Navigation IDs, user settings, existing Gameflow owner and update assets must remain compatible. Remap visible routes through a central route table rather than renaming legacy IDs or creating a second navigation stack.

## Information architecture and screen responsibilities

| Entry | User question | First screen | Secondary detail |
|---|---|---|---|
| Overview | What is happening now? | League connection, phase, 3 relevant actions | Source and refresh timestamp |
| Matches | What happened in my games? | Account selector, search/filter, actual match results | Match / player details |
| Guides | What should I do with this champion? | Champion search/current champion, build/mode context | Runes/spells/items, Mayhem |
| Automation | What is enabled, and under which conditions? | Grouped setting rows and active states | Constraints, timing, failure results |
| Tools | How can I diagnose/fix a problem? | Diagnostics first, safe tasks | Preview changes, explicit confirmation |
| My GGman | What history belongs to me? | Local-first profile, identifiable account vs confirmed game distinction | All accounts, metadata, backup/privacy |
| Settings | How does GGman behave? | Appearance, hotkeys, floating entry, privacy, updates | Restore defaults / advanced details |

Existing nine League Hub view IDs (dashboard/player/live/mayhem/profile/recommendation/efficiency/repair/presence) should be preserved internally and routed through these visible groupings. Do not delete functionality in the name of navigation simplification.

## First-run / discovery

- Do not make a first-time user hunt for a floating ball. Show a dismissible first-run orientation once on a fresh install, with prominent Workbench and quick actions, and a way to skip it.
- Client detection is nonblocking. The disconnected state must offer offline tools and a precise League next step; never describe process detection as a verified LCU connection.
- A newly opened page displays meaningful local state immediately; remote requests run with bounded cancellation, and may subsequently update the view.
- The first minute should let an unfamiliar user find and attempt one useful feature without instructions. This is a usability acceptance goal, not a current measured result.

## Shared visual tokens (proposed, not final calibrated palette)

- Semantic: canvas, surface, surfaceRaised, border, text, textMuted, textSubtle, accent, accentSubtle, success, warning, error, focus, disabled.
- First candidate dark palette: canvas `#10151B`, surface `#171E26`, raised `#1D2630`, border `#2C3845`, primary text `#EDF1F5`, muted text `#97A7B7`, accent `#83B1FA`. Test actual text/background contrast before acceptance; no assumption that these examples pass every pair.
- Prefer Windows-compatible system font stack (`Microsoft YaHei UI`, `Segoe UI` as appropriate), not font-file bundling. Logical baseline typography: 12–13 body; 11–12 secondary; 16–18 section heading; 22–24 page title. Preserve text scaling.
- Spacing cadence: 4, 8, 12, 16, 24 logical px. Control radii: 4–8; standard window may reach 10–12; do not derive window dimensions from visual theme.
- A single accent family; domain icons and champion/item assets may keep their real colors. No default glow, ornamental gradients, emoji buttons, glass overlay layering, repeated interchangeable cards or meaningless percentages.
- A consistent 16/20px icon family with text labels for essential actions; do not show anonymous glyphs as functional controls.
- Honor reduced motion and high contrast preferences. Use semantic colors for meaning, not category decoration. Provide keyboard focus and required assistive labels.

## Interaction state contract

Every nontrivial action needs: availability/preconditions, trigger, progress, success evidence, blocked/rejected result, timeout or uncertain result, cancellation or retry rule, and user-facing next step. Common surfaces should not call LCU endpoints directly.

| Action | Start | Complete | Failure / uncertainty |
|---|---|---|---|
| Read-only lookup | Cached local content immediately where valid, distinct refresh state | Source + refresh time / actual results | Offline, empty, outdated and error distinguished; limited retry |
| Toggle setting | UI shows pending write where necessary | Saved and reflected in the owner | Failure reverts visible state or marks it pending; never claim enabled before verified |
| Risky file repair | Preview objects/actions and consequences | Confirm + owner verification | Stop and show affected items; no silent fallback or unrelated cleanup |
| Runes / spells / items | Check phase, champion and supported action | Write via established owner; verify postcondition | Stale champion, rejected or ambiguous outcome shown; no blind repeated write |
| Leave ChampSelect | Explain dodge risk and expected preservation of original party | Only confirmed absence of ChampSelect and same original party | Unsupported route/unknown party -> unconfirmed, no invented success |
| App update | Manifest / version check, bounded download, signature/hash verification | New process/version actually observed | Safe rollback and clear instructions; no optimistic notification |
| Cloud sync | Local action remains available | Identified confirmed synced state | Durable pending retry or explicit failure, never discard local state |

Use nonmodal inline status for ordinary feedback, restrained notification for completion, and modal confirmation only for consequential actions. Clear distinction between closing a panel, hiding the entry, and exiting the process.

## Page specifications for first review

**Quick access:** approx. 400 x 270–320 logical px; a single connection line, 2x2 labeled action targets, settings/update footer. Use existing application state. Respect work-area boundaries when anchored to the floating ball, tray or cursor.

**Workbench:** target 1120 x 640–700 logical px, adapt within currently supported minimum bounds after actual WinForms measurement. Persistent 172–200px left navigation; 24px outer content margin at comfortable widths, 12–16px when constrained. Local page title and one clear primary action, not a second fixed context sidebar. Dense tables retain virtualization.

**Companion:** 320–340px logical width; context header, compact content, clear mode gating, local section disclosure, optional internal scroll only. Remain within working area at 100/125/150/200% DPI and multi-monitor negative coordinates; preserve show-without-activation. Dangerous quit action separate and guarded.

**My GGman:** differentiate identified accounts from verified match records, show missing values as unknown rather than zero, keep cloud ranking opt-in, protect portability and private data. This is a complete page, not a cramped four-row appendage inside Matches.

## Migration policy

- Establish shared tokens/interaction primitives before migrating pages; do not run a new system beside the existing theme system.
- Replace `DesktopLauncherEnhancer` reflection against `CompactMenuForm` private members with a typed integration contract as a scoped refactor, with equivalent open/close/entry semantics and smoke coverage.
- Remove duplicate toolbar/dock state from League Hub only after a route mapping and UI evidence review. Preserve all public features.
- Replace page-local fixed rectangles only where they demonstrably clip or fail DPI/content tests. Use native WinForms layout constraints rather than a wholesale framework rewrite.
- Keep old theme IDs and UI text keys readable for existing installs. Release changes in reviewable vertical slices with a rollback path.
- Preserve the product's .NET 4.8 single-EXE release footprint and existing performance budgets; no second Gameflow session or per-window network polling.

## Acceptance gates

1. Automated: existing host/League/UI text/smoke gates; new route-map invariants, navigation state, click/cancel safety, message/contrast token checks and geometry assertions.
2. Desktop manual: real GGman.exe at 100/125/150/200% DPI, 1366x768, 1920x1080 and mixed multi-monitor, dark/light/high contrast; keyboard Tab/Shift+Tab/Esc/Enter; Win10/Win11 where available.
3. Real League: offline, disconnected, Lobby, Queue, ReadyCheck, ChampSelect, InGame, EndOfGame; no extra Gameflow poller; verified LCU ownership/postconditions remain unchanged.
4. UX task test: an unfamiliar user opens the app, finds Match History and a guide, and understands whether an automation toggle is active. Track task success, errors and time; do not present design estimates as measured outcomes.
5. Release: visual before/after evidence plus executable tests, signed public asset verification, updater compatibility, data preservation and a safe path back to the last stable release.

## Delivery slices

- **R0** source audit, design spec and interactive *non-product* review artifact. No runtime code or production release.
- **R1** canonical control/typography/focus/input state contracts and test harness.
- **R2** first-run, floating entry, quick access and central routing.
- **R3** workbench/navigation plus grouped Automation and Tools.
- **R4** Matches/Guides/Companion screens and real League verification.
- **R5** My GGman / cloud-state presentation / Settings / updates / pets, followed by whole-product visual regression.
- **R6** user acceptance, performance proof and staged formal release.

Design references (patterns, not runtime dependencies):
- https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview
- https://learn.microsoft.com/en-us/windows/powertoys/general

The interactive HTML review is a standalone design artifact, not GGman code and not a verified live Windows screenshot. Production conversion requires approval of screenshots and real-client behaviors.
