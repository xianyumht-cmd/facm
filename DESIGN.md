# GGman Precision Workbench Design System

> **2026-10-21 direction update:** The owner approved a standalone HTML-based classic iOS + Liquid Glass design as the next-generation visual reference. The former Precision Workbench geometry and anti-glass restrictions below describe the **existing production baseline**, not requirements for the new UI. New design integration must preserve the approved HTML/CSS composition and all nine League route capabilities, and pass independent native rendering/performance tests. Draft PR #355 is an experimental renderer candidate only.


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

## B2 source implementation record (native visual validation pending)

## B5 continuation and B6 acceptance boundary (2026-10-17)

Completed source-only continuation on PR #351:
- `CleanupRepairForm` surfaces directory/help, repair and maintenance state using legible 9–9.5pt copy, with the same owner callbacks and SafeCleanupService operations.
- `GgmanCaptchaForm` uses wider image/input/status regions and shared primary/secondary native buttons. Captcha fetching, verification and token ownership are unchanged.
- `GgmanStatsPrivacyForm` groups the two opt-ins in one native card while keeping actual consent state, ranking visibility writes and import/save permissions unchanged.
- `LeagueProfileCustomizationForm` has four distinct task surfaces for champions/skins, regalia, challenge tokens and emotes, with wider fields, legible hints, common action buttons, and unchanged service/lifetime/selection guards.
- `PetPickerForm` and `AnimalPetPickerForm` adopt semantic text, surface, list selection and action tones; image, download, PetHost, animation and selection/activation owners remain intact.
- `LeagueItemSetForm` and `LeagueBuildApplyForm` replace dark-only presentation with standard semantic colors and native actions, preserving their existing read/write confirmation semantics.
- `LeaguePersonalStatsForm` aligns summary/history typography with the 9pt minimum while retaining its current scroll, CloudBase editor and account layout.

**Build regression recorded:** `LeagueBuildApplyForm`'s first button-factory replacement accidentally omitted three existing auto-apply callbacks; Windows Build #39018862920 failed with four CS0103 errors. Follow-up commit `a3ae722de1226e7f67fdcf672a45cdb87e9f492d` restored the exact original handlers and Windows Build #39019063500 passed. Never infer service correctness from the intermediate failed state.

**Still incomplete/unknown:** Native visual before/after evidence for any new surface, all ten themes, keyboard/IME and 100/125/150/200% DPI checks, startup/idle memory, real installer/CloudBase/game tests, companion HUD and legacy assistant visual assessment, remaining accessible dialogs and the whole-product image-led acceptance ledger. Source/CI cannot replace native experience validation. Do not declare full visual completion, merge, publish or assert Astra approval.

## B5 source migration — cleanup review and maintenance progress dialogs

Three active GGman-owned secondary surfaces remain native and use the existing design tokens:
- `CleanupReviewForm`: replaces a separate hardcoded dark palette with semantic Canvas/Surface/Text/Warning/Error roles across the preview summary and grid. Existing path validation, blocked entries, native Cancel semantics and explicit destructive Execute confirmation are preserved; the Execute action uses the visually distinct Danger button tone. The custom rounded region now disposes any replaced Region correctly.
- `BackgroundOperationDialog`: consistent canvas, title and status text with the rest of the application; no change to background task execution, no premature close.
- `GameLocatorSearchDialog`: consistent theme and accessible native cancel button; task cancellation token, progress and result propagation remain unchanged.

**Current B5 is not exhaustive.** `CleanupRepairForm`, profile customization, secondary diagnostics and other reachable GGman-owned surfaces still require a final native visual consistency and input sweep. B5 source success does not constitute full product acceptance; operator screenshots, Win10 keyboard/DPI, actual cleanup safety controls and screenshot-based independent review remain pending.

## B4 native launcher, theme, account, update and ESC presentation (in progress)

