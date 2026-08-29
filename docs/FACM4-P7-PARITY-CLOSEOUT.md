# FACM 4.0 P7 Functional Parity Closeout

Status: **AUTOMATED-STABILITY-GREEN / unified real-machine candidate ready**
Production baseline: FACM 3.5.15
Stacked base: `feat/facm4-function-parity-p6-settings-maintenance@d3801a0fa4276e74514a59a6c673c4cc4efbaff8`
Tracking: #233 / PR #234
Verified code head: `f3906b84dd0076411dcd8a4fd82610d1d6c2a179`
Verified Foundation: **#628 / run `33230830272` = SUCCESS**

## Purpose

P7 is the final **functional-equivalence and automated-stability closeout** before one unified real-machine candidate is used for full Windows validation.

P7 is not:

- UI 2.0 / visual redesign;
- permission to merge stacked P2-P7 branches;
- a production pointer or release-channel change;
- Gate13 cutover approval;
- legacy retirement approval.

The rule remains: **functional parity + automated stability first, one unified real-machine validation second, visual redesign later.**

## Code-side parity result

P7 has met its code-side parity definition on the stacked line:

- production FACM 3.5.15 legacy settings contract is frozen from the real 15-key `AppSettings.BuildLines()` set/order;
- legacy `settings.ini` migration does not rewrite the legacy file;
- corrupt/newer Settings2 and atomic-write failure remain fail-safe;
- Repair / League / Personalization / Settings primary entries all resolve to real WinUI functionality;
- no user-visible `TODO`, `Coming soon`, `placeholder`, `暂未实现` or `开发测试` remains on primary surfaces;
- Cleanup retains preview/confirmation/elevation boundaries;
- launcher-first / F / compact launcher / MainWindow ownership remains intact;
- League retains one discovery/session owner, one shared gateway and one gameflow heartbeat;
- PetHost, global hotkeys, maintenance, single-instance and updater helper keep explicit lifecycle ownership.

## Stability-audit findings closed after initial parity

The older `3956/#595` candidate was not treated as final. P7 continued with a fault-oriented audit and closed these concrete issues:

### 1. WinUI theme resource ownership

Real Win10 startup captured `E_ACCESSDENIED` while runtime code attempted to set the color of a WinUI platform-owned `SolidColorBrush`.

Current rule:

- platform/system brushes are read-only inputs;
- FACM semantic brushes are app-owned mutable objects;
- runtime theme application only mutates FACM-owned brushes;
- personalization startup remains fail-soft.

Subsequent Win10 evidence no longer showed the old brush startup failure and recovery state reached `Running`.

### 2. Personalization stale Busy / permanently disabled controls

The Personalization surface manually synchronized `IsEnabled` while desktop-pet initialization was asynchronous. If the surface synced during `IsBusy=true`, there was no completion refresh and controls could remain disabled forever.

Fix:

- desktop-pet initialization completion now dispatches a UI-owner surface refresh;
- actual Win10 follow-up persisted `mono-emerald`, `greenfly`, pet enabled=true and F coordinates, proving user intents reached Settings2 after the fix.

This is narrow evidence; it does not by itself prove every theme visual or PetHost visible-runtime behavior.

### 3. Cross-feature Settings2 lost updates

Several feature writers previously followed whole-document load/mutate/save patterns. Parallel feature actions could overwrite unrelated changes.

Fix:

- `IAtomicSettings2Repository` / narrow `UpdateAsync` transaction boundary;
- Recovering repository serializes latest-load -> narrow mutation -> validate -> save -> LKG;
- normal feature mutations cannot rebuild recovery primary unless explicitly authorized by the maintenance intent;
- Cleanup, Personalization, F coordinates, League settings and other feature writers use the atomic boundary.

Regression stress: 40 iterations of concurrent Theme + F position + League setting updates with read-back after every iteration.

### 4. Maintenance lifecycle and retry

Closed defects:

- failed initialization no longer permanently marks the presenter initialized;
- More Settings can retry initialization in the same app session;
- active download owns/disposes its linked CTS; shutdown cancels but does not dispose underneath an active await;
- installer disposal is deferred until active download/replacement operations leave their finally blocks;
- Maintenance async-void handlers have final exception containment;
- install confirmation returns to a revalidated current state before replacement intent continues.

