# GGman independent source-level UI/UX audit

Date: 2026-10-16. Status: audit complete; recommendations **not implemented or approved for release**.

## 1. Executive assessment / 中文摘要

**结论：GGman 已有可继续使用的产品骨架，但还不能认定为视觉与交互一致、经过完整验收的成熟桌面产品。建议选择 B：在现有 WinForms 架构内统一设计系统，先修高影响问题，再逐页收敛。无需推倒重写。**

当前正式版为 **3.5.77**。本报告检查的 main 提交是 `14806c2ef593c6cdd8dec357d8ecce04913109ab`，其应用源码与正式发布标签一致。没有运行 Windows 客户端，没有把源码推断冒充截图验收。

最值得先处理的五件事：

1. **工作台失去焦点就关闭，编辑器又缺少草稿离开保护。** 用户切去复制文字、查看资料或换页面，可能丢掉未保存编辑。这里的问题是交互规则，不是配色。
2. **部分主题存在可计算的文字对比度缺陷。** 推荐页标题硬编码白色，浅色主题背景也是白色；多个主题的主按钮固定白字，亮色强调背景不足以支撑小字阅读。
3. **重要确认界面的键盘操作不完整。** 清理预览的取消、执行按钮都退出 Tab 顺序；选人助手的部分窗口操作也不参与 Tab 导航。
4. **视觉统一只完成了一部分。** 共享主题、页面自己的颜色、运行时颜色替换和自动压缩布局同时存在，导致相同功能在不同页面上的字号、间距和状态表现不稳定。
5. **云同步的规则变好了，但结果仍不够清楚。** 软件/文字同步与游戏自动恢复各有开关，状态却主要汇总成一句话；用户难以直接确认哪一类在何时恢复成功。相关操作还位于个人统计下方。

这解释了“说不出哪里不对”的一部分来源：页面的视觉密度不统一，重要操作的层次与位置不稳定，切换页面/窗口的行为也不够可预测。以上结论不等于所有页面都难看，更不等于所有假设都已在真实机器复现。

Positive foundations worth retaining: floating-entry-first access, a single grouped workbench rail, stable route IDs, native Button/CheckBox-derived shared controls, explicit data-unavailable states, virtual match rows, bounded companion dimensions, and established League/cloud/update owners. The earlier audit's broad P0 labels are not carried forward. **No P0 is established by this review.**

## 2. Baseline, method and scope

