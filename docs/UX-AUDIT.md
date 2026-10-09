# GGman UX Architecture Audit — source evidence and review queue

Date: 2026-10-09. Scope: `main` after GGman 3.5.49 publication. **Source-only audit**: inspected repository implementation, navigation, theme, page layout, update flow and existing contracts. The candidate interactive HTML design was rendered and exercised independently; it is **not** a screenshot or test of the distributed GGman.exe. User/customer complaints indicate dissatisfaction but do not prove each hypothesized cause. Before changing production behavior, capture real app screenshots and run usability testing.

## Source inventory and verified facts

- `src/FACM/MainForm.cs`: 56px floating entry with tray, drag and contextual menu; main process entry is not a conventional permanently visible app window.
- `src/FACM/CompactMenuForm.cs` + `src/FACM/DesktopLauncherEnhancer.cs`: launcher enhancement hides historical compact content and overlays a new tile surface. The enhancer uses reflection against private `_theme`, `_ownerBall`, `_settings`, `_cleanup` fields and `OpenPersonalizationMenu` / `OpenMoreMenu` methods. Constants include 420x680 base geometry, compact height 236 and contextual compact height 322.
- `src/FACM/League/LeagueHubForm.cs`: preferred client size 1120x640, minimum 900x580; a primary sidebar, top sub-navigation (42 logical px) and optional contextual right dock compose the same frame.
- `src/FACM/League/LeagueHubNavigation.cs`: nine current workbench views across three top-level sections; strong code-level route IDs already exist and should be mapped, not removed.
- `src/FACM/Theming/ThemeCatalog.cs`: default `glass-blue` plus multiple visually divergent styles, some with large raw radius and multiple accent colors. `FacmDesignSystem` already caps normal card/control radii and provides more restrained semantic tokens.
- `src/FACM/Theming/FacmWindowChrome.cs`: global borderless shell with custom title, close, minimize, maximize, resize and drag behavior; requires Windows focus and DPI regression.
- `src/FACM/League/LeagueDashboardForm.cs`: fixed-position rectangles inside a 650x430 page, with `MinimumSize` equal to nominal client size.
- `src/FACM/League/LeaguePersonalStatsForm.cs`: nominal 720x560/min 650x540, fixed-position history and preferences areas; four visible recent rows.
- `src/FACM/Online/OnlineCenterForm.cs`: 560x620 fixed-position content and user-facing fallback error dialog still titled `FACM` near line 276.
- `src/FACM/League/LeagueRuntimeCompanionForm.cs`: large (~2774 line) WinForms surface with explicit pixel geometry, manual tooltips and local pages. Existing narrow-window, non-activation and League ownership rules are valuable and must survive.
- `src/FACM/League/LeagueHubUiText.cs`: current `ContextHint` default is a design rationale sentence rather than an actionable end-user instruction.
- `docs/UI-TEXT-CONTRACT.md`, `docs/PERFORMANCE-CONTRACT.md`, `docs/RUNTIME-COMPANION-DESIGN.md`: existing durable constraints. Do not replace these with a new subsystem.

## Findings (severity indicates prioritized **engineering/UX review**, not field-proven bug)