### 5. League cancellation / Window and ContentDialog teardown

Closed defects:

- Refresh / Advisor / ItemSet / automation settings distinguish linked caller/lifetime cancellation from provider failure;
- normal cancellation no longer fabricates `refresh-failed`, `advisor-refresh-failed`, `prepare-failed` or `apply-failed` states;
- fire-and-forget MainWindow refresh is contained during Window dispose;
- ContentDialog close/XamlRoot teardown is contained;
- explicit `ContentDialogResult.Primary` remains mandatory before recommendation/item-set writes.

A real compiler regression found during this audit (`CS0157`, leaving a finally clause) was caught by the full Release build and fixed before the final stability head.

### 6. Updater interrupted-replacement primitive

The previous fallback/rollback paths could stream-copy bytes directly over the live FACM executable. A helper termination during that copy could leave a partial EXE.

Current contract:

- `File.Replace` remains the preferred replacement path;
- fallback first prepares a complete `.facm-old` while live destination is unchanged;
- validated candidate staging moves over destination using same-directory `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)`;
- rollback moves the complete backup over destination using the same atomic primitive;
- no fallback/rollback path may use `File.Copy(staging|backup, liveDestination, overwrite:true)`;
- the built `FACM.Updater.exe --self-test` executes in Foundation and verifies backup-before-swap, complete candidate swap, complete rollback, and fallback backup integrity.

This materially narrows the interruption risk but does **not** convert Gate13 `update.interrupted-replacement-rollback` to Passed; real Windows controlled-termination evidence is still required.

## Repeated-operation stability matrix

| Area | Repetition | Required invariant | #628 result |
| --- | ---: | --- | --- |
| Settings2 cross-feature mutation | 40 rounds | no unrelated-field lost update after concurrent narrow mutations | PASS |
| Single-instance lifecycle | 24 rounds | one primary, one activation callback, mutex released after dispose | PASS |
| Updater UAC cancellation | 24 rounds | Win32 1223 returns false; existing app stays usable; no success launch path | PASS |
| PetHost bundle/cache | 24 repeated prepares | same SHA/path; no repeated embedded-bundle reopen/rehash | PASS |
| League Recommended cycle | 24 cycles | at-most-once write per stable ChampSelect fingerprint/cycle | PASS |
| League Efficiency hotkey transaction | 30 updates | registration/runtime/persisted settings stay in lockstep | PASS |

These are deterministic service/runtime tests, not substitutes for real UI/input/DPI/accessibility evidence.

## Final parity matrix

| 3.5.15 behavior area | FACM 4 owner | P7 automated state | Remaining evidence |
| --- | --- | --- | --- |
| Launcher-first startup, floating F, compact launcher | App + Core Desktop + Platform.Windows | GREEN | full supported-Windows real-machine behavior |
| Floating placement persistence / recovery-safe restore | Settings2 + Desktop platform | GREEN + 40-round settings stress | real mixed-DPI / multi-monitor |
| Cleanup preview / confirm / elevation / safe delete | Core + Platform.Windows + WinUI | GREEN | real UAC cancel and separately authorized delete |
| Driver cleanup + native League repair actions | Core + Platform.Windows + App | GREEN | real-machine operation |
| Themes / flying pets / VPet / reset F / reset positions | Core + PetHost + App | GREEN + narrow Win10 evidence | full visual/PetHost real-machine behavior |
| League Dashboard / Player / Live | Core + Infrastructure + shared gateway | GREEN | real League client behavior |
| OP.GG advisor / ItemSet / recommended runes & spells | Core + Infrastructure + App | GREEN + repeated transaction smoke | real League write-path validation |
| Matchmaking / ReadyCheck / PostGame / Presence | Core services + shared gameflow | GREEN | real League lifecycle |
| Efficiency / global hotkeys | Core + Platform.Windows | GREEN + 30-round transaction smoke | real hotkey conflicts/focus behavior |
| Bench quick-pick / swap | Core + shared gateway | GREEN | real ChampSelect behavior |
| ARAM / Mayhem data, decisions, build, localization, balance | Core + Infrastructure | GREEN | live public-data/LCU behavior |
| Mayhem WinUI query/cancel/save/copy | App + Core | GREEN | real save/clipboard behavior |
| Auto update / manual check / announcements | Core + Infrastructure + App | GREEN | real network/UI behavior |
| Update download / receipt / signer / UAC helper / rollback | Infrastructure + Platform.Windows + FACM.Updater | GREEN + atomic helper self-test | real interrupted replacement + final package identity |
| Open diagnostic log | Platform.Windows + App | GREEN | real Shell open behavior |
| Single-instance Ensure Open | Platform.Windows + App lifecycle | GREEN + 24-round Windows smoke | real second-launch behavior |
| `settings.ini` -> Settings2 | Core + Infrastructure | GREEN | real 3.5.15 user-machine migration |
| `ui-text.ini` stable TextKeys | Core + Infrastructure + App | GREEN | real-machine visual review |
| Main navigation: Repair / League / Personalization / Settings | App | GREEN | real-machine interaction |
| Cross-module shutdown ownership | App + platform runtimes | GREEN | real process/UAC/updater shutdown behavior |

