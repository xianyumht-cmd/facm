# FACM 4.0 P7 Functional Parity Closeout

Status: **FUNCTIONAL-PARITY-GREEN / LOCAL-CANDIDATE-2730-FOUNDATION-EQUIVALENT-GREEN / HOSTED-RUNNER-PENDING**
Production baseline: FACM 3.5.15
Stacked base: `feat/facm4-function-parity-p6-settings-maintenance@d3801a0fa4276e74514a59a6c673c4cc4efbaff8`
Tracking: #233 / PR #234
Latest code fix: `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83`
Verified Foundation: **#632 / run `33233590075` = SUCCESS**
Current targeted artifact: `9709261625`

Current cloud staging candidate: `2730eda15dc28a801871b5a3d10b4eecbd03a656` (parent formal P7 `9744af848e4b888c1876e76e2cbf0c06d5c526bf`)

## Purpose and boundary

P7 is the final functional-equivalence and stability closeout before full real-Windows validation. It is not UI 2.0, permission to merge stacked P2-P7, a production pointer/release change, Gate13 cutover approval, or legacy retirement approval.

Production remains FACM 3.5.15.

## Functional parity result

The stacked P7 line retains the code-side 3.5.15 parity contract:

- production legacy Settings contract uses the real ordered 15-key `AppSettings.BuildLines()` baseline;
- legacy `settings.ini` migration does not rewrite the source legacy file;
- corrupt/newer Settings2 and atomic-write failures remain fail-safe;
- Repair / League / Personalization / Settings primary entries resolve to real WinUI functionality;
- primary surfaces contain no user-visible development placeholders;
- Cleanup retains preview/confirmation/elevation boundaries;
- launcher-first F / compact launcher / MainWindow ownership remains one product lifecycle;
- League retains one discovery/session owner, one shared gateway and one gameflow heartbeat;
- PetHost, global hotkeys, maintenance, single-instance and updater helper retain explicit ownership/teardown.

## Stability findings closed before Batch M

### WinUI theme ownership

Real Win10 captured `E_ACCESSDENIED` while runtime attempted to mutate a platform-owned `SolidColorBrush`. Platform/system brushes are now read-only inputs; FACM semantic brushes are app-owned mutable objects. Startup remains fail-soft.

### Personalization async Busy

The surface previously synchronized controls manually while desktop-pet work was asynchronous. PropertyChanged + DispatcherQueue refresh now brings UI state back to the owner thread. Busy state has explicit “正在处理，请稍候…” feedback.

### Settings2 atomic mutation

Feature writers use a narrow atomic `UpdateAsync` boundary instead of whole-document load/mutate/save races. Recovery feature mutations remain read-only unless an explicit maintenance rebuild is authorized.

### Maintenance and League lifecycle

Maintenance supports retry after failed initialization and does not Dispose linked CTS/installer ownership under active awaits. League caller/lifetime cancellation and Window/ContentDialog teardown are contained without relaxing explicit write-confirmation boundaries.

### Updater interrupted replacement

`File.Replace` remains primary. Fallback/rollback use complete staging/backup plus same-directory `MoveFileEx(REPLACE_EXISTING | WRITE_THROUGH)` atomic swaps. No fallback path stream-copies over the live EXE. Built helper `--self-test` executes in Foundation.

## Repeated-operation stability

| Area | Repetition | Invariant | Result |
| --- | ---: | --- | --- |
| Settings2 cross-feature mutation | 40 rounds | unrelated fields not lost | PASS |
| Single-instance lifecycle | 24 rounds | one primary / one callback / clean release | PASS |
| Updater UAC cancellation | 24 rounds | cancel is fail-safe; no success launch | PASS |
| PetHost same-process prepare | 24 rounds | same SHA/path; no repeated reopen | PASS |
| League Recommended | 24 cycles | at-most-once write per stable cycle | PASS |
| League Efficiency hotkeys | 30 updates | registration/runtime/persistence stay in lockstep | PASS |

Batch M adds a separate **cross-process** PetHost disk-cache test because same-process repetition did not model FACM restart.

## Batch M: Win10 PetHost cross-process cache defect

### Real-machine observation

2026-08-29 Win10 22H2 evidence showed:

