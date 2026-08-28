# FACM 4.0 P7 Functional Parity Closeout

Status: active stacked closeout
Production baseline: FACM 3.5.15
Stacked base: `feat/facm4-function-parity-p6-settings-maintenance@d3801a0fa4276e74514a59a6c673c4cc4efbaff8`
Tracking: #233

## Purpose

P7 is the final **functional-equivalence closeout** before one unified real-machine candidate is handed to the user.

P7 is not:

- a UI 2.0 / visual redesign;
- permission to merge stacked P2-P7 branches;
- a production pointer or release-channel change;
- Gate 13 cutover approval;
- legacy retirement approval.

The rule is: **code-side parity first, one unified candidate second, visual redesign later.**

## Status vocabulary

- `CODE-GREEN` — replacement behavior exists and deterministic source/build/smoke coverage is green on the stacked line.
- `P7-AUDIT` — behavior exists but P7 must still prove entry-point/migration/lifecycle completeness.
- `REAL-MACHINE` — cannot be honestly closed by CI; requires supported-Windows or release evidence.
- `GATE13` — release/cutover evidence only; never implied by P7 success.

## Unified parity matrix

| 3.5.15 behavior area | FACM 4 owner | Current state at P7 start | P7 closeout requirement |
| --- | --- | --- | --- |
| Launcher-first startup, floating F, compact launcher | `FACM.App` + `FACM.Core.Desktop` + `FACM.Platform.Windows` | CODE-GREEN | Re-run entry/lifecycle source checks; no giant-shell click regression |
| Floating placement persistence / recovery-safe restore | Settings2 + desktop platform | CODE-GREEN | Verify Settings2 migration and Recovery/LKG do not overwrite damaged primary state |
| Cleanup preview / confirm / elevation / safe delete | Core + Platform.Windows + WinUI | CODE-GREEN | Verify repair entry has no placeholder path and UAC cancel/ancestor-reparse gates remain wired |
| Driver cleanup + native League repair actions | Core + Platform.Windows + App | CODE-GREEN | Verify all user-visible repair actions remain reachable through WinUI |
| Themes / flying pets / VPet / reset F / reset positions | Core + PetHost + App | CODE-GREEN | Verify all catalog IDs migrate and PetHost has one lifecycle owner |
| League Dashboard / Player / Live | Core + Infrastructure + shared League gateway | CODE-GREEN | Verify every visible workbench entry uses the single shared runtime |
| OP.GG advisor / ItemSet / recommended runes & spells | Core + Infrastructure + App | CODE-GREEN | Preserve read/apply separation, explicit confirmation and pre-write context revalidation |
| Matchmaking / ReadyCheck / PostGame / Presence | Core services + shared gameflow heartbeat | CODE-GREEN | Prove no second polling/session owner and Settings2 toggles survive migration |
| Efficiency / global hotkeys | Core + Platform.Windows | CODE-GREEN | Preserve `RegisterHotKey`, conflict/rollback behavior and migrated hotkey values |
| Bench quick-pick / swap | Core + shared League gateway | CODE-GREEN | Preserve explicit click, one POST max, bounded readback verification |
| ARAM / Mayhem data, decisions, build, localization, balance | Core + Infrastructure | CODE-GREEN | Re-run all typed public-data / cache / timeout / offline fixture gates |
| Mayhem WinUI query/cancel/save/copy | App + Core contracts | CODE-GREEN | Verify no user-visible placeholder and no GDI/WinForms dependency leaks back into Core |
| Auto update / manual check / announcements | Core + Infrastructure + App | CODE-GREEN | Verify auto/manual distinction and force-update lock state |
| Update download / receipt / signer / UAC helper / rollback | Infrastructure + Platform.Windows + `FACM.Updater` | CODE-GREEN | Keep updater security gate and both Foundation + Windows updater smokes executing |
| Open diagnostic log | Platform.Windows + App | CODE-GREEN | Controlled current log path only |
| Single-instance Ensure Open | Platform.Windows + App lifecycle | CODE-GREEN | Secondary launch bounded signal; no toggle-close/takeover/kill |
| `settings.ini` -> Settings2 | Core + Infrastructure | P7-AUDIT | Compare against production 3.5.15 key set, preserve legacy file byte-for-byte, prove no silent reset |
| `ui-text.ini` stable TextKeys | Core + Infrastructure + App | P7-AUDIT | Audit newly migrated WinUI user-facing strings and stable fallback behavior |
| Main navigation: Repair / League / Personalization / Settings | App | P7-AUDIT | No `TODO`, `Coming soon`, `placeholder`, `暂未实现`, `开发测试` or dead navigation surfaces |
| Cross-module shutdown ownership | App + platform runtimes | P7-AUDIT | F close, UAC handoff, PetHost, League runtime, hotkeys, updater and diagnostics dispose cleanly |