## Same-head CI acceptance

FACM 4.0 Foundation **#628 / run `33230830272` = SUCCESS** on code head `f3906b84dd0076411dcd8a4fd82610d1d6c2a179`.

The same run passed:

- controlled PetHost payload + self-test;
- controlled Updater payload + built-helper atomic self-test;
- all P1-P7 source/product gates;
- PowerShell 5.1 real-machine collector self-test;
- Release x64 restore/build;
- deterministic FoundationSmoke including Settings/League repeated stress;
- deterministic WindowsSmoke including single-instance/UAC/PetHost repeated stress;
- WinUI x64 self-contained single-file publish;
- publish-output verification;
- artifact upload.

Unified candidate:

```text
artifact: facm4-x64
artifact id: 9708452498
artifact ZIP bytes: 165,704,298
GitHub artifact digest: sha256:dcc5b93ae48508d73ce44e90f4f6600047090acddfef876e0a6d38cee0d92888
code head: f3906b84dd0076411dcd8a4fd82610d1d6c2a179
```

No independent EXE hash is claimed here unless it is recomputed from the downloaded artifact.

## Evidence that remains outside automated parity/stability

P7 automated success does **not** change canonical Gate13 readiness. The following remain real-machine/release evidence:

- non-admin / real UAC-cancel behavior;
- Defender / SmartScreen observations;
- Windows 10 1809 real-machine support;
- Windows 10 22H2 complete real-machine support;
- controlled real-user Windows 11 support;
- real mixed-DPI / multi-monitor behavior;
- real accessibility behavior;
- real 3.5.15 -> 4.0 Settings2 migration;
- interrupted updater replacement / rollback under controlled real termination;
- final signed-package / release identity evidence;
- production pointer / cutover / legacy retirement authorization.

Canonical matrix remains:

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

## Unified real-machine validation boundary

The next P7 action is one unified functional validation on artifact `9708452498`.

Start non-destructively:

1. cold start: launcher-first F appears without giant-Shell regression;
2. F drag / persistence / compact open-close / detailed shell;
3. all four primary entries work;
4. Cleanup preview/review/cancel; real UAC cancel keeps original instance alive;
5. Personalization theme visual, F restore/reset, greenfly/VPet visible runtime;
6. League Dashboard / Player / Live / Mayhem and explicitly bounded workbench actions on a real client;
7. Settings auto-update toggle, manual check, announcement, diagnostics/log entry;
8. second launch signals existing instance instead of creating a second resident runtime;
9. normal exit disposes PetHost / League / hotkeys / maintenance cleanly;
10. collect Settings2 / recovery state / JSONL evidence.

Do not include real LOL deletion, updater replacement/kill, production pointer changes, release publication or legacy retirement unless separately authorized.

## Completion boundary

**Code-side functional parity and automated-stability closeout are complete on `f3906b84...`.**

PR #234 stays Draft and stacked P2-P7 stay unmerged until the unified real-machine functional validation is reviewed. UI 2.0 starts only after functional-equivalence validation. Gate13 cutover remains a separate evidence/authorization process.