- recovery state `Running`, current/LKG version 4.0.0.0, consecutive failures 0;
- Settings2 LKG `glass-blue`, selected pet `moth`, enabled=false, F position `1569,576`;
- disabled-selection greenfly -> dragonfly -> moth completed;
- enable moth reached `pet-enable-start -> IsBusy=true -> payload-preparing`;
- more than 13 seconds later no `host-starting / ready / failed / finish` had appeared;
- F drag/persistence events continued during that window, so the main UI/message loop remained alive.

The evidence localized the delay to PetHost payload preparation rather than a whole-application UI deadlock.

### Root cause

Old `WindowsPetHostBundleStore` needed the payload SHA to find `runtime/pethost-host/<sha>`, but computed that SHA by opening and hashing the entire embedded PetHost ZIP on the first `PrepareAsync()` of every FACM process. The bundle is about 76.9 MB. `_cachedPreparation` made repeated calls fast only inside the same process, so previous 24-round smoke missed restart behavior.

### Fix

Batch M fix `6ba8c917c73e9f7eee1229b29ba9ed243be8ae83`:

- Foundation computes `PetHostBundle.sha256` immediately after the controlled PetHost ZIP is built;
- FACM.App embeds both `FACM.Resources.PetHost.zip` and `FACM.Resources.PetHost.sha256`;
- required candidate builds fail when either controlled resource is missing;
- App reads the tiny identity resource and supplies it to the bundle store;
- a new FACM process can check the exact disk cache before opening the large embedded ZIP;
- complete cross-process cache hit returns without reopening or rehashing the ZIP;
- local/lightweight builds without the identity retain the safe runtime hash fallback;
- WindowsSmoke constructs a fresh second store to simulate a new process and requires `openBundle == 0` for an existing complete cache;
- source gate freezes build identity + cross-process no-rehash + Busy-feedback contracts.

## Batch M CI acceptance

FACM 4.0 Foundation **#632 / run `33233590075` = SUCCESS**.

The run checked out PR merge ref containing `803e1ba5f9b671b0a787a8c77bb39912d4211b7d`, whose parent includes Batch M code fix `6ba8c917...`.

Controlled PetHost output:

```text
PetHostBundle.zip bytes=76,924,303
PetHostBundle SHA-256=48e24e9a67f7f75dffc4bef56eeadee9c13d9cc028c38679c8fab0c651141fc4
```

Both Release build and publish logged:

```text
Embedding FACM 4.0 PetHost bundle as FACM.Resources.PetHost.zip
Embedding FACM 4.0 PetHost identity as FACM.Resources.PetHost.sha256
```

Personalization gate logged:

```text
Personalization PropertyChanged/Dispatcher busy feedback: OK
Controlled PetHost build identity + extraction/cache/timeout boundary: OK
Cross-process PetHost cache no-rehash boundary: OK
FACM 4.0 Personalization foundation contract: SUCCESS
```

The same run passed all P1-P7 source/product gates, PowerShell 5.1 collector self-test, Release build with 0 warnings/0 errors, FoundationSmoke, WindowsSmoke, WinUI x64 self-contained single-file publish, publish-output verification and artifact upload.

## 2026-08-30 local candidate closeout

The isolated candidate worktree reproduced the Foundation sequence with .NET SDK `10.0.400`:

- FlyingHost publish/self-test PASS: 464 files, `72,052,263` bytes, SHA-256 `63f94f2bd3fbd4908d0736c9067f26c90afcd7798bdc2abc1929f7b2771cabb5`; no `VPet-Simulator.Core.dll`;
- PetHost publish/self-test PASS: 472 files, `76,915,115` bytes, SHA-256 `e295beec4035fe671b3e757b9b515668b8f7eca39178337a73c7c855424d00df`; `VPet-Simulator.Core.dll` present;
- all 28 source gates PASS;
- `FACM4.sln` Release x64 restore/build PASS with required bundles/updater and 0 warnings / 0 errors;
- FoundationSmoke PASS; WindowsSmoke PASS;
- FACM.App single-file publish PASS, 4 output files and 0 DLL entries; EXE `377,994,404` bytes / SHA-256 `5aa53107fd8efcf67423c3b625908ec083ed6ff5c3effb6f3d80f613c1fe90d6`; artifact ZIP `237,924,305` bytes / SHA-256 `0132c3e4c3037741f0e1af017a377888a6cc23c57d5177da3d99c6a75`.

`WFAC010` was a real .NET 10 warning caused by legacy manifest DPI nodes in the WPF/WinForms hosts. Both hosts now use `ApplicationHighDpiMode=PerMonitorV2`; FlyingHost manifest identity is `FACM.FlyingHost.app`; the warning is absent after republish. Three stacked-PR protection gates now compare the candidate/PR base parent rather than `origin/main`, so inherited production-control history is not misreported. `online/version.json` and `release/request.json` remain unchanged.