| ID | Priority | Finding | Evidence | Smallest safe next step |
|---|---|---|---|---|
| UX-01 | P0 | New UI installed over legacy launcher via private reflection introduces an unstable presentation boundary | `DesktopLauncherEnhancer.cs` | Define typed launcher interface + parity smoke before deleting old overlay |
| UX-02 | P0 | Competing persistent workbench navigation surfaces reduce available page width | `LeagueHubForm.cs`, `LeagueHubNavigation.cs` | User-test a single-sidebar variant, preserve stable page IDs |
| UX-03 | P0 | No canonical full-program interaction state specification across reads/writes | Page-local handlers and custom controls | Establish pending/success/rejected/uncertain states; test critical operations |
| UX-04 | P1 | Design sources and theme presets allow visual drift despite shared semantic primitives | `ThemeCatalog.cs`, `FacmDesignSystem.cs` | Choose approved standard theme; preserve legacy theme IDs as compatibility |
| UX-05 | P1 | Screen-specific absolute coordinates create DPI/localization clipping risk | `LeagueDashboardForm.cs`, `LeaguePersonalStatsForm.cs`, `OnlineCenterForm.cs` | Measure actual 100/125/150/200% geometry; target only failing pages |
| UX-06 | P1 | Player history belongs under Matches navigation and is visually compressed | `LeagueHubNavigation.cs`, `LeaguePersonalStatsForm.cs` | Move visible route to My GGman without altering storage |
| UX-07 | P1 | Legacy public product copy remains in an update failure path | `OnlineCenterForm.cs` | Fix after UI text-key contract review and smoke tests |
| UX-08 | P1 | Contextual helper copy includes implementation/design commentary | `LeagueHubUiText.cs` | Replace with actual user action or remove; preserve UI key |
| UX-09 | P1 | Transient companion is large and tightly coupled to layout | `LeagueRuntimeCompanionForm.cs` | Extract only testable presentation/layout boundaries; preserve game owner |
| UX-10 | P1 | First-use orientation needs field validation | Floating-first `MainForm.cs` | Observe novice open-and-navigate session; decide on one-time welcome |
| UX-11 | P2 | Existing textual customization doc includes old brand screenshots/strings | `docs/UI-CUSTOMIZATION.md` | Reconcile after actual new text assets exist |
| UX-12 | P2 | Existing tests prove several geometry/string contracts, not whether users finish tasks | UI and League smoke scripts | Add a lightweight task-test rubric and Windows screenshot checklist |

## Proposed user journeys (to validate)

1. **New recipient**: launch EXE -> identify current state -> open Workbench -> find one useful action -> understand whether result succeeded.
2. **Queueing player**: launch -> open flyout -> see queue state -> toggle controlled automation -> return to game without accidental process exit.
3. **ChampSelect player**: companion appears without stealing focus -> sees actual current champion and mode -> views applicable recommendations -> applies via existing confirmed owner or explicitly leaves champion select without party deletion.
4. **Returning player**: launch offline -> browse existing local history -> see whether cloud data is pending or unavailable -> optional safe backup/restore.
5. **Repair/updates**: inspect planned changes -> explicitly confirm -> observe source-authoritative settled result; failure never masquerades as success.

## Visual acceptance checklist (requires real client evidence)

- Capture the entry, launcher, hub, per-page panels, updater, dialogs, companion, pet/theme picker and tray context menu.
- Test at 1366x768, 1920x1080, 2560x1440; 100,125,150,200% DPI including mixed multi-monitor/negative coordinates.
- Check keyboard Tab/Shift+Tab/Enter/Esc, visible focus, hover/pressed/disabled, high contrast, dark/light, system text size, safe resize/minimize/Alt+Tab.
- Check disconnected/loading/no-data/stale/permission-rejected/network-failure and completed states.
- Confirm process exit vs flyout dismissal semantics on every entry route.
- Benchmark cold/warm launch, input delay, CPU/RAM and League InGame/ChampSelect overhead against the unchanged 3.5.49 baseline.
- Use real Windows build + League-client validation before a production update. Browser prototype checks cannot replace these.

## Prototype review outcome

A standalone HTML mock demonstrates quick access, main workbench, and Champion Select companion under a single token proposal. Browser smoke exercised route switches, toggle states, a guarded exit confirmation and no JavaScript page errors. This is a **design exploration**, not production functionality, and uses synthetic League/user data only. It must not be bundled into the product automatically.

Next steps:
1. Request real GGman screenshot bundle (or run a real Windows UI capture if available); identify fidelity discrepancies between source and live rendering.
2. Review design with the user against current 3.5.49 before selecting a palette/layout.
3. Implement a small shared-control/launcher pilot on one branch, instrument tests, and evaluate before full workbench migration.

References:
- `docs/GGMAN-PRODUCT-DESIGN.md`
- https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview
- https://learn.microsoft.com/en-us/windows/powertoys/general
