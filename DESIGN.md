# GGman Precision Workbench Design System

Status: **Product-wide visual modernization in progress; B1 remains partial; full native evidence pending (2026-10-17)**. This document records the proposed product-specific visual contract, not a claim that all existing views comply or have passed native acceptance.

Design authority: GGman UI/UX Modernization Blueprint (17 October 2026), authored from source review and the published guidance of [UI UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill), [Impeccable](https://github.com/pbakaus/impeccable), and [Awesome DESIGN.md](https://github.com/VoltAgent/awesome-design-md). These sources inform principles, not WinForms binaries or runtime dependencies. Root AGENTS.md and actual source/runtime behavior take precedence.

## Direction: Precision Workbench

- A compact, consistent **native Windows utility**, not a website, game launcher redesign, or animated glass dashboard.
- Preserve floating-ball/tray entry, the existing **nine route IDs**, a single grouped left rail, existing League/CloudBase service owners, editor draft safeguards, all saved theme IDs, and desktop accessibility semantics.
- Readability, predictable navigation, category-specific status and restrained emphasis take precedence over decoration.
- One primary action per task group when appropriate; distinguish waiting, offline, connected, rejected and unknown using words, not color alone. Never invent live status.

## Semantic visual contract

Keep ThemeCatalog and FacmThemeRuntime as palette owners. FacmDesignSystem owns shared color/geometry roles. Do **not** add a second theme runtime. New appearance code must use semantic roles; legacy exact-color remapping remains a compatibility mechanism for unmigrated pages.

| Role | Dark reference | Light reference | Requirement |
|---|---|---|---|
| Canvas | #111820 | #F3F5F7 | Window/page background |
| Surface | #1B2530 | #FFFFFF | Task groups, not nested decorative cards |
| SurfaceRaised | #25323F | #E9EEF4 | Inputs / subordinate surface |
| Text | #F1F5F9 | #17212B | Essential foreground |
| TextMuted | #B8C5D2 | #4A5968 | Supporting information |
| BorderSoft | #3B4A59 | #CCD4DD | Decorative separation |
| BorderControl | #8193A5 | #637487 | Meaningful input/focus boundaries |
| Accent | #83B4FF | #245FC5 | Selection, focus and action emphasis |

These are reference values, **not** an instruction to replace users' existing ten palettes in B1. Use actual final painted colors for contrast assertions: enabled normal text at least 4.5:1, meaning-bearing focus/boundary at least 3:1. Check hover/pressed/disabled outcomes individually; preserve high-contrast SystemColors behavior.

### Typography, spacing, geometry

- Page title 15pt bold; section heading 11pt bold; row/card heading 10pt bold; primary body/navigation/buttons 9.5pt; supporting text and table minimum 9pt for the migrated pilot; meaningful status/data emphasis 13pt bold.
- Prefer existing Microsoft YaHei UI/CJK-capable system fonts. No downloaded font. Font sizes use point units; geometry below uses 96-DPI logical units. Scale once and measure wrapped text using the actual font.
- Spacing scale: **4, 8, 12, 16, 24, 32**. Page padding 24, compact 16; section content padding 16; related gap 8; action heights at least 32; nav row at least 36 with 4 gap; section rows at least 36, growing for measured text.
- Control radius 4, section 6, outer chrome no larger than existing 12; respect themes that intentionally use square controls. No ornamental nested cards.
- Keep Hub preferred 1120×640 and current grouped rail width policy **194 / 172**. Existing 900×580 minimum is not proof of 200% DPI work-area fit; verify and change only after native evidence.
- Dashboard reading region is **at most 880 logical units and left-aligned**, not a centered narrow island. Two status columns when **inner usable width >=600**, otherwise vertical stacking. Detail labels become stacked above values below **560**. Content scrolls vertically if required. Do not shrink fonts to fit.
- Provide a visible focus state, measured Chinese text, keyboard-operable native controls and robust empty/error states. Respect IME, Tab, Shift+Tab, Enter, Escape, selection and scroll ownership.
- No pilot animation, new timer, polling service, remote dependency or graphical asset.

## Native repair-page visual language (B1 follow-up)

An actual operator screenshot of the previous game-repair page showed oversized solid-blue grouping, small 7.8pt action descriptions and actions clustered on the left with unused right-hand space. The Repair route is the first visibly differentiated content prototype within the **same** B1 task branch. This does not extend the pilot to the updater, launcher, Mayhem or other routes.

- Two task groups with quiet, bounded shared surfaces; group headings have an accent indicator and are distinct from individual actions.
- Each action occupies a full-width **description left / native action right** row; below a constrained width, its action moves underneath rather than clipping text. No 7.xpt copy and no fake KPI cards.
- The currently present five business actions and service owners are unchanged. A one-click exit-game action receives a Danger tone without changing its execution behavior. The automatic-window-repair button retains its on/off caption and normal click semantics.
- One compact status surface displays the actual result and semantic success/warning/error text, with no fabricated progress or status. No new runtime dependency, animation or polling.
- Evaluate actual native snapshots at the same client size against the user's initial Repair-page image before extending to any other screen. Screenshot aesthetics and task readability are independently assessed; CI is a necessary but insufficient gate.

## Product-wide rollout scope and completion ledger

The owner rejected the first pilot as visually indistinguishable and requests modernization of all **currently reachable GGman-owned surfaces** before one official release. Progress in batches internally on the same PR #351 branch; no per-page owner approvals. An unchanged-looking reachable surface is not completed merely because shared tokens or CI changed.

| Batch | Owned surfaces | Proof of completion |
|---|---|---|
| B1 | Hub rail, Dashboard, native Game Repair | Clear hierarchy and meaningful compared screenshots |
| B2 | Quick Tools / automation, online Presence | No tiny copy or left-clustered controls, keyboard reachable |
| B3 | Mayhem imagery, player/matches, Live, guide/recommendation, champion-select companion | Legible information, no unintended nested/horizontal scroll |
| B4 | Floating entry/launcher, updates/announcements, account/profile, themes/settings/ESC/cloud feedback | Coherent application chrome, forms and status states |
| B5 | Cleanup, repair review, other active auxiliary dialogs and global 10-theme audit | All currently reachable GGman-owned surfaces complete |
| B6 | One pinned final build, native visual/keyboard/DPI/performance acceptance, update/rollback preparation | Complete ledger with image evidence and passing functional gates |

**OS-owned exception:** system tray context menus, Windows file dialogs and system-owned UAC prompts retain their native appearance/accessibility unless there is a separately verified usability defect. Document intentional exclusions rather than silently skipping screens.

For every included surface maintain its activation path, source owner, baseline/candidate screenshots under identical theme/window/DPI/fixture, contrast, focus/keyboard, resize/DPI, function parity, performance concern and exact reviewed SHA. Missing native image evidence is **INCONCLUSIVE**. Never perform destructive repair/League/cloud operations just to capture a visual.

### Requested final handoff order

The owner requested all visual batches finished, one consolidated formal update after mandatory CI/native functional release checks, then a **single independent Astra visual audit at the end**. That reviewer is not a substitute for pre-release QA; post-release findings can affect users. Record residual risk, prepare a fast hotfix/rollback path, and do not falsely assert that the final independent design review occurred before publication. A pre-release independent design review remains the safer recommendation if the owner changes the order.

## Batch B1 scope and evidence

Only the existing workbench shell, grouped navigation rail and Dashboard are pilot surfaces. The current code reflows its existing status/details and Refresh/Close, while preserving actual asynchronous owners and refresh cadence. All other forms and themes stay under previous ownership.

Before independent professional sign-off collect *native* Windows baseline/candidate screenshots and measured execution traces. Required comparisons: dark/light, representative vivid theme, all 10 theme IDs at 100%, 100/125/150/200% with realistic work-area constraints, both 1120×640 and 900×580 scenarios where physically feasible, long Chinese text, disconnected/stale/pending states, focus, hover, selected, enabled/disabled, owned dialogs. Capture matching SHA/theme/DPI/scenario pairs **without resizing PNGs**. Exercise Alt+Tab and all nine routes, including a pending editor draft.

Engineering checks and the Windows CI are required but **not** a substitute for an independent image-based critique or a live DPI/keyboard session. Compare EXE size, startup and idle memory, navigation/theme latencies and stable handles against a recorded B0 baseline. Do not invent missing measurements.

For B1, independent reviewer scores each observed dimension (hierarchy, density, typography, alignment, spacing, components, navigation, color, accessibility and product cohesion) 0–4, with screenshot/behavior references. Every observed dimension must score **>=3** for a professional visual pass; none of the mandatory engineering checks may be red. Unobserved cases are **INCONCLUSIVE**, not PASS. The product owner verifies workflow fit, not professional visual quality.

The exact native evidence procedure and decision states live in docs/OPERATIONS.md; no B1 release or update-manifest edit is authorized.