- Floating launcher: existing four route IDs, context/welcome flow, menu owner and launcher dimensions are unchanged; each shortcut now paints a distinct semantic surface/border even at rest, with accessible keyboard focus. Footer actions use `FacmActionButton`. Context details use readable 8.5–9.5pt fonts.
- Theme picker: native Cancel/Apply buttons use shared tone/focus/Tab semantics, theme swatches retain their true per-theme preview, and card text/description uses contrast-aware paint with correctly disposed fonts. No theme ID, theme selection or persistence semantics changed.
- Online update/announcement dialog: reading width expanded from 560 to 640 logical units, version and announcement cards use wider text regions with clearer headings. Existing mandatory-update exit behavior, download installation, announcement links and preference handlers remain untouched.
- Email account: slightly expanded two-state authentication card and text field spacing, larger session identity/description, and readable privacy copy. No authentication endpoint/session or credential logic changed.
- ESC/lobby settings: the two **visible** local actions now sit inside a semantic surface with consistent typography and room for the full warning/status copy. Existing cloud buttons and probe remain **hidden** as before. The local and CloudBase action handlers, permissions, paths and persistence are untouched.

No new runtime dependency, polling timer, event-driven network call, or new app-owned route is introduced. Native rendering, DPI, accessibility, visual differentiation and actual update/login/game workflow acceptance remain **INCONCLUSIVE** pending consolidated Windows evidence. Additional settings/profile and secondary surfaces have not been declared complete. Do not merge or publish PR #351.

## B3 source implementation record — Mayhem, Live, player and recommendation (native visual acceptance pending)

- `MayhemLookupForm` groups its search controls and status into one semantic toolbar surface while presenting the preview in a separate full-width scrollable reading surface. `MayhemLookupLayoutPolicy` reserves horizontal padding plus the vertical scrollbar for the preview; geometry smoke checks multiple widths. Image query, rendering, copy, save, cancellation and owner lifetime remain unchanged. Status tones retain their semantic role during theme changes.
- `LeagueLiveForm` and `LeagueBuildAdvisorForm` no longer hardcode a private dark background, pale cyan text and manually filled generic action buttons. They use `FacmDesignSystem` roles and `FacmActionButton` while retaining original polling/service ownership, player identity and data-specific highlighting.
- `LeaguePlayerForm` adds an account summary surface/accent and standardizes typography and list backgrounds without changing cached profile, virtual rows or data fetch.
- `LeagueRecommendationForm` replaces the white-on-light selection text risk with paint-background-aware readable text, uses semantic action buttons and improves Chinese choice/preview text sizes; exact auto-apply logic and user selections are unchanged.

**Evidence boundary:** All above are source-level changes on PR #351. Existing screenshots do not show this code. No native WinForms images, focus/DPI, interaction, live League client data, or performance acceptance is asserted for these surfaces. The unmodified companion and additional active pages remain in scope under B3/B4/B5. Do not claim product-wide completion.


On the existing task branch, `LeaguePresenceForm` now reparents its existing settings into full-width scrollable task surfaces with status choices, signature, ranked identity and appearance controls. Width-dependent three/two-column state controls and structured field groups replace its 430px-wide, left-clustered fixed presentation. Native service calls, status options, account semantics and personal customization entry points are unchanged. `LeagueEfficiencyForm` now exposes the existing contextual hints inline in settings rows instead of relying entirely on hover text, with increased typographic rhythm and unchanged toggle/numeric/keyboard handlers. `LeagueSoftGlassSkin` no longer compresses these intentionally laid-out migrated pages.

Build compilation/contract gates passed for the Presence-only commit `635fd52cd8716510791e149e8efb369e447c5613` and combined B2 code commit `916d007f82030dc561d4e843a8760c41cbcfb5f4`; neither automated pass demonstrates actual layout geometry, light/dark palette quality, IME, keyboard/focus, scroll behavior or authentic cloud/session operation. Record these B2 surfaces as **implemented in source, native visual acceptance INCONCLUSIVE**, not completed.

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