| Item | Verified evidence |
|---|---|
| Canonical repository | https://github.com/xianyumht-cmd/facm |
| Audited main SHA | `14806c2ef593c6cdd8dec357d8ecce04913109ab` |
| Production release | [v3.5.77](https://github.com/xianyumht-cmd/facm/releases/tag/v3.5.77), non-draft, non-prerelease; published 2026-10-15 15:20:43 UTC |
| Release target / tag source | `01c41e2eaeb9f9b14182b0f587878668158ae379` |
| Documented release base | `a659d75765c058452bc510fb8d40e0a89d1e9576` in PROJECT_STATE |
| Online manifest | `online/version.json`: enabled, 3.5.77, minimum 3.0.0, non-forced |
| Published executable metadata | Both `GGman.exe` and compatibility `FACM.exe`: 2,650,520 bytes; API digest `d6f88ce3c78cce401420e9c78b5357351890362f007bf230fd928ad3a8875d84`, matching manifest |
| Source equivalence | `git diff v3.5.77 HEAD --stat` before this report: only `docs/PROJECT_STATE.md` and `online/version.json`; no application source difference |
| Architecture | `src/FACM/FACM.csproj:1–24`: WinForms, net48, GGman assembly; optional PetHost is not the normal embedded product |
| DPI declaration | `src/FACM/app.manifest:20`: PerMonitorV2; declaration is not proof of every custom painter's DPI correctness |
| Environment | Linux source inspection; no native Windows desktop, League session, screenshot, binary execution or user study |

Release metadata was checked through GitHub's latest-release API and the fetched tag, not assumed from a previous conversation. Executables were **not** downloaded or signature-verified in this audit; the digest statement is metadata corroboration, not fresh binary attestation.

Repository rules and knowledge consulted: root `AGENTS.md` (no nested AGENTS discovered), PROJECT_STATE, ARCHITECTURE, DECISIONS, PITFALLS, AI_WORKSTYLE, OPERATIONS, UX-AUDIT and GGMAN-PRODUCT-DESIGN. `docs/AI_CONTEXT.md` is absent. Several knowledge sections retain historical 3.5.61/67/74 policies; current service code and the newer 3.5.77 sections take precedence. This report does not silently promote a design proposal to an accepted product decision.

Method: enumerate Form/UserControl implementations; trace current factory/menu callers; inspect constructors, layout, painting, state and close handlers; compare legacy claims to current source; calculate opaque sRGB contrast; check proposed fixes against current ownership boundaries. Inventory coverage is broader than deep path coverage: data parsing, every tooltip, every game endpoint and every pet animation were not exhaustively reviewed.

All source references below are repository-relative and pinned conceptually to the audited SHA. Paths beginning `League/`, `Theming/`, `Services/`, `Online/`, `Mayhem/`, `Pets/` or `Application/` are relative to **`src/FACM/`**; other `.cs` names in findings have that same root. Example immutable reference: [LeagueHubForm.cs](https://github.com/xianyumht-cmd/facm/blob/14806c2ef593c6cdd8dec357d8ecce04913109ab/src/FACM/League/LeagueHubForm.cs).

## 3. Current UI inventory and ownership

| Surface / actual implementation | Current entry and dependencies | Visual ownership / status |
|---|---|---|
| `MainForm.cs`, `LayeredFloatingBall.cs` | ShellModule; floating entry, tray, drag, hide/recovery | Purpose-built shell; active |
| `CompactMenuForm.cs`, `DesktopLauncherEnhancer.cs` | MainForm opens menu; typed LauncherTheme/Settings/Owner access; contextual shared Gameflow state | Legacy controls hidden under active 2×2 launcher, shared tokens plus local owner drawing; active |
| `League/LeagueHubForm.cs`, `LeagueHubNavigation.cs` | LeagueHubModule factories, LeagueHubUiBridge; nine route IDs | Shared chrome + one grouped sidebar; active |
| `League/LeagueDashboardForm.cs` | Hub dashboard factory; phase/details services | Shared cards, fixed rectangles; active |
| `League/LeaguePlayerForm.cs` | Hub player route and player bridge; PlayerDataService | Responsive existing layout, virtual match ListView and champion summary; active |
| `League/LeagueLiveForm.cs` | Hub live route; shared live and bench services | Local legacy colors softened by shared skin; active; distinct from automatic companion |
| `League/LeagueRecommendationForm.cs` | Hub recommendation route; read/apply/item/auto owners | Responsive cards and footer, local header/controls plus tokens; active |
| `Mayhem/MayhemLookupForm.cs`, `MayhemCardRenderer.cs`, `MayhemLookupLayout.cs` | Hub mayhem route / launcher; existing Mayhem service | Responsive query shell containing a rendered bitmap result; active |
| `League/LeagueRuntimeCompanionForm.cs`, `LeagueRuntimeCompanionController.cs` | LeagueLiveModule creates; LeagueHubModule controls automatic episode presentation | 320 logical-pixel custom window, bounded height, disclosure, tooltips, guide/bench state; active |
| `League/LeagueEfficiencyForm.cs` and nested `LeagueHotkeyCaptureDialog` | Hub efficiency route / efficiency bridge; settings and automation module | TableLayoutPanel, switch rows, explicit hotkey save; active |
| `League/LeaguePersonalStatsForm.cs`, `GgmanStatsPrivacyForm.cs` | Hub profile route; personal statistics module, registered session | Scroll page combining account, statistics, sync and editors; owned privacy modal; active |
| `GgmanAccountForm.cs`, `GgmanCaptchaForm.cs` | My GGman account action; email auth/session owner | Shared colors and fixed-layout owned login/CAPTCHA modals; active when required |
| `League/GgmanUnifiedSyncPanel.cs` | Embedded in My GGman; process-lifetime GgmanAutoSyncService | Two policy controls, publish and exceptional backup action; active |
| `EscSettingsForm.cs` | Embedded in My GGman; local configuration backup/locator | Local backup/recovery and path controls; active; not an independent top-level cloud workflow |
| `League/UiTextEditorPanel.cs` | Embedded in My GGman; customization store and sync save notification | Expandable editor; old cloud upload/restore controls constructed but not exposed by current expansion list |
| `League/LeaguePresenceForm.cs`, `LeagueProfileCustomizationForm.cs` | Hub presence route -> customization dialog; dedicated League owners | Fixed internal composition; Hub presence wrapper supplies scrolling; active |
| `League/LeagueGameRepairForm.cs` | Hub repair route; game repair + efficiency services | Shared cards but fixed two-column action coordinates; active |
| `CleanupRepairForm.cs`, `CleanupReviewForm.cs` | Shell cleanup/tools path -> preview -> execute via cleanup owner | Shared repair shell; separate hard-coded preview palette/chrome; active |
| `Online/OnlineCenterForm.cs` | MainForm.OpenOnlineCenter path; OnlineService/UpdateInstaller | Shared badges/buttons, scrollable notes, fixed outer layout; active |
| `Theming/ThemePickerForm.cs`, `ThemeMenu.cs` | MainForm personalization; ThemeCatalog/ThemeRuntime | Live preview and custom theme cards; active |
| `Pets/AnimalPetPickerForm.cs`, `SpritePetWindow.cs` | MainForm.OpenPetSelector -> AnimalPetManager | Hard-coded dark picker, active sprite window; optional personalization |
| `Services/GameLocatorSearchDialog.cs`, `BackgroundOperationDialog.cs` | Locator and blocking worker call sites; diagnostic/repair workflows | Task dialogs, local styling; active helpers |
| `Services/DiagnosticsExportService.cs` | Existing tool/menu action | Export operation, native dialogs/messages rather than a separate diagnostics dashboard |
| `League/LeagueBuildAdvisorForm.cs`, `LeagueBuildApplyForm.cs`, `LeagueItemSetForm.cs` | Advisor module factories and compatibility UI bridges | Retained secondary paths; not three additional Hub navigation pages; preserve until route reachability is checked in runtime |
| `League/LeagueChampSelectAssistantForm.cs` | No production `new` call found; static validation called by smoke | Legacy compiled surface, not the current automatic companion |
| `Pets/PetPickerForm.cs`, `AnimalPetWindow.cs` | No current production constructor call found; manager constructs SpritePetWindow | Retained/legacy code; do not score their appearance as the active picker/window |
| `src/FACM.PetHost/` | Optional separate WPF runtime | Not the standard embedded 3.5 desktop UI; not evidence for current WinForms quality |

Cross-cutting owners: ThemeCatalog defines palette; FacmThemeRuntime switches it; FacmDesignSystem recursively styles; FacmControls supplies action/switch/status primitives; LeagueCompactDensity mutates geometry; FacmWindowChrome and FacmBorderlessOutsideClose manage top-level shell/closure. The interfaces share services, but sharing services does not establish sharing a complete visual contract.

### Historical findings reconciled

| 2026-10-09 audit claim | Current disposition |
|---|---|
| Launcher accesses private menu fields with reflection | Superseded for launcher: `DesktopLauncherEnhancer.cs:59–64` uses typed properties. Remaining tray bridges still reflect `_tray`; maintenance risk, not a new P0 visual defect. |
| Persistent primary/subnav/right-dock competition | Resolved in current Hub constructor, `League/LeagueHubForm.cs:154–193`; nine routes remain. |
| No first-run orientation | Current MainForm startup path and launcher welcome controls exist. Actual discoverability still untested, but absence is no longer a valid claim. |
| Player/recommendation pages inherently fixed and clipped | Responsive policies and narrow-footer fixes now exist; do not repeat the old defect without a failing current constraint. |
| Update release notes silently clipped / errors overwritten | Current dedicated scrollable notes and persistent failure handling address the earlier issue. Remaining reading area is small (F13), a lower-priority concern. |
| My GGman merely a four-row appendage | Separate profile route and scroll layout exist; now its combined responsibilities and ordering need attention (F09). |

Newly traced evidence includes default focus-loss closure combined with draft lifetime, specific color failures, exclusion of destructive-dialog buttons from Tab order, and theme/density ownership interactions.

## 4. Visual-design assessment

### Hierarchy and composition

The new single rail is a real improvement. Its 194px normal / 172px compact widths are approximately 17–19% of the nominal 1120/900 widths; those proportions are reasonable. Nine destinations need not become nine new redesign projects. Group labels and selected indicators support scanning.

Within pages, hierarchy is less coherent. Dashboard uses a small fixed canvas inside a much larger content area, whereas My GGman requires long vertical travel and repair retains two fixed action columns. Large page headings, small bold subheadings, tiny explanatory labels and card borders often compete for emphasis. The problem is inconsistent allocation of space to tasks, not simply “too many cards.”

### Typography

Most surfaces start at 9pt Microsoft YaHei UI, appropriate to investigate as a Chinese desktop baseline rather than reject automatically. But the actual system includes 7.4/7.5/7.6/7.8/8/8.2/8.4/8.5/8.6/8.8/9/9.2pt values and many bold variants. The companion puts substantive evidence at 7.4–7.8pt; repair descriptions use 7.8pt. These are around 10 device pixels at 96 DPI before glyph rasterization. Legibility is a strong concern, not a measured reading failure. Arial in the brutalist theme also implies Chinese fallback; no broken glyph claim is made.

### Color and identity

Default dark-blue tokens, restrained radii (window ≤12, card ≤8, control ≤6) and shared state tones provide an identifiable direction. The old “glass” class names are not proof of excessive glow: the shared panel now draws a flat surface and optional top accent rule. Conversely, ten available palettes are not proof of ten usable themes. Actual foreground/background combinations matter (F03).

Cleanup preview and pet picker retain separate palettes. Live/Recommendation include literal white labels; the recursive skin does not recolor ordinary labels. The result is a demonstrably incomplete theme contract, especially in light appearance. Theme switching also remaps exact colors, while other code has already blended those colors (F05).

### Components and states

Action buttons retain native keyboard semantics and paint focus/disabled states; navigation has a visible selection edge and focus outline. Toggle controls use native Checked state. These are strengths. Native input/list controls, custom switch geometry, page-local buttons, custom theme tiles and a mouse-oriented cleanup preview do not yet form a fully consistent system. Native MessageBox use is not itself a defect; it should remain where it gives reliable confirmation semantics.

## 5. Interaction and product experience

| Journey | Current assessment |
|---|---|
| New launch -> useful action | One-time welcome and four launcher shortcuts exist. Preserve skip/reopen and tray recovery. No novice task-time evidence exists. |
| Browse workbench -> consult another app | Default deactivate closure makes the workbench transient despite its long-form editing responsibilities (F01). |
| Edit UI text -> choose another item/page | Selection reloads saved entry; navigation disposes page; unsaved guard protects external restore, not leaving the editor (F02). |
| Switch machine / GGman account | 3.5.77 cloud-first behavior matches the stated requirement better than old conflict-choice UI. Missing cloud backup has an explicit creation action. Remaining issue is observability and ordering, not a request to revert sync policy (F10). |
| Configure automation | Grouped sections and native-derived switches are good. Hotkeys require Save while automation changes are immediate; make that local scope obvious, without adding a second global save. |
| Read guide during ChampSelect | Bounded compact window and ShowWithoutActivation are correct product constraints; tiny evidence text, hover-only detail and some non-Tab controls increase reading/access effort (F04/F12). |
| Clean files | Explicit preview and confirmation already exist. Keyboard access and visual integration need repair; no evidence establishes accidental deletion or unsafe cleanup paths (F04/F06). |
| Check/update | Separate verified update state, notes, progress and persistent errors exist. No updater trust-chain change is justified by this audit. |
| Offline / empty / stale | Player empty labels, companion missing-data feedback and update unknown/error distinctions exist. Do not invent “always shows zero” or “no errors handled” claims. Sync still needs category-specific outcomes. |

## 6. Design-system evaluation

The design system is **partially adopted**, not absent. It provides semantic colors, radii and several interaction primitives but lacks enforceable typography/spacing roles and complete window-lifetime semantics. Final presentation can be determined by:

1. a form constructor's raw colors, fonts and positions;
2. a generic compact-density pass that changes geometry;
3. recursive token styling, sometimes more than once;
4. page-specific resize/layout handlers;
5. theme-switch exact-color remapping;
6. a global chrome/close policy.

This makes a source-level “same token used” check insufficient: output depends on the path and sequence. Consolidation should make each migrated component own its layout and semantic colors. Do not create a second parallel theme engine. Remaining reflection-based tray bridges are an engineering migration constraint, not evidence that today's launcher still uses reflection or that users encounter a rendering failure.

## 7. Prioritized findings

Classification vocabulary: **Confirmed from source** = implementation directly establishes the fact; **Strong design concern** = code and principles support concern without visual validation; **Runtime verification required** = outcome depends on native behavior; **Subjective design option** = a preference, not an established defect. Confidence refers to the stated evidence, not to unperformed live tests. Complexity estimates are relative, not delivery promises.

### F01 — Persistent workbench inherits transient focus-loss dismissal

- **P1; Confirmed from source; high confidence in policy, runtime verification required for exact Windows event sequences.**
- Evidence: `Theming/FacmWindowChrome.cs:200–221,154–169,499–508`; `League/LeagueSoftGlassSkin.cs:19–29`; `Application/Modules/LeagueHubModule.cs:74–88`.
- Implementation: default `CloseOnDeactivate` and `CloseOnEscape` come from ControlBox, not task type. QueueOutsideClose closes when another process owns foreground, except minimized/no-focus edge cases. Hub uses default chrome options.
- Principle/impact: user control and continuity. A working window can disappear when the user goes to League, a browser or email. Combined with F02, this also discards draft work. Same-process owned dialogs are explicitly exempt from the foreground check, so this is not “every dialog click closes the Hub.”
- Correction: explicit transient versus persistent policy; reserve outside-click dismissal for launcher/popup surfaces. Keep Hub/editor state when focus moves away; define Esc according to active task.
- Complexity/risk: medium; window behavior, companion dismissal and update modal ownership. Verify Alt+Tab/external click, minimize, owned modal, game foreground, and explicit close separately; preserve floating entry and nonactivation.

### F02 — Unsaved text is protected from cloud restore but not local navigation

- **P1; Confirmed from source; high confidence.**
- Evidence: `League/UiTextEditorPanel.cs:77–84,96–100,166–180,215–285,290–358`; `League/LeagueHubForm.cs:356–358,407–434`; `Services/GgmanAutoSyncService.cs:529`.
- Implementation: edits update preview; `_draft` changes on Save. Selection/filter refresh reloads `_value` from saved entries. Re-expansion recaptures disk state. Hub navigation closes/disposes the child. HasPendingVisibleChanges gates remote restore but no corresponding local leave guard exists.
- Principle/impact: error prevention and recoverability; unsaved text can vanish on selection change, collapse/reopen, page navigation or window close. This is not a claim of saved-file corruption.
- Correction: explicit Save/Discard/Cancel before replacing a dirty edit, or preserve drafts in the existing editor owner until explicitly resolved. Do not silently publish drafts to cloud.
- Complexity/risk: medium; avoid turning canceled navigation into disposal, preserve formatting-token validation. Verify each exit path, canceled exit, search/filter, cloud restore deferral and switching GGman accounts.

### F03 — Theme foreground/background combinations fail readable contrast

- **P1; Confirmed from source; high confidence in color mathematics.**
- Evidence: `Theming/FacmControls.cs:79–130` (white primary-button text); `Theming/ThemeCatalog.cs:64–190` and remaining preset definitions; `League/LeagueRecommendationForm.cs:77–90,907–916`; `Theming/FacmDesignSystem.cs:164–244` (no ordinary-label recoloring).
- Implementation: enabled primary actions always use white on Accent. Brutalist secondary actions also use white on a very pale raised surface because IsLight is false. Recommendation title uses literal white on the shared Surface; cloud-light Surface is white, with only a bottom separator painted in the header.
- Calculations at normal enabled state, opaque sRGB, no screenshot sampling:

| Pair | Contrast |
|---|---:|
| Recommendation title white / cloud-light Surface white | **1.00:1** |
| Secondary button white / brutalist raised `(239,236,224)` | **1.18:1** |
| Primary white / obsidian accent `(204,150,54)` | **2.63:1** |
| Primary white / cloud-light accent `(78,126,255)` | **3.65:1** |
| Primary white / holo accent `(32,184,255)` | **2.24:1** |
| Primary white / aurora accent | **2.22:1** |

- Principle/impact: readable text; 4.5:1 normal-text benchmark from W3C, 3:1 for qualifying large text. The 1:1 title fails either benchmark. Native desktop benchmarking is not a WCAG certification claim.
- Correction: add/resolve semantic on-accent foreground and test all presets; replace literal text colors on semantic surfaces. Preserve stored theme IDs. Target margin above thresholds, including hover/pressed, not rounded borderline compliance.
- Complexity/risk: low–medium; affects many visible controls. Verify enabled/hover/pressed/focus text for all presets and capture light/dark before/after. Disabled controls are assessed separately, not counted as normal-text failures.

### F04 — Important controls are excluded from keyboard traversal

- **P1 for cleanup; P2 for companion chrome; Confirmed from source; high confidence.**
- Evidence: `CleanupReviewForm.cs:61–75,198–207,230–246`; `League/LeagueRuntimeCompanionForm.cs:2430–2445`.
- Implementation: cleanup Cancel/Execute both use a helper with `TabStop=false`; close is a clickable Label. Companion chrome-button helper also sets TabStop false. These controls are not normal Tab destinations. Cleanup receives global Esc dismissal through FacmBorderlessOutsideClose, so this report does **not** claim there is no keyboard escape whatsoever.
- Principle/impact: keyboard operability and predictable confirmation; keyboard-only users cannot traverse to cleanup actions in the expected way.
- Correction: native tab-focusable buttons, meaningful names, visible focus and deliberate cancel/default policy; no accidental Enter-to-delete default. Add an explicit way to engage companion controls without auto-stealing focus.
- Complexity/risk: low–medium; verify Tab/Shift+Tab/Space/Enter/Esc, focus order and no execution without explicit selection. Confirm native accessibility exposure separately.

### F05 — Theme changes do not have deterministic semantic ownership

- **P2; Confirmed from source for remapping gap; strong design concern for resulting mixed palette; high/medium confidence.**
- Evidence: `Theming/FacmDesignSystem.cs:210–243`; `Theming/FacmThemeRuntime.cs:58–67,153–181`; `Theming/ThemePickerForm.cs:147`; `League/LeagueHubForm.cs:367` and LeagueSoftGlassSkin apply path.
- Implementation: styling blends existing button/list/panel backgrounds toward tokens. Runtime Remap only recognizes exact previous palette values. A blended or page-local color is generally not an exact palette value; ordinary-label fonts are not comprehensively re-resolved either. Reapplying a blend is not idempotent.
- Principle/impact: consistency; an already-open surface can differ from a freshly opened surface under the same selected theme. Exact visible cases require Windows comparison.
- Correction: semantic role assignment at component ownership boundary; re-resolve the complete state on theme change. Avoid repeated blending as the contract.
- Complexity/risk: medium; do not lose match-result semantics or chrome special cases. Verify dark→light→dark on open and newly created forms with equivalent data.

### F06 — Active preview/pet/live surfaces retain competing visual systems

- **P2; Confirmed from source; high confidence.**
- Evidence: `CleanupReviewForm.cs:11–36,61–75,230–246`; `Pets/AnimalPetPickerForm.cs:32–100`; `MainForm.cs:267–281`; `CleanupRepairForm.cs:352–357`; `League/LeagueLiveForm.cs:45–66`.
- Implementation: active cleanup preview and animal picker use literal dark palettes and separate fonts/chrome. Live has literal light labels despite skinning. Shared recursive styling does not replace arbitrary label foregrounds. The cleanup title property also retains “FACM 清理预览”; since its title bar is custom this is not asserted to be a visible on-canvas title.
- Principle/impact: common product identity, status consistency and accessibility; switching tasks feels like changing tools and light appearance is unreliable.
- Correction: migrate these active surfaces to existing semantic components, preserving the file preview and pet behavior. Keep system dialogs native.
- Complexity/risk: medium; preview column/readability and pet preview lifecycle. Verify default/light themes, failure, selection and confirmation states; do not spend effort polishing unused PetPickerForm first.

### F07 — Global density transformation makes layout depend on call order

- **P2; Confirmed from source as engineering risk, not proof of clipping; high confidence.**
- Evidence: `League/LeagueCompactDensity.cs:15–25,112–164`; `League/LeagueSoftGlassSkin.cs:23–36`.
- Implementation: absolute table rows shrink by 0.82/0.90, columns by 0.92; some button widths/heights, padding and margins shrink independently; titles ≥16pt lose 1.5pt. The active Hub is exempt, but legacy business forms are not. The old CompactHub branch is not the current Hub layout.
- Principle/impact: coherent sizing and maintainability; constructor geometry is not final geometry. Fixed children can retain dimensions while containing rows shrink, complicating review and localization.
- Correction: for each migrated page, make its final layout explicit and stop applying generic compaction to that page. Do not globally remove density adjustment before checking unmigrated surfaces.
- Complexity/risk: medium–high in aggregate; migrate one surface at a time. Verify final post-skin control bounds, text fit and repeated application; keep current responsive footer tests.

### F08 — Dashboard does not use the workbench's available space adaptively

- **P2; Strong design concern; high confidence in geometry, medium in visual impact.**
- Evidence: `League/LeagueDashboardForm.cs:39–100,133–181`; `League/LeagueHubForm.cs:367–381`.
- Implementation: 594px overview/detail cards and fixed positions remain after embedding Dock.Fill in the larger host. Value labels ellipsize; the page has no width-driven content reflow. At nominal 1120px Hub width the body has roughly 900px before inner details, leaving substantial unused horizontal capacity.
- Principle/impact: proportion and information priority; long values truncate while adjacent space is unused. No actual native text truncation has been measured.
- Correction: bounded responsive card width, readable value column and purposeful maximum line length; one contextual next action if useful. Do not fill empty area with decorative metrics.
- Complexity/risk: low–medium; verify longest account/region/phase text at supported widths and DPI, connected/detected/disconnected states.

### F09 — Account page places configuration after statistics and becomes a long task stack

- **P2; Strong design concern; high confidence in layout, medium in usability judgment.**
- Evidence: `League/LeaguePersonalStatsForm.cs:113–186,280–301`; `League/GgmanUnifiedSyncPanel.cs:135–139`; `League/UiTextEditorPanel.cs:166–180`.
- Implementation: summary, four recent-account rows and ranking precede cloud center at y=454; then 291/328px cloud panel, 352px local ESC panel, then 48/492px editor. Normal collapsed page extent is about **1189 logical px**, expanded about **1633**, before native scaling and any later layout effects.
- Principle/impact: task hierarchy and recognition; users seeking portability must scan/scroll past retention statistics. Existing scroll support makes this accessible by scrolling, not “missing controls.”
- Correction: retain My GGman ownership, add compact local section navigation or reorder account + configuration first; keep statistics secondary and advanced text editing disclosed. This respects the repository decision to keep ESC in My GGman.
- Complexity/risk: medium; preserve control lifetime, privacy separation, scroll/focus position and draft guards. Verify find-and-complete tasks for cloud backup, game publish, local restore and editing.

### F10 — Cloud policy is explicit but per-category outcomes are not

- **P2; Confirmed from source for state projection; strong design concern for interpretation; high/medium confidence.**
- Evidence: `League/GgmanUnifiedSyncPanel.cs:43–108,134–163,211–224`; `Services/GgmanAutoSyncService.cs:198–216`; `Services/UiTextCatalog.cs:62–80`.
- Implementation: independent program-sync/game-auto-restore controls; one general StatusKey prioritizes program-sync-off before game restore state, while the service can still run if game auto-restore is on. Game status starts as explanatory text and is updated by manual publish; the panel does not expose separate last verified restore times for software/text/game categories.
- Principle/impact: visibility of system status; “off” for one policy and enabled game recovery can coexist without an obvious per-category outcome. This is not evidence that sync itself fails or that the new cloud-first rule is wrong.
- Correction: concise existing-page rows: category, direction/policy, current result, last verified time, bounded actionable failure. Distinguish enabled from completed and local file restored from game runtime applied.
- Complexity/risk: medium; project existing owner events, no new poller or database schema. Verify missing backup, signed out, program off/game on, partial ESC/YAML recovery, retry, draft deferral and successful restore.

### F11 — Repair two-column descriptions exceed compact host width

- **P2; Runtime verification required; high confidence in constraint, medium in visible clipping.**
- Evidence: `League/LeagueGameRepairForm.cs:63–110,128–173`; `League/LeagueHubForm.cs:13–15,70–79`.
- Implementation: second action panel starts at x=350 and has width 318, ending at 668 within the card. At a nominal 900px host, the 172px sidebar, outer/inner padding and repair root padding leave roughly that width or less before table-cell margins. The second description has no wrap-to-next-column layout. Generic density changes make exact final bounds runtime-dependent.
- Principle/impact: resize resilience and readable safety descriptions; the action can remain visible while its explanatory text loses space.
- Correction: width-based one/two-column policy for the existing cards; measure child bounds after skinning and DPI.
- Complexity/risk: medium; preserve action owner and no automatic clicks. Verify at minimum host width, 125/150/200%, long localized hints and with navigation scrollbar visible.

### F12 — Companion information density spends readability on secondary detail

- **P2; Strong design concern; high confidence in font/geometry, medium in reading impact.**
- Evidence: `League/LeagueRuntimeCompanionForm.cs:28–53,1450–1468,1667–1712,1927–1937,2065–2078`.
- Implementation: 320px window, 284px content, narrow labeled sections, 7.4–7.8pt evidence/metrics and hover tooltips; alternatives and augments add nested disclosure/pagination/scrolling.
- Principle/impact: glanceability during a time-limited task. Dense secondary metadata can be harder to read than the recommendation it supports, especially Chinese glyphs; no timed user study exists.
- Correction: keep champion/mode/current actionable recommendation first; use fewer typography roles and progressive disclosure for secondary evidence, with keyboard-accessible detail. Preserve bounded size, scroll cue and source completeness warnings.
- Complexity/risk: medium; rapid rerolls, sparse/unknown data and game focus. Verify long champion/item/augment names, ordinary ARAM versus Mayhem, blocked image/source, focus and monitor placement. Do not promise data availability through layout changes.

### F13 — Update notes are accessible but unnecessarily cramped

- **P3; Strong design concern; high confidence in dimensions, medium in priority.**
- Evidence: `Online/OnlineCenterForm.cs:48,92–113`.
- Implementation: release-notes TextBox is 486×44 with vertical scrolling inside a fixed 560×620 window. This fixes historical hidden notes, but presents only a small reading aperture for substantial release notes.
- Principle/impact: efficient reading; users scroll a small inner text area to understand what will change.
- Correction: expandable details area or modest resizable notes region; keep current state and error protection.
- Complexity/risk: low; verify long notes, keyboard selection/scroll and errors. No update-service changes.

### F14 — Mayhem lookup uses an export bitmap as its primary reading interface

- **P2; Confirmed from source for image-only result; strong design concern for accessibility; high confidence.**
- Evidence: `Mayhem/MayhemLookupForm.cs:116–125,243–277,308–313`; `Mayhem/MayhemCardRenderer.cs:15–16,131`.
- Implementation: result is rendered into a bitmap and displayed with Zoom, fitted to viewport width. AccessibleName is the window title; there is no corresponding structured result text in this result control. Copy copies an image.
- Principle/impact: text access and scalable reading; bitmap text cannot be selected or exposed as individual result fields, and narrowing the preview scales all text down together. A generic accessible name is not equivalent content.
- Correction: keep export/copy image as a feature; offer a lightweight text summary/details view from the existing result model and an actual-size viewing option. Do not re-fetch the guide or replace its data service.
- Complexity/risk: medium; keep factual parity and source/patch labels between text and image. Verify keyboard/readout access and small viewport readability with missing fields.

## 8. Evidence boundaries and calculations

Confirmed implementation facts: F01 closure defaults, F02 edit replacement/disposal paths, F03 colors and ratios, F04 Tab exclusions, F05 remapping/compaction operations, F06 active private palettes, F09 vertical ordering, F10 state projection, F14 bitmap presentation. Actual clipping, speed of recognition, fatigue, screen-reader narration and final perceived polish remain unmeasured.

Contrast calculation: normalize each 8-bit sRGB channel c to [0,1]; linearize as c/12.92 when c≤0.04045, otherwise ((c+0.055)/1.055)^2.4; luminance L=0.2126R+0.7152G+0.0722B; ratio=(Llighter+0.05)/(Ldarker+0.05). Values above are rounded only for display. They describe opaque fill/text colors, not anti-aliased edge pixels. The calculation was run directly over ThemeCatalog definitions. Normal primary white/accent ratios for all presets were approximately: glass-blue 4.50, obsidian 2.63, neon 3.48, cloud-light 3.65, brutalist 6.86, holo 2.24, emerald 2.23, RGB 2.24, aurora 2.22, sunset 3.85. Treat the near-threshold default as requiring unrounded/state verification, not a comfortable pass.

Source geometry is logical constructor/layout geometry. WinForms DPI, non-client dimensions, fonts, compaction and events alter physical results. Neither fixed coordinates alone nor PerMonitorV2 alone establishes a clipping defect or a pass. F11 intentionally remains runtime-dependent.

## 9. Professional quality scorecard

Scores are **expert heuristic judgments out of 5**, not measured usability results, screenshot ratings, compliance certifications or comparisons against a sampled market. A 3 means usable foundations with material inconsistencies; 5 would require coherent implementation plus native acceptance evidence. No numeric overall average is warranted.

| Dimension | Judgment | Basis |
|---|---:|---|
| Visual consistency | 2.5/5 | Shared shell/tokens coexist with active private palettes and literal text colors |
| Typography | 2.5/5 | Common base font but many micro sizes, substantial tiny evidence copy |
| Layout | 3/5 | Current responsive improvements coexist with fixed dashboard/repair and generic compression |
| Information architecture | 3/5 | Single rail is sound; account/configuration ordering and secondary utility paths remain uneven |
| Interaction design | 2.5/5 | Good guarded actions, weak persistent-window and dirty-editor semantics |
| Accessibility | 2/5 | Native-derived primitives help; contrast, Tab exclusions and bitmap content are material gaps |
| Design-system maturity | 2.5/5 | Semantic palette and controls exist; lifecycle/typography/spacing/adoption are incomplete |
| Overall product coherence | 3/5 | A recognizable utility with a viable architecture, not yet consistently polished |

## 10. Recommended design direction

**Compact, calm, legible League companion.** Keep the floating entry, on-demand workbench and narrow runtime companion. Preserve familiar blue as the default accent; reduce competition through hierarchy rather than adding gradients or animations. A neutral dark default is a **Subjective design option**, not an objectively required palette change.

Proposed practical contracts, subject to native measurement:

- Typography: Microsoft YaHei UI for Chinese; body 9.5–10pt, secondary 9pt, section 11–12pt semibold/bold, page title 15–17pt. Companion body at least a tested 9pt where feasible; disclose details rather than squeeze every field smaller. These are design starting points, not universal accessibility minimums. Use actual measured height instead of a single universal line-height constant.
- Spacing: 4/8/12/16/24 logical-pixel roles; 16–24 page inset, 12–16 card inset, 8 between related controls. Bind roles to layout owners, not post-hoc arbitrary scaling.
- Color: explicit Canvas/Surface/Raised/Text/Muted/OnAccent/Error/Warning/Success roles. Every preset must satisfy text/state contrast checks. Pair status color with text/icon; preserve semantic match results.
- Navigation: keep the single existing rail and route IDs. Separate local section navigation from top-level destinations. Make appearance/update/diagnostics discoverable through concise existing settings/tools entries; do not add a permanent third navigation column.
- Page layout: one page title, clear status and one primary action per task region; use WinForms TableLayoutPanel/FlowLayoutPanel or small existing pure layout policies. Constrain long reading lines, stretch meaningful data areas, and collapse fixed columns when necessary.
- Components: shared buttons/toggles/status, consistent focus/disabled/error behavior, native input accessibility, standard destructive confirmation; avoid renaming classes solely to erase historical names.
- Density: compact lists where scanning matters, sufficient room for editable text and errors. One nested scroll area only when its separate content responsibility warrants it; avoid compressing safety guidance.
- Companion: preserve 320–340 logical-pixel target width, bounded work-area fit, no automatic focus grab, explicit actions and single-owner updates. Prioritize current champion/mode/action; show provenance or alternatives on demand without extra network work.
- Window lifetime: flyout dismisses on outside click; workbench remains; editor prompts/preserves drafts; modal cancellation is predictable. “Close page,” “hide entry” and “exit process” stay distinct.

## 11. Strategy comparison

| Strategy | Benefit | Cost / compatibility | Maintenance / regression |
|---|---|---|---|
| A — targeted correction | Fast fixes to draft loss, contrast and keyboard blockers | Low–medium; limited footprint | Leaves palette/density ownership and page-to-page inconsistency; recurring patch work |
| B — design-system consolidation | Resolves high-impact defects, then consistent typography/layout/state through existing components | Medium, incremental; stable routes and services preserved | Best long-term fit if migrated per page with native evidence; medium visual regression risk |
| C — comprehensive presentation redesign | Can revisit every workflow and screen composition together | High even without changing WinForms; broad parity and acceptance burden | Highest route/lifetime/LCU-focus regression risk; unnecessary before current defects are corrected |

**Recommend B, starting with A-sized safety/readability fixes.** A alone does not resolve repeated ownership drift. C is not supported by evidence of a fundamentally unusable shell, and would spend too much effort recreating working flows. Choosing B does not authorize implementation in this task.

## 12. Future implementation sequence

1. Freeze this baseline and create a small native acceptance fixture set; collect representative existing screenshots as implementation evidence, not as a prerequisite to understanding this report.
2. Fix F01/F02 together: persistent workbench lifetime and explicit dirty-editor protection. Verify all leave paths before any visual rearrangement.
3. Fix F03/F04: semantic text colors/contrast, cleanup keyboard confirmation, deliberate companion access. Keep destructive actions explicit.
4. Establish typography/spacing/state roles in the existing design system; address F05, then migrate active cleanup/pet/live surfaces (F06). Retain theme IDs and user text keys.
5. Update My GGman's local ordering and category outcomes (F09/F10), without changing cloud-first selection, upload policy, database permissions or service cadence.
6. Migrate dashboard/repair layout (F07/F08/F11) and retire generic density effects only for migrated pages. Keep existing responsive Player/Recommendation improvements.
7. Refine companion legibility and Mayhem text access (F12/F14), then optional update-notes space (F13).
8. Whole-product native review, performance comparison and normal repository gates before a separately authorized release. No new framework, polling owner or always-open dashboard.

Each future slice should contain a small before/after evidence set, exact changed owner boundaries and acceptance results. Do not produce a large “modernized” mockup and treat it as completed application work.

## 13. Regression risks and acceptance criteria

| Area | Required acceptance for future changes |
|---|---|
| Entry and tray | Click versus drag, show/hide, tray recovery, first-run skip/reopen, startup without network; no new persistent dashboard |
| Window/draft lifetime | Alt+Tab does not discard work; dirty selection/page/close paths preserve or explicitly resolve drafts; owned modals work; Esc affects the intended surface |
| Navigation | All nine route IDs reachable; correct initial route; no extra Dashboard instantiation; current child ownership/cancellation remains correct |
| Color/typography | All ten presets checked for meaningful enabled text/state combinations, including light and hover/pressed/focus; Chinese and long custom UI copy remain readable |
| Layout/DPI | 100/125/150/200%, supported working-area sizes including 1366×768 and 1920×1080; mixed monitors and negative coordinates; measure final bounds after skin/resize; define fallback where logical minimum exceeds work area |
| Accessibility | Keyboard-only end-to-end tasks, visible focus, native accessible names/roles, high-contrast inspection; bitmap guide has equivalent useful text if F14 is implemented |
| Cloud | Signed out, missing remote, existing remote, explicit save, CAS rejection, draft deferral, software off/game on, partial game restore; no new raw identifiers or implicit game upload |
| League companion | Correct ordinary ARAM/Mayhem gating, rerolls, unavailable/stale data, explicit write confirmation/owner, no focus theft, one presentation per episode, pin/collapse/work-area fit |
| Cleanup/update | Preview scope and cancellation retained; no deletion without explicit confirmation; persistent update errors, long notes, cancellation and verification semantics unchanged |
| Performance | Existing performance budgets, one shared Gameflow owner, no extra normal-path requests; compare startup and companion overhead against baseline rather than assume visual refactoring is free |

Automated source/geometry/contrast tests can enforce contracts. They cannot certify perceived quality, actual screen-reader output or Riot-client behavior. Native screenshots verify composition; interactive checks verify focus, keyboard and task continuity; real League checks verify the external integration. These are complementary forms of evidence.

## 14. Limitations, references and delivery boundary

No native screenshots, Windows rendering, actual font fallback, mixed-monitor interactions, accessibility-tree inspection, live user task observations, League play, CloudBase account access or binary signer checks were performed. No customer data were used. Source evidence cannot establish how frequent a problem is among users. System high-contrast behavior remains unverified rather than assumed supported or broken. Current external data-source availability was outside scope.

Professional references consulted on 2026-10-16 (principles, not copied styles):

- [Microsoft Windows design guidelines](https://learn.microsoft.com/en-us/windows/apps/design/guidelines-overview): coherent navigation, layout, color, input and typography.
- [Microsoft typography guidance](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography): typography supports communication and hierarchy; WinUI sizing is not transplanted uncritically into WinForms.
- [Microsoft WinForms automatic scaling](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/automatic-scaling-in-windows-forms?view=netframeworkdesktop-4.8): font/DPI scaling behavior and layout constraints.
- [Microsoft WinForms high-DPI support](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms?view=netframeworkdesktop-4.8): framework support does not validate custom painting automatically.
- [W3C contrast minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html): quantitative readability benchmark used in F03.
- [W3C WCAG 2.2](https://www.w3.org/TR/WCAG22/): keyboard and visible-focus principles, applied here as design evaluation criteria rather than a formal desktop certification.
- [W3C non-text contrast](https://www.w3.org/WAI/WCAG21/Understanding/non-text-contrast.html): meaningful state/focus cues need sufficient contrast.

GitHub stars and coding-agent conventions were not used as a design-quality score. No irrelevant style copying, generated screenshot, synthetic user study or claimed native test result underlies this assessment.

**Delivery scope:** this report only, on one documentation task branch and an unmerged documentation PR. Application code, themes, tests, dependencies, assets, release requests and production manifests are unchanged. Documentation validation checks path/line references, numerical calculations and diff scope; no application build/test claim is made for this audit. Stop here; implementation requires a separate task.