This is local evidence only. The latest known hosted run `33292986694` / job `99207749499` had `runner_id=0` and `steps=[]`; it did not execute source gates, build, smoke, publish, or artifact upload.

## Current targeted candidate

```text
artifact: facm4-x64
artifact id: 9709261625
artifact ZIP bytes: 165,704,303
GitHub artifact digest: sha256:32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
Foundation: #632 / 33233590075
```

Independent re-hash:

```text
ZIP SHA-256: 32331020c0c1c3fc93ebf70991ddff99a6349deede41e7374ae063da0aa9cb0a
FACM.App.exe bytes: 305,912,996
FACM.App.exe SHA-256: 5d65bd3f3e64a2520cb0c9514627a42e97781396d9e21013f04499fb464a9fea
ZIP DLL entries: 0
```

Old #628 artifact `9708452498` is superseded for current PetHost validation; its earlier integrity evidence is historical only.

## Parity matrix

| 3.5.15 behavior area | FACM 4 owner | Automated state | Remaining real evidence |
| --- | --- | --- | --- |
| Launcher-first F / compact / detailed Shell | App + Desktop Core/Platform | GREEN | complete supported-Windows behavior |
| F placement persistence | Settings2 + Desktop | GREEN | mixed-DPI / multi-monitor |
| Cleanup preview / confirm / elevation | Core + Platform.Windows + App | GREEN | real UAC cancel; separately authorized delete |
| Repair actions | Core + Platform.Windows + App | GREEN | real-machine operation |
| Themes / flying pets / VPet | Core + App + PetHost | BATCH-M CI-GREEN | targeted cache/visible-runtime retest |
| League Dashboard / Player / Live | shared League gateway/runtime | GREEN | real League client behavior |
| Recommended setup / ItemSet | Core + Infrastructure + App | GREEN | real write-path validation |
| Matchmaking / ReadyCheck / PostGame / Presence | shared gameflow services | GREEN | real League lifecycle |
| Efficiency / hotkeys | Core + Platform.Windows | GREEN | real conflict/focus behavior |
| Bench quick-pick | shared League gateway | GREEN | real ChampSelect behavior |
| ARAM / Mayhem | Core + Infrastructure + App | GREEN | live public-data/LCU behavior |
| Maintenance / online | Core + Infrastructure + App | GREEN | real network/UI behavior |
| Updater replacement | Infrastructure + Platform + Updater | GREEN automated | controlled interrupted replacement + final identity |
| Single-instance Ensure Open | Platform.Windows + App | GREEN | real second-launch behavior |
| 3.5.15 -> Settings2 | Core + Infrastructure | GREEN automated | real user-machine migration |
| Cross-module shutdown | App + platform runtimes | GREEN | real process/runtime shutdown |

## Gate13 boundary

Canonical readiness remains:

```text
22 required / 12 Passed / 10 Blocked
ReleaseReady=false
CUTOVER BLOCKED
```

Still required: non-admin/UAC, Defender/SmartScreen, Win10 1809, complete Win10 22H2, Win11, mixed-DPI/multimonitor, accessibility, real 3.5.15 migration, controlled interrupted updater replacement/rollback, final signed-package identity.

## Next validation

Use artifact `9709261625` for a focused Win10 PetHost retest first:

1. first enable may perform one extraction for a never-cached exact SHA, but must terminate in ready or explicit failure/timeout;
2. exit FACM normally and relaunch the same candidate from the same directory;
3. second process with complete cache must not spend a long interval in `payload-preparing`;
4. while enabled, switch pets 5-10 times; every Busy episode must return to interactive state;
5. Busy status must show “正在处理，请稍候…”;
6. collect `facm4-events.jsonl`, `settings.v2.lkg.json`, `state.json`.

Only after this targeted retest passes should P7 resume the broader non-destructive unified validation.

Do not execute real LOL deletion, updater kill/replacement, production pointer changes, release publication or legacy retirement without separate authorization.

## Completion boundary

Functional parity and automated stability are code-green through Batch M, but **real-machine P7 is not complete until the Batch M PetHost retest and the remaining unified functional checks are reviewed**. PR #234 stays Draft/unmerged. UI 2.0 remains after functional-equivalence validation. Gate13 remains a separate evidence/authorization process.
