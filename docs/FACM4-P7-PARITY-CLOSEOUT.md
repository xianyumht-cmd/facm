# FACM 4.0 P7 Functional Parity Closeout

Status: **CODE-GREEN / unified real-machine candidate ready**
Production baseline: FACM 3.5.15
Stacked base: `feat/facm4-function-parity-p6-settings-maintenance@d3801a0fa4276e74514a59a6c673c4cc4efbaff8`
Tracking: #233 / PR #234
Verified head: `3956e1414e22cf8bf24fd654ab66a795e52d7723`

## Purpose

P7 is the final **functional-equivalence code closeout** before one unified real-machine candidate is handed to the user.

P7 is not:

- a UI 2.0 / visual redesign;
- permission to merge stacked P2-P7 branches;
- a production pointer or release-channel change;
- Gate 13 cutover approval;
- legacy retirement approval.

The rule remains: **code-side parity first, one unified candidate second, visual redesign later.**

## Final code-side result

P7 has met its code-side completion definition on the stacked line.

Verified on head `3956e1414e22cf8bf24fd654ab66a795e52d7723`:

- production FACM 3.5.15 legacy settings contract is frozen from the real `AppSettings.BuildLines()` 15-key set and order;
- legacy `settings.ini` migration is validated without rewriting the legacy file;
- corrupt/newer Settings2 and atomic-write failure remain fail-safe;
- Repair / League / Personalization / Settings primary entries all resolve to real WinUI functionality;
- no user-visible `TODO`, `Coming soon`, `placeholder`, `暂未实现` or `开发测试` remains on primary surfaces;
- cleanup still requires explicit confirmation and blocked-target presentation matches the Core contract;
- launcher-first / F / compact launcher / MainWindow ownership remains intact;
- League keeps exactly one discovery/session owner, one shared gateway and one gameflow loop;
- PetHost, global hotkeys, maintenance, single-instance and updater boundaries retain explicit lifecycle ownership;
- the MainWindow closeout no longer calls obsolete League / Diagnostics APIs and does not incorrectly own League runtime disposal.

## Final parity matrix

| 3.5.15 behavior area | FACM 4 owner | P7 code state | Remaining evidence |
| --- | --- | --- | --- |
| Launcher-first startup, floating F, compact launcher | App + Core Desktop + Platform.Windows | CODE-GREEN | supported-Windows real-machine behavior |
| Floating placement persistence / recovery-safe restore | Settings2 + desktop platform | CODE-GREEN | real mixed-DPI / multi-monitor |
| Cleanup preview / confirm / elevation / safe delete | Core + Platform.Windows + WinUI | CODE-GREEN | real UAC cancel and controlled real delete |
| Driver cleanup + native League repair actions | Core + Platform.Windows + App | CODE-GREEN | real-machine operation |
| Themes / flying pets / VPet / reset F / reset positions | Core + PetHost + App | CODE-GREEN | real desktop/PetHost behavior |
| League Dashboard / Player / Live | Core + Infrastructure + shared League gateway | CODE-GREEN | real League client behavior |
| OP.GG advisor / ItemSet / recommended runes & spells | Core + Infrastructure + App | CODE-GREEN | real League write-path validation |
| Matchmaking / ReadyCheck / PostGame / Presence | Core services + shared gameflow heartbeat | CODE-GREEN | real League lifecycle |
| Efficiency / global hotkeys | Core + Platform.Windows | CODE-GREEN | real hotkey conflicts/focus behavior |
| Bench quick-pick / swap | Core + shared League gateway | CODE-GREEN | real ChampSelect behavior |
| ARAM / Mayhem data, decisions, build, localization, balance | Core + Infrastructure | CODE-GREEN | live public-data/LCU behavior |
| Mayhem WinUI query/cancel/save/copy | App + Core contracts | CODE-GREEN | real save/clipboard behavior |
| Auto update / manual check / announcements | Core + Infrastructure + App | CODE-GREEN | real network/UI behavior |
| Update download / receipt / signer / UAC helper / rollback | Infrastructure + Platform.Windows + FACM.Updater | CODE-GREEN | interrupted replacement / rollback + final package identity |
| Open diagnostic log | Platform.Windows + App | CODE-GREEN | real Shell open behavior |
| Single-instance Ensure Open | Platform.Windows + App lifecycle | CODE-GREEN | real second-launch behavior |
| `settings.ini` -> Settings2 | Core + Infrastructure | CODE-GREEN | real 3.5.15 -> 4.0 user-machine migration |
| `ui-text.ini` stable TextKeys | Core + Infrastructure + App | CODE-GREEN | real-machine visual review |
| Main navigation: Repair / League / Personalization / Settings | App | CODE-GREEN | real-machine interaction |
| Cross-module shutdown ownership | App + platform runtimes | CODE-GREEN | real process/UAC/updater shutdown behavior |