## Settings migration audit

P7 must derive the legacy key set from the **production 3.5.15 implementation**, not from memory or a hand-written test-only list.

At minimum the migration matrix must account for the current 3.5.15 user state represented by:

- floating position;
- game path;
- update preference and last announcement;
- theme and pet selection/enabled state;
- League recommended setup toggle;
- League efficiency hotkeys;
- post-game automation toggles;
- matchmaking / ready-check automation toggles.

Requirements:

1. existing `settings.ini` remains readable;
2. migration creates Settings2 only after validation;
3. the legacy file is not rewritten as a side effect of migration;
4. second load uses Settings2 without re-running migration;
5. invalid/corrupt/newer Settings2 is not silently replaced by migration;
6. Recovery/LKG reads do not overwrite the primary document until an explicit user action requires a save.

## Placeholder / dead-entry audit

P7 source gates must inspect the WinUI shell and feature surfaces for user-visible development placeholders. A string match alone is not sufficient to delete legitimate comments/tests; the audit should focus on user-facing XAML/code paths.

Four primary navigation entries must resolve to real functionality:

1. 清理与修复
2. LOL 工作台
3. 个性化
4. 更多设置

No P7 change should start a visual redesign just to remove a placeholder.

## Lifecycle audit

P7 must retain these ownership rules:

- one floating F owner;
- one compact launcher owner;
- one MainWindow instance;
- one League discovery/session owner and one gameflow loop;
- one PetHost runtime owner;
- one global-hotkey runtime owner;
- updater helper is a replacement-process boundary, not a second resident FACM runtime;
- successful UAC handoff may close the original process only after the elevated/replacement process is actually started;
- UAC cancellation leaves the current process alive.

## CI acceptance for the unified candidate

Before a candidate is handed to the user, the P7 head must have all of the following on the same commit:

- P1-P7 source gates green;
- Release x64 build green;
- deterministic FoundationSmoke green;
- deterministic WindowsSmoke green;
- controlled PetHost payload/self-test green;
- controlled updater payload/security smokes green;
- x64 self-contained single-file publish green;
- publish-output verification green;
- artifact upload green.

## Evidence that remains outside code-side parity

The following must stay visibly separate from P7 code completion and remain `REAL-MACHINE` / `GATE13` until actual evidence exists:

- Windows 10 1809 real-machine support;
- Windows 11 real-machine support;
- non-admin / real UAC-cancel behavior;
- real mixed-DPI / multi-monitor behavior;
- Defender / SmartScreen observations;
- real 3.5.15 -> 4.0 settings migration on a user machine;
- interrupted replacement / rollback on a real executable;
- final signed-package / release identity evidence;
- production pointer / cutover / legacy retirement.

P7 going green must never rewrite those evidence states to Passed.

## Completion definition

P7 is code-side complete only when:

1. settings migration is derived and tested against the production 3.5.15 key set;
2. no unexplained user-visible placeholder or dead primary navigation entry remains;
3. lifecycle ownership has deterministic/source coverage across the stacked feature line;
4. the unified parity matrix has no unexplained functional gap;
5. one same-head Foundation run passes the complete build/smoke/publish chain.

Only then should the user receive one unified FACM 4.0 candidate for real-machine functional validation.