## Same-head CI acceptance

FACM 4.0 Foundation **#595 / run `33194723681` = SUCCESS** on head `3956e1414e22cf8bf24fd654ab66a795e52d7723`.

The same run passed:

- P1-P7 source gates;
- controlled PetHost payload + self-test;
- controlled updater payload + security contract;
- Release x64 build;
- deterministic FoundationSmoke;
- deterministic WindowsSmoke;
- WinUI x64 self-contained single-file publish;
- publish-output verification;
- artifact upload.

Unified candidate:

```text
artifact: facm4-x64
artifact id: 9695331632
artifact ZIP bytes: 165,696,693
artifact ZIP sha256: 12ac16496ff76918d1aa05167ebb30250005d429a274d44422ef46a96d255524
FACM.App.exe bytes: 305,879,700
FACM.App.exe sha256: d2ebddbf109c3525668c11a12598bef85f7aba79126eb3b25c08b168856e3c40
```

The ZIP digest above matches the GitHub Actions artifact digest. The EXE hash was independently recomputed from the downloaded artifact before handoff.

## Evidence that remains outside code-side parity

P7 success does **not** change Gate 13 readiness. These remain REAL-MACHINE / GATE13 evidence until actual evidence exists:

- non-admin / real UAC-cancel behavior;
- Defender / SmartScreen observations;
- Windows 10 1809 real-machine support;
- Windows 10 22H2 real-machine support;
- controlled real-user Windows 11 support;
- real mixed-DPI / multi-monitor behavior;
- real accessibility behavior;
- real 3.5.15 -> 4.0 Settings2 migration;
- interrupted updater replacement / rollback;
- final signed-package / release identity evidence;
- production pointer / cutover / legacy retirement.

The canonical release evidence matrix therefore remains **RELEASE BLOCKED**, and Gate 13 remains **CUTOVER BLOCKED**.

## Unified real-machine validation boundary

The next P7 action is one unified functional validation on the candidate above. Start with non-destructive checks:

1. cold start: launcher-first F appears without a giant Shell regression;
2. F drag / position persistence / compact launcher open-close / detailed Shell navigation;
3. all four primary entries open real functionality;
4. Cleanup preview/review/cancel and UAC-cancel keep the current instance alive;
5. Personalization theme / F reset / pet selection and controlled PetHost startup;
6. League Dashboard / Player / Live and user-driven workbench actions on a real client;
7. Settings maintenance: auto-update toggle, manual check, announcement, diagnostics/log entry;
8. second launch signals the existing instance instead of creating another resident runtime;
9. normal exit disposes PetHost / League / hotkeys / maintenance ownership cleanly.

Do not make real LOL deletion, updater replacement, production pointer changes, release publication or legacy retirement part of the first validation pass.

## Completion boundary

**Code-side P7 closeout is complete.**

PR #234 stays Draft and stacked P2-P7 stay unmerged until the unified real-machine functional validation is reviewed. UI 2.0 starts only after that functional-equivalence validation, and Gate 13 cutover remains a separate evidence/authorization process.
