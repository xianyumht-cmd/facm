# FACM 构建、验证与发布运行手册

## 1. 生产冻结线

FACM 3.5.15 是正式生产版本。正式 Gate 13 cutover 前：

- 不修改 production `online/version.json` / `release/request.json`；
- legacy `FACM.sln` / Updater / ToolBundle / PetHost 持续可构建；
- 不自动 deploy/restart、删除 branch/tag 或退休 legacy。

Legacy gates：`FACM Windows Build` + `FACM UI Text Contract`。

## 2. FACM 4.0 Foundation CI

当前顺序：

```text
architecture source gate
Shell source gate
desktop source gate
League Workbench source gate
Diagnostics source gate
DPI/Accessibility source gate
Recovery/Feature policy source gate
Release evidence/Performance source gate
Production cutover source guard
Real-machine evidence collector source gate
Windows PowerShell 5.1 collector self-test
restore FACM4.sln
build Release x64
FACM.FoundationSmoke (cumulative Gates 1-13)
FACM.WindowsSmoke
publish win-x64 self-contained single-file
verify EXE + no DLL leaks
upload facm4-x64
```

`TreatWarningsAsErrors=true` 持续开启；不得为了过 Gate 降低 warning/source gate。

本地等价：

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
pwsh ./scripts/check-facm4-desktop.ps1
pwsh ./scripts/check-facm4-league-workbench.ps1
pwsh ./scripts/check-facm4-diagnostics.ps1
pwsh ./scripts/check-facm4-accessibility.ps1
pwsh ./scripts/check-facm4-recovery.ps1
pwsh ./scripts/check-facm4-release-evidence.ps1
pwsh ./scripts/check-facm4-cutover.ps1
pwsh ./scripts/check-facm4-real-machine-evidence.ps1
powershell.exe -NoProfile -File ./scripts/collect-facm4-real-machine-evidence.ps1 -SelfTest
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj -c Release
```

## 3. Stable runtime paths

`Environment.ProcessPath` = distribution EXE；`AppContext.BaseDirectory` 可能是 `%TEMP%/.net/...`。settings/ui-text/logs/runtime/diagnostics/recovery/PetHost/update replacement 都只能从 distribution EXE 推导。

```text
<distribution>/settings.ini
<distribution>/settings.v2.json
<distribution>/runtime/recovery/state.json
<distribution>/runtime/recovery/settings.v2.lkg.json
<distribution>/runtime/recovery/feature-kill-switch.json
```

Release evidence 是 repository file `evidence/facm4-release-evidence.json`，不是 runtime 配置或 production release control。

## 4. Runtime owners

固定 exactly one：

```text
WindowsLeagueTransportSessionSource
LeagueHttpGateway
LeagueGameflowMonitor
PerformanceBudgetProvider
ProductStateStore
```

Gate 12 source gate 直接检查 App composition construction count。MainWindow/F/ViewModel 不新建第二 runtime。Bench 手动；writer 只通过 capability。

## 5. Diagnostics / DPI / Accessibility

Diagnostics 输入只允许 Product State、`facm4-events.jsonl`、`.1`；导出前二次 scrub，ZIP exactly `summary.txt/events.jsonl/manifest.json`。

Manifest 必须保持 `asInvoker + true/pm + PerMonitorV2, PerMonitor`。DPI conversion 只走 Core `DesktopDpi`；负坐标/off-screen recovery 保留。

Main navigation、Diagnostics actions、F entry 保持 stable AutomationId；Name/HelpText 来自 UI Text；action keyboard-capable；正文 Wrap；theme alias platform resources。

Hosted runner 不替代真实 mixed-DPI / High Contrast / screen-reader evidence。

## 6. Recovery / Feature Flags

Feature baseline 是 Core 手写 approved list；kill switch 只有 disable 语义。

`feature-kill-switch.json` 最大 32 KiB；unknown property/capability、bad schema/JSON、over-size/read failure => disable all approved。

Recovery state 最大 64 KiB；phase `Clean / Starting / Running / Failed / Recovering`；same-directory temp + WriteThrough + flush-to-disk + replace。上一轮停在 Starting => `previous-start-incomplete`。

Settings：strict primary 成功才刷新 LKG；strict load InvalidDataException 后才能读 validator-backed LKG；无 LKG 使用安全 defaults，`AutoUpdateEnabled=false`；坏 primary 不覆盖；legacy INI 不重写。

Update recovery 不替代 updater：没有 validated receipt 禁止 replacement；旧版本必须保留；replacement failed keeps old。

## 7. Gate 12 Performance regression

Gate12Smoke 必须逐字段保持：

```text
Desktop     4/2/2/2 history20 poll15s prefetch/maintenance/visual=true
Client      3/2/2/2 history12 poll20s true
Queueing    2/1/1/1 history4  poll30s false
ChampSelect 2/1/1/1 history0  poll45s false
InGame      1/1/1/1 history0  poll60s false
Background  1/1/1/1 history0  poll60s false
```

并验证 `Client <= Desktop`、`Queueing <= Client`、`ChampSelect <= Queueing`、`InGame <= ChampSelect`、`Background <= Desktop`。

Gameflow cadence：ChampSelect 2s；Matchmaking/ReadyCheck 3s；InGame 10s；Lobby/PostGame 5s；NotRunning/Connecting/ClientError 10s。

## 8. Release evidence runbook

Canonical matrix：`evidence/facm4-release-evidence.json`。

每项：

```text
id
category
requiredForRelease
status = Passed | Blocked | NotRun | Failed
evidence
notes
```

规则：

- `Passed` 必须有 evidence；
- required 非 Passed 必须有 blocker notes；
- mandatory ID 不得消失；
- JSON 不存 `releaseReady`；Core evaluator 从 required statuses 推导；
- `RELEASE BLOCKED` 是合法状态，不等于 CI failure；
- source/evidence schema 造假、runtime owner 变多、Performance/cadence/Smoke 消失才让 CI fail。

Gate 12 已合入 `main@4be7d6c38a8a59c6ff437a1352b8c0c4a5d2a798`。

Gate 13 Guard implementation evidence：

```text
head 71d82ea060f393f271048102bc4eff77d0707305
Foundation #240 SUCCESS
Windows Build #1366 SUCCESS
UI Text #487 SUCCESS
FACM.App.exe 227,794,567 bytes
artifact 9666591196
artifact ZIP 88,321,030 bytes
digest sha256:dc6a80aa80f1032af7dbb55721a1d19a02c72d1b4a01b49530c48252ffc4ab69
```

`gate13.cutover-guard` 已晋升 Passed。当前 matrix：**22 required / 12 Passed / 10 Blocked => ReleaseReady=false**。

## 9. Current external blockers

必须保持 Blocked，直到真实材料存在：

1. non-admin launch + UAC cancel；
2. Defender / SmartScreen；
3. Win10 1809；
4. Win10 22H2；
5. controlled real-user Win11；
6. real 100/125/150/175/200% mixed-DPI multi-monitor；
7. keyboard/focus + High Contrast + text scaling + basic screen reader；
8. real 3.5.15 -> 4.0 Settings migration/relaunch/rollback；
9. interrupted updater replacement/rollback；
10. final signing/package verification。

## 10. Gate 13 cutover guard

正式切换不是“CI 绿就发布”，而是双门：

```text
matrix validate
-> ReleaseEvidenceEvaluator.ReleaseReady == true
-> fresh production/destructive authorization
-> fresh production safety checks
-> only then permit production pointer / release / legacy retirement transaction
```

Authorization 必须满足：

```text
Granted = true
Scope = FACM4ProductionCutover
CandidateSha = evidence candidate headSha
IssuedAtUtc <= now
now - IssuedAtUtc <= 30 minutes
ExpiresAtUtc >= now
ExpiresAtUtc - IssuedAtUtc <= 30 minutes
```

Gate13Smoke 已证明：当前 repository matrix 即使提供形式正确授权仍返回 `ReleaseEvidenceBlocked`；synthetic all-pass matrix 在 missing/not-granted/wrong scope/wrong candidate/future/expired/stale/overlong authorization 下全部拒绝，只有 fresh matching authorization 才允许。

`check-facm4-cutover.ps1` 当前 `ReleaseReady=false` 时必须：

- 输出 `CUTOVER BLOCKED` 但 source gate 本身 SUCCESS；
- 确认 `FACM.sln`、`src/FACM`、`src/FACM.Updater`、`src/FACM.ToolBundle` 仍存在；
- 检查 Gate 13 diff 未修改 `online/version.json` / `release/request.json`；
- 禁止 application source 持久化/硬编码 production authorization。

任何条件不满足 => 不得修改 production pointer、deploy/restart、退休 legacy、删除 branch/tag。

普通“继续工程”不是 production/destructive authorization。Issue #213 在真实 production cutover 完成前保持 OPEN。

## 11. Gate 13 真正完成 / cutover transaction

只有 matrix 所有 required item 都 Passed 后，才进入 fresh release transaction：

1. 重新核对 final signed candidate identity、SHA-256/package/signature；
2. 重新跑 latest-head CI 与真实 Windows evidence，确认没有过期/换包；
3. 获得**当次**明确 production/destructive authorization；
4. fresh safety check：当前 production version/pointer、rollback artifact、main SHA、release request；
5. 执行受控 production cutover；
6. 验证 4.0.0 启动/更新/rollback/online pointer；
7. 仅在 cutover 验证通过后处理 legacy retirement；
8. 更新 evidence、canonical docs、Issue #213。

当前不满足第 1～3 条，因此 Gate 13 状态是 **GUARD VERIFIED / CUTOVER BLOCKED**，生产继续 FACM 3.5.15。

## 12. 一键真机 Release Evidence 采集

入口：仓库根目录 `FACM-4.0-真机证据采集.bat`。它固定调用系统自带 Windows PowerShell 5.1，不申请管理员权限，不联网，不修改注册表，不执行更新/重启/删除，也不修改 production pointer。

### 最简单用法

把待验证的最终 `FACM.App.exe` 直接拖到 BAT 上。也可以双击 BAT；如果同目录没有 candidate，则仍会采集机器、显示器、UAC/Defender/Accessibility 基本事实，并把 candidate 标为 missing。

命令行等价：

```bat
FACM-4.0-真机证据采集.bat "D:\FACM\FACM.App.exe" General
```

迁移证据建议同一台机器分阶段采集：

```bat
FACM-4.0-真机证据采集.bat "D:\FACM\FACM.App.exe" MigrationBaseline
FACM-4.0-真机证据采集.bat "D:\FACM\FACM.App.exe" MigrationAfter
```

Updater 受控中断/rollback 试验使用：

```bat
FACM-4.0-真机证据采集.bat "D:\FACM\FACM.App.exe" UpdaterRollback
```

### 输出

每次采集生成：

```text
FACM-4.0-Evidence-YYYYMMDD-HHMMSS/
├─ evidence.json
├─ README.txt
└─ collector.log
FACM-4.0-Evidence-YYYYMMDD-HHMMSS.zip
```

`evidence.json` 只保存稳定机器/文件事实、相对 settings 路径和文件名；默认脱敏用户名、UserProfile、Windows/UNC 路径、Basic/Bearer/token/password/secret/cookie/authorization。不会把完整 candidate 路径或个人目录写进 bundle。

自动采集内容包括：Windows edition/build/architecture、管理员态/UAC policy 基本事实、Defender/SmartScreen 配置事实、candidate SHA-256/version/AuthentiCode、显示器 bounds/DPI/scale/负坐标/mixed-DPI observation、High Contrast/text scale、settings/recovery 文件大小/时间/哈希。

### 自动观察不等于 PASS

collector 只负责**采集证据**，不会直接编辑 `evidence/facm4-release-evidence.json`。以下项目仍必须真实交互并人工审核：

- standard-user + UAC cancel；
- SmartScreen 实际 reputation/UI；
- mixed-DPI 跨屏真实拖动；
- keyboard focus / screen reader；
- Settings 3.5.15 -> 4.0 migration/relaunch/rollback；
- interrupted updater replacement/rollback；
- final signing/package identity。

因此 bundle 内会使用 `manual_required`、`observed_requires_interaction`、`observed_requires_review`，不会生成“Passed”。只有审核确认 bundle 与目标 candidate、机器、交互结果匹配后，才能由独立 evidence import/review 任务更新 canonical matrix。

CI 只执行 `-SelfTest`：验证 PS5.1 语法、脱敏、8 个 evidence slots、JSON roundtrip、ZIP 创建及 ZIP 固定条目。Hosted runner 的 self-test/机器信息**永远不能**替代真实 release evidence。

## 13. 2026-08-30 local Foundation-equivalent candidate verification

Use a clean worktree at `D:\project2\worktrees\facm-p7-candidate-2730` checked out at cloud candidate `2730eda15dc28a801871b5a3d10b4eecbd03a656`. The portable SDK used for this run was `.NET SDK 10.0.400`; the existing machine .NET 9 installation was not changed.

The local order is: publish/self-test FlyingHost and create `FlyingHostBundle.zip` + SHA; publish/self-test PetHost and create `PetHostBundle.zip` + SHA; build the updater and run its self-test; execute all source gates with workflow-compatible `pwsh`; restore/build `FACM4.sln` with required bundle/updater properties; run FoundationSmoke and WindowsSmoke; publish FACM.App as x64 self-contained single-file and verify the output contains no DLL.

The observed outputs were FlyingHost 464 files / `72,052,263` bytes / `63f94f2bd3fbd4908d0736c9067f26c90afcd7798bdc2abc1929f7b2771cabb5`, PetHost 472 files / `76,915,115` bytes / `e295beec4035fe671b3e757b9b515668b8f7eca39178337a73c7c855424d00df`, and FACM.App.exe `377,994,404` bytes / `5aa53107fd8efcf67423c3b625908ec083ed6ff5c3effb6f3d80f613c1fe90d6`. The App output had four files and zero DLL entries.

With .NET 10, `WFAC010` is real when a WPF/WinForms host keeps legacy `dpiAware`/`dpiAwareness` manifest nodes. Resolve it by setting `ApplicationHighDpiMode=PerMonitorV2` and removing those nodes; do not suppress the warning or lower warnings-as-errors. Stacked PR production-control gates must compare against the PR base parent, not `origin/main`, because inherited P2-P6 changes otherwise look like a new protected-file mutation.

## 14. 2026-08-30 Batch P IPC lifecycle candidate

Batch P was built in the isolated worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` from base `fc9f2376ceece94351837c9339ae9301ea7415ad` on temporary branch `tmp/p7-ipc-lifecycle-fix-20260830`. The formal P7 branch and PR #234 were not moved or merged.

The candidate adds cancellation-aware client command writes, poisoned-transport cleanup, post-kill process waiting, activate-gated WPF Host visibility, and a command-first server handshake. The targeted output is `artifacts/facm4-win10-targeted-batch-p.zip`: 237,928,250 bytes, SHA-256 `a5508c6ab65e3c5c023e957a32e44cf41ece7871f996f4338aaefaa71c9f8c80`.

The contained `FACM.App.exe` is 378,010,788 bytes with SHA-256 `662e1fb5b2df4c4d09bd5657059ba3f8086fbcb8a017380fbee76757a06046f0`. The targeted directory contains four files and zero DLL files. The new host payload identities are FlyingHost bundle `2a4f9722adbc21a5a63050ed50510fea1f3a01a9a844230c56c9e48bc6639f81` and PetHost bundle `ca63f66db51012eda1a5f4816dffc3a8b762f0d0bf0fd165ef6a2aea232cd051`.

Local verification completed: both Host self-tests, solution Release build (0 warnings / 0 errors), the two desktop-pet source gates, personalization source gate, deterministic FoundationSmoke, deterministic WindowsSmoke, and the new IPC lifecycle smoke. Before any real-machine conclusion, retest the candidate with `real-bee -> butterfly -> vpet -> real-bee -> Off`; only after that minimal sequence is clean should the longer six-pet / ten-round sequence run. Do not treat this local candidate as a production release.

## 15. 2026-08-30 Batch Q-S and T1 local candidate

The current continuation uses `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` on `tmp/p7-ipc-lifecycle-fix-20260830`. Local commits are Batch Q `681b6de`, Batch R `60c8ae2`, Batch S `03ceba4`, WinUI/WinForms build compatibility `7b084b0`, and T1 League trace instrumentation `5600d94`.

For this candidate, use `.NET SDK 10.0.400` from `D:\project2\dotnet10`. Keep `TEMP`, `TMP`, `NUGET_PACKAGES`, and `DOTNET_CLI_HOME` under `D:\project2` when restoring, building, or running smoke tests. The verified local commands are a Debug x64 `FACM4.sln` build, FoundationSmoke with `--skip-gate13`, and WindowsSmoke; all completed with 0 warnings / 0 errors and SUCCESS.

T1 is an evidence collector only. Start FACM from the candidate output, reproduce the real League phase sequence, then preserve `logs\facm4-events.jsonl` and the matching settings/state files. Only after the trace is reviewed may a separate T2 change address a ranked root cause. Do not use T1 as authorization to alter production pointers, merge PR #234, run Gate13, or publish FACM 4.0.

## 16. 2026-08-30 Live LCU reliability candidate

Use the isolated worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` and verify the executable path after every build. For this candidate the current runnable Debug x64 binary is under `src\FACM.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\FACM.App.exe`; the older `bin\x64\Debug` copy must not be used as evidence unless its timestamp and commit are explicitly verified.

The local .NET SDK is `D:\project2\dotnet10\dotnet.exe` (10.0.400). Keep `DOTNET_CLI_HOME`, `TEMP`, `TMP`, and NuGet packages under `D:\project2`. The verified commands for this stage are: App self-contained Debug x64 build with 0 warnings / 0 errors; FoundationSmoke with `--skip-gate13` and 0 warnings / 0 errors; then launch the exact current App binary and inspect `logs\facm4-events.jsonl` for lifecycle, paired Workbench stages, exception fields, HTTP outcome/classification, PID/port and in-flight counters.

The latest natural local interaction kept FACM PID `16436` responsive and completed Workbench Dashboard/Player/Live/Advisor/Refresh without COMException. The current LCU observation is LeagueClient PID `8812`, LeagueClientUx PID `20504`, port `61101`, phase `Lobby / Connected`. The cumulative log has 387 HTTP completions (340 success, 83 ExpectedUnavailable, 0 UnexpectedFailure), p50/p95/max `0/10/374 ms`, and max in-flight `2`. Two automation matchmaking writes took `109 ms` and `127 ms`; no Auto Accept write occurred because the trace did not enter ReadyCheck. This is not a ReadyCheck or full real-machine pass. A future ReadyCheck test must be natural and read-only except for the already-authorized Auto Accept behavior; do not start a queue or game merely to manufacture evidence. The persisted settings currently read `autoMatchmakingEnabled=false` and `autoAcceptEnabled=true` while the same trace contains two automation matchmaking writes; resolve this runtime/persistence discrepancy with diagnostics before changing settings behavior.

## 17. 2026-08-31 Morphing Surface MS9 runtime candidate

本阶段的真实根因证据来自不可变目录 `D:\project2\facm-ms8-out-20260831`：实际日志含 90 个
`facm.surface.presentation-failed`，全部为 `System.InvalidOperationException` / `0x80131509`；
其中 outside-click 84 个、collapse-to-orb 5 个、gameflow-lobby-restored 1 个。MS9.1
operation telemetry 将抛错定位到 `EnsureSurfacePresentationInvariant` 的 `invariant-check`，
因为系统实际外框 `136×39`，而 Orb 目标 `36×36`；失败上下文为 UI thread 2，
`hasThreadAccess=true`，`dispatcherQueueAvailable=true`。

最终修复位于提交 `e834763b09f69d7aaa0951af3bc8a0601d64edf3`，Windows 平台层对唯一 Morphing
MainWindow HWND 仅适配 `WM_GETMINMAXINFO` 最小跟踪尺寸，并把其它消息转发给原窗口过程。最终
候选如下：

```text
worktree:  D:\project2\worktrees\facm-p7-ipc-lifecycle-fix
branch:    tmp/p7-ipc-lifecycle-fix-20260830
head:      e834763b09f69d7aaa0951af3bc8a0601d64edf3
candidate: D:\project2\facm-ms9.4-runtime-out-20260831-1305
exe:       D:\project2\facm-ms9.4-runtime-out-20260831-1305\FACM.App.exe
sha256:    94AD1C97C93C32285A76F27E3CB3FE78FBE42B7D1BDEEC2DC18B789DD4E66412
```

最终候选实际外框为 `36×36`、客户区 `30×30`，唯一精确匹配的 `FACM.App.exe` 进程保持响应。
真实窗口完成 100 次 Orb→ControlMatrix→Orb，100/100 成功；日志为 0 presentation-failed、0
operation-failed、0 invariant-failed、0 stale、0 unhandled、0 fatal。Repair/FeatureSurface→Orb
和 LeagueSurface→Orb 也已在同一修复序列的真实窗口中通过。27/27 非 cutover source gates、
`FACM4.sln` Debug x64、FoundationSmoke `--skip-gate13`、WindowsSmoke 均通过。

启动和人工验证规则保持不变：默认环境进入 Morphing Surface，只有兼容对照时才使用
`FACM_SHELL_EXPERIENCE=legacy`；启动前确认只有目标 FACM 进程，退出使用应用自身关闭流程。
本轮没有注入桌面空白 outside-click，也没有制造 ChampSelect/Lobby 自然回归，因此
outside-click、modal、ChampSelect/Lobby、tray/single-instance、桌宠切换、多屏/DPI 和视觉复核
仍为 `USER_MANUAL_VALIDATION_REQUIRED`。该候选不是 release-ready；禁止由本地候选触发 Gate13、
release、production pointer、merge 或 push。

## 18. 2026-08-31 Morphing Bench Swap Strip BS1–BS6 candidate

本阶段使用 `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix` 的
`tmp/p7-ipc-lifecycle-fix-20260830`，代码基线为 `f80a70e065c33b9ba650b09a2cebcc0088233bfc`。
产品代码提交为 `4b9fe1b`（candidate identity model）、`fea17fd`（Morphing strip / shared Live
state / detailed Workbench reuse）和 `d551a46`（post-click authoritative refresh）；测试/门禁提交为
`dc70c98`、`028268e`、`50f7026`。`src/FACM.Platform.Windows/FACM.Platform.Windows.csproj`
的换行噪声和既有 `out/` 仍未纳入提交。

Bench candidate source is `LeagueWorkbenchViewModel.Live.BenchChampionIds`, populated by the
existing `LeagueWorkbenchDataSource` Legacy/TeamBuilder session reads. Both the strip and the
detailed League card use `LeagueBenchCandidatePresentation`; both route clicks to the existing
`LeagueBenchQuickPickService.TrySwapAsync` and its one POST plus bounded read-back. Portrait identity
uses the existing champion summary and icon paths/cache; no new portrait network path or polling owner
was added.

Verified local commands, with all temporary directories under `D:\project2`, were:

```powershell
dotnet build src/FACM.App/FACM.App.csproj --configuration Debug --no-restore
dotnet build FACM4.sln --configuration Debug -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj --configuration Debug --no-restore -- --skip-gate13
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj --configuration Debug --no-restore
```

Results: FACM.App 0 warnings/0 errors; FACM4.sln Debug x64 0 warnings/0 errors;
FoundationSmoke SUCCESS with Gate13 omitted; WindowsSmoke SUCCESS; Bench targeted smoke passed
inside FoundationSmoke; all 28 `check-facm4-*.ps1` source gates passed. Gate13 was not run.

Fresh user-review candidate:

```text
directory: D:\project2\facm-bs6-review-out-20260831-1600
exe:       D:\project2\facm-bs6-review-out-20260831-1600\FACM.App.exe
config:    Debug / win-x64 / self-contained / single-file
bytes:     421024376
sha256:    68766D9B9D2511B846F477FA658EF6573BC7197CBE94861D36BFE0481DF8CE9B
files:     1
dlls:      0
```

This candidate is for user review only. Do not overwrite the immutable MS9.4 candidate, modify
production pointers, merge/push/release, or treat deterministic green as natural ARAM, modal,
outside-click, accessibility, mixed-DPI, or Gate13 evidence.

## 2026-08-31 BOOT-1 local review candidate

Implementation and all generated validation material must remain under `D:\project2`. Use the portable
`.NET SDK 10.0.400` at `D:\project2\dotnet10\dotnet.exe`; keep `DOTNET_CLI_HOME`, `TEMP`, `TMP` and
NuGet packages on D:. The implementation source is the isolated worktree
`D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`; do not use `D:\project2\Facm` as its source.

Build the candidate with:

```powershell
$env:DOTNET_CLI_HOME='D:\project2\facm-dotnet-home'
& 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\boot1\Build-BootCandidate.ps1' `
  -RepoRoot 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix' `
  -OutputRoot 'D:\project2\facm-boot1-review-20260831-final' `
  -BuildRoot 'D:\project2\facm-boot1-build-20260831-final'
```

The review root should contain only `FACM.exe` and `.facm`; build intermediates belong in the separate
`BuildRoot`. The generated layout is:

```text
FACM.exe
.facm/
  app/ runtime/ components/ versions/ staging/ cache/ logs/ state/
```

Run deterministic checks with:

```powershell
& 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\boot1\Test-Bootstrapper.ps1' `
  -CandidateRoot 'D:\project2\facm-boot1-review-20260831-final' `
  -TestRoot 'D:\project2\facm-boot1-tests-20260831-final'
```

The test covers no-pet binary packaging, active state, local A/B provision, switch/rollback, malformed
state, failed staging preservation, Unicode-safe arguments, stable data root, optional-pet fail-soft and
single-instance mutex behavior. Verify a pack with `FACM.exe --verify-pack <zip> --manifest <manifest> --no-ui`;
the expected exit code is 0 for a matching pack and 11 for a size/hash mismatch.

For an app-local launch check, start the exact review-root `FACM.exe` and inspect
`.facm\logs\bootstrapper.jsonl` plus `.facm\logs\facm4-events.jsonl`. The bootstrapper must resolve the
active Core under `.facm\versions`, and the app log should correlate `app.bootstrap-launch` with
`desktop-launcher-ready`. Stop the candidate through its own window-close path; do not force-kill it when
collecting lifecycle evidence. A review launch is not a real-machine, signing, network provisioning or
Gate13 result.

BOOT-1 currently creates a ZIP pack for delivery evidence but the native prototype provisions an expanded
local source tree; do not describe this stage as having native ZIP extraction or a production downloader.
Do not merge, push, release, modify production pointers, move formal P7, or run Gate13 from this candidate.

## 2026-08-31 BOOT-2 network provisioning candidate

All BOOT-2 build, package, mirror, test, log and temporary paths remain under `D:\project2`. The implementation
source is the isolated worktree `D:\project2\worktrees\facm-p7-ipc-lifecycle-fix`; do not use
`D:\project2\Facm` as implementation source. The build script also pins `TEMP`, `TMP`, `DOTNET_CLI_HOME`
and NuGet packages to D:.

Build a fresh candidate and local mirror with:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\boot1\Build-Boot2Candidate.ps1'
```

The resulting review roots are `D:\project2\facm-boot2-review-20260831\clean-first-run` and
`D:\project2\facm-boot2-review-20260831\pre-provisioned`; the deterministic source is
`D:\project2\facm-boot2-mirror-20260831`. Run the full local supply/update experiment with:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\boot1\Test-Boot2.ps1'
```

`Start-Boot2TestMirror.ps1` is a local static HttpListener with Range support and request logging. The test
intentionally uses a missing primary URL so mirror failover is observed, preloads a 4KB `.partial` app pack,
stops the server to prove the normal fast path, then runs no-change, app-only and runtime-only update variants.
The test uses `--dry-run`; it validates provisioning/activation without claiming natural League or real-machine
behavior. Production URLs must be HTTPS, production trust must use a real signed manifest/package policy, and
the current `unsigned-local` mode must not be shipped as release trust.

Failure safety: verified packages are promoted only after hash/size verification; extraction occurs inside
fresh staging; active state is written after composition; old active is never deleted during a failed network,
hash, extraction or activation attempt. Failed staging is preserved under the controlled `.facm\staging`
directory for diagnosis/cleanup. Do not merge, push, release, modify production pointers, move formal P7 or
run Gate13 from this candidate.

## 2026-08-31 BOOT3-A trust verification candidate

The focused trust contract is in `docs/BOOT3A-TRUST.md`. The source gate is:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\scripts\check-facm4-boot3a.ps1'
```

Build the bootstrapper with the D: toolchain and keep the output under `D:\project2`, then run the focused
fixture test. The test requires an externally held local validation private key corresponding to the embedded
`facm-production-r1` public root; it creates a separate unmistakable `facm-test-only-r1` key under its D: test
root and proves production rejects that identity. No private key or signed fixture is written to the repository.

```powershell
$env:PATH = 'D:\project2\w64devkit-2.9.1\w64devkit\bin;' + $env:PATH
cmake -S 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\src\FACM.Bootstrapper' `
  -B 'D:\project2\facm-boot3a-native-build' -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build 'D:\project2\facm-boot3a-native-build' --config Release --parallel 2
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\boot1\Test-Boot3A.ps1'
```

`--verify-trust-bundle` is a bounded verification diagnostic: it validates a local signed application/component
bundle, package hash, native CAB extraction, and extracted content digest without changing active state. The
production network path performs the same exact-byte signature checks over HTTPS. The 2026-08-31 candidate also
passed Release x64 with 0 warnings / 0 errors, FoundationSmoke `--skip-gate13`, WindowsSmoke, 29 non-cutover
source gates, and an independent BOOT-2 regression smoke. BOOT3-A does not run Gate13, touch production pointers,
release/merge/push PR #234, move Formal P7, or retire FACM 3.5.15.

## 2026-08-31 BOOT3-B signed artifact pipeline

BOOT3-B build, signing-request, signer-response, validator, test-key and temporary paths must remain under
`D:\project2`. The normal builder does not receive a private key. It reuses `tools/boot1/Build-Boot2Candidate.ps1`
for the three CAB stages, then creates a production schema-3 bundle and `signing-request.json`:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Build-FacmBoot3BRelease.ps1' `
  -OutputRoot 'D:\project2\facm-boot3b-release-<date>' `
  -Version '4.0.0-<release>' `
  -ManifestBaseUrl 'https://<approved-origin>/facm/<release>'
```

The external signer receives the request and returns only detached Base64 signatures. Apply responses with:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Apply-FacmSigningResponses.ps1' `
  -RequestPath 'D:\project2\facm-boot3b-release-<date>\signing-request.json' `
  -SignatureRoot 'D:\project2\facm-boot3b-release-<date>\signer-responses' `
  -BundleRoot 'D:\project2\facm-boot3b-release-<date>\bundle'
```

Validate a completed bundle offline with:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Test-FacmReleaseBundle.ps1' `
  -BundleRoot 'D:\project2\facm-boot3b-release-<date>\bundle' `
  -Bootstrapper 'D:\project2\facm-boot3a-native-build\FACM.exe'
```

`Test-Boot3BRelease.ps1` uses a non-formal local validation key outside the repository only to emulate an external
signer. It verifies deterministic output from the same BOOT-2 inputs, exact-byte signature sensitivity, replay/tamper/
unknown/planned/test-only/unsigned/metadata/package/downgrade rejection and successful native validation. The release
bundle validator is read-only with respect to installed FACM state. BOOT3-B does not run Gate13, modify production
pointers, upload artifacts, merge/push PR #234, move Formal P7, or retire FACM 3.5.15.

## 2026-08-31 BOOT3-C production-like HTTPS rehearsal

BOOT3-C local origin/mirror infrastructure is test-only. It uses a fresh output root under `D:\project2`, a
candidate bootstrapper built from the current worktree, and an external local validation key only for test signing.
The production private key is never passed to the builder or test harness.

Build the native candidate with the pinned toolchain:

```powershell
$env:PATH = 'D:\project2\w64devkit-2.9.1\w64devkit\bin;' + $env:PATH
cmake -S 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\src\FACM.Bootstrapper' `
  -B 'D:\project2\facm-boot3c-native-build-20260831' -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build 'D:\project2\facm-boot3c-native-build-20260831' --config Release --parallel 2
```

Run the local HTTPS distribution rehearsal:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Test-FacmBoot3CHttpsDistribution.ps1' `
  -Bootstrapper 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe'
```

The script generates a short-lived `FACM BOOT3-C local test` certificate and adds only that public certificate to the
current user's Root store so WinHTTP can perform actual TLS validation. Confirm the Windows prompt only when the
displayed name and fingerprint belong to this test certificate. The script removes its private key, certificate file
and matching current-user Root entry in cleanup. If cleanup is interrupted, remove only the exact certificate shown in
the script's test output; do not change machine-wide trust settings.

Expected evidence is retained under `D:\project2\facm-boot3c-https-tests-20260831\results.json` and per-scenario
redacted request/bootstrap logs. The harness covers primary success, primary unavailability with mirror success,
corrupt primary package with mirror success, corrupt mirror fail-closed with old active preservation, incomplete
`.partial` recovery, stale staging cleanup, same-version idempotence, redirect rejection, local rollback and disk-space
diagnostics. It does not claim a production CDN, external signer, real-machine PASS, online pointer change or Gate13.

Generate the real-machine evidence/checklist without making deployment changes:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Test-FacmBoot3CRealMachineHarness.ps1' `
  -Target Windows10-22H2 `
  -CandidatePath 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe'
```

All matrix rows remain `manual_required` until reviewed on the intended physical Windows 10 22H2 and controlled
Windows 11 machines. Do not turn automatic collector facts into release PASS by copying or editing the JSON.

The BOOT3-C source contract gate is:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\scripts\check-facm4-boot3c.ps1'
```

## BOOT3-C publication ordering (design only)

For a later explicitly authorized production task: build from a reviewed source commit; obtain the external signer
response and offline/native validation; publish immutable CAB blobs to approved primary and mirror version paths;
independently compare bytes and hashes from both origins; publish signed component manifests and signatures; publish
the signed application manifest and signature; then, only with release-owner authorization, update release index and
online pointers. Never publish a pointer before all referenced immutable bytes are available from every approved origin.

## 2026-08-31 FREE-DIST-1 GitHub Release candidate preparation

The zero-cost distribution candidate can be prepared locally without publication:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Prepare-FacmFreeDistCandidate.ps1'
```

This reuses the already verified BOOT-2 component stages, builds canonical GitHub Release metadata for the selected
tag, applies detached signatures using only the external local validation key, and writes the release-compatible
bundle to `D:\project2\facm-free-dist-release-20260831\bundle`. It writes the two-file launcher review candidate to
`D:\project2\facm4-free-dist-review-20260831`. The key is not copied into either output.

Run the focused transport and trust-separation test:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Test-FacmFreeDistProxyTransport.ps1'
```

Run the source contract gate:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\scripts\check-facm4-free-dist.ps1'
```

The candidate transport order is `ghfast.top`, `gh-proxy.com`, `gh.llkk.cc`, and `github-direct`. Signed metadata
must contain only canonical GitHub Release URLs; public proxy URLs must never be copied into manifests. Native
redirect handling accepts only bounded HTTPS redirects to GitHub-owned release asset hosts. Range resume must retain
the partial file across candidate failure, reject mismatched `Content-Range`, restart safely on a resume-time `200`,
and recheck exact package/content identity before activation.

Evidence is retained at `D:\project2\facm-free-dist-release-20260831\free-dist-evidence.json` and
`D:\project2\facm-free-dist-probe-20260831\free-dist-test-results.json`. This task intentionally does not create
the GitHub Release, upload assets, change `online/version.json` or `release/request.json`, merge/push PR #234,
perform real-machine publication acceptance, run Gate13, or retire FACM 3.5.15. The public repository currently has
FACM 3.5.15 rather than the local FREE-DIST candidate, so first-run and second-launch acceptance against the public
release remain pending explicit release-owner authorization.

## 2026-09-01 FREE-DIST-2 final candidate revalidation

Use the explicit .NET 10 executable `D:\project2\dotnet10\dotnet.exe` (SDK 10.0.400) for this candidate. Keep
`TEMP`, `TMP`, `DOTNET_CLI_HOME`, and `NUGET_PACKAGES` under `D:\project2`; do not rely on a shell that still resolves
`dotnet` to the machine .NET 9 installation.

The full Release solution build completed with 0 warnings and 0 errors. The native bootstrapper Release build,
32 non-cutover source gates, BOOT3-A, BOOT3-B, BOOT-2 13/13, BOOT3-C 8/8, FREE-DIST, FoundationSmoke
`--skip-gate13`, and WindowsSmoke all passed. Gate13 and the cutover gate were not run.

The final non-production candidate is prepared with tag `v4.0.0-free-dist-test.1`, title
`FACM 4.0.0 FREE-DIST test.1`, and `prerelease=true`:

```text
D:\project2\facm-free-dist-final-candidate-flat4-20260901\bundle
D:\project2\facm4-free-dist-final-review-flat4-20260901
D:\project2\facm-free-dist-final-candidate-flat4-20260901\free-dist-evidence.json
```

The bundle is a flat GitHub Release asset set with unique names. The launcher-only directory contains exactly
`FACM.exe` and `bootstrap.json`. The local final-candidate BOOT3-C and FREE-DIST evidence is retained at:

```text
D:\project2\facm-free-dist-final-candidate-flat4-boot3c-20260901\results.json
D:\project2\facm-free-dist-final-candidate-flat4-probe-20260901\free-dist-test-results.json
```

This is `PASS_LOCAL` only. The public test Release, public clean-machine first run, second-launch zero-download proof,
and real Windows acceptance are still `WAITING_FOR_PUBLIC_TEST_RELEASE`. Publishing requires a separate explicit
authorization.

## 2026-09-01 FREE-DIST-3 single-launcher prerelease rehearsal

FREE-DIST-3 changes the clean first-run shape from `FACM.exe + bootstrap.json` to exactly `FACM.exe`. The default
non-production manifest URL is generated into the native executable by `src/FACM.Bootstrapper/CMakeLists.txt` from
`BootstrapDefaults.h.in`. A valid `bootstrap.json` remains optional and may provide discovery overrides; malformed or
unsupported configuration falls back to the compiled URL without changing the embedded production trust table.

Build with the pinned local toolchain and run the focused harness:

```powershell
$env:PATH = 'D:\project2\w64devkit-2.9.1\w64devkit\bin;' + $env:PATH
cmake -S 'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\src\FACM.Bootstrapper' `
  -B 'D:\project2\facm-boot3c-native-build-20260831' -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build 'D:\project2\facm-boot3c-native-build-20260831' --config Release --parallel 2
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File `
  'D:\project2\worktrees\facm-p7-ipc-lifecycle-fix\tools\release\Test-FacmSingleLauncher.ps1'
```

The harness must report `SingleLauncherBeforeFirstLaunch: PASS` with exactly one `FACM.exe`, prove local signed
three-component provisioning without creating `bootstrap.json`, observe a real `FACM.App.exe` Orb launch, and pass
the optional-config and trust-boundary negative cases. It writes evidence to
`D:\project2\facm4-single-launcher-tests-20260901\results.json` and prepares the exact review root
`D:\project2\facm4-single-launcher-review-20260901`.

The test certificate is current-user-only temporary material and must be removed in cleanup. If a proxy is temporarily
needed for the native live probe, record the original WinHTTP state and restore it immediately after the run. Do not
publish the prerelease, push/merge, change production pointers, run Gate13, or retire FACM 3.5.15 from this rehearsal.

## 2026-09-01 FREE-DIST-4 public prerelease validation

The authorized non-production release `v4.0.0-free-dist-test.1` was briefly published with `draft=false`,
`prerelease=true`, and exactly 13 assets, then withdrawn and deleted after the blocker below. Its historical URL was
`https://github.com/xianyumht-cmd/facm/releases/tag/v4.0.0-free-dist-test.1`; no public test.1 release currently exists.
The approved local bundle was independently downloaded through the public canonical URLs into
`D:\project2\facm4-public-release-download-20260901`; all 13 names, sizes, and SHA-256 values matched with no extras.

Public first-run evidence is retained in
`D:\project2\facm4-public-first-run-test-20260901` and its bootstrap log. Only `FACM.exe` was present before launch;
the real Orb started after 103,647,538 CAB bytes were downloaded. Second launch and a temporarily unavailable
WinHTTP offline launch passed with zero new manifest/download/extraction events. The controlled valid-prefix Range
root is `D:\project2\facm4-public-range-resume-controlled-20260901`; it recorded a resume event, completed all three
components, and passed a follow-up Orb launch. Proxy probes and prior deterministic BOOT3-C evidence are retained in
the existing evidence directories listed above.

This validation is **BLOCKED**, not `PUBLIC_FREE_DIST_TEST_READY_FOR_USER`. A forced termination at the exact end of a
CAB download left a `.partial` whose size and SHA-256 equaled the complete package; the current code then requested
an invalid Range at EOF, received HTTP 416 through the available transports, and could not recover. The failing root is
`D:\project2\facm4-public-interrupted-range-test-20260901`. Fix and regression-test this boundary before preparing a
new `test.2`; never mutate the existing `test.1` asset identity. Do not create the final user copy, merge/push PR #234,
move formal P7, run Gate13, alter production pointers, or retire FACM 3.5.15.

## 2026-09-01 FREE-DIST-5 test.2 release and recovery acceptance

FREE-DIST-5 changes only the full-size `.partial` recovery boundary. Before using a Range request, the native
bootstrapper now handles `partialSize == packageSize` as follows: verify the complete file against the authenticated
manifest SHA-256, atomically promote a valid file to the complete cache, or delete an invalid file and restart from byte
zero. `partialSize < packageSize` continues to use bounded Range resume, and `partialSize > packageSize` remains a safe
rejection/restart path. BOOT3-A/BOOT3-B trust behavior and the canonical signed URL surface are unchanged.

The fresh test.2 build and candidate are:

```text
D:\project2\facm-free-dist5-native-test2-20260901\FACM.exe
D:\project2\facm-free-dist5-test2-candidate-20260901\bundle
D:\project2\facm-free-dist5-test2-candidate-20260901\free-dist-evidence.json
D:\project2\facm-free-dist5-test2-boot3c-background4-20260901\results.json
D:\project2\facm-free-dist5-test2-proxy-probe2-20260901\free-dist-test-results.json
D:\project2\facm-free-dist5-test2-one-file-review-20260901\FACM.exe
```

The native executable is 3,364,691 bytes with SHA-256
`887386803d33215304a21c5e55fcf84c1fef0b7bfa273d7feb828f711425edb5`. The flat signed bundle contains exactly 13
assets and 103,647,538 CAB bytes. The local BOOT3-C harness is 10/10 PASS, including both new full-size partial cases;
all 32 non-cutover source gates, BOOT2, BOOT3-A, BOOT3-B, BOOT3-C, FREE-DIST, the .NET 10 Release x64 solution build
(0 warnings / 0 errors), FoundationSmoke `--skip-gate13`, and WindowsSmoke passed.

The authorized public prerelease is `v4.0.0-free-dist-test.2`, title `FACM 4.0.0 FREE-DIST test.2`,
`draft=false`, `prerelease=true`, targeted at remote main SHA `269da6c751a8463542ed0d172300675deff9571e`. Anonymous
download comparison passed 13/13 for exact size and SHA-256 at
`D:\project2\facm-free-dist5-test2-public-assets-20260901`.

Public acceptance evidence:

- `D:\project2\facm-free-dist5-test2-public-first-run2-20260901`: one-file first run, 19.7 seconds, 103,647,538 CAB
  bytes, real Orb launch;
- same root second launch: 0.1 seconds, no new manifest/download/extraction events;
- same root offline third launch: 0.1 seconds with temporary invalid WinHTTP proxy, no network/extraction events, WinHTTP
  restored to Direct access;
- `D:\project2\facm-free-dist5-test2-public-range-resume-20260901`: 1 MiB nonzero Range resume, final hash and Orb PASS;
- `D:\project2\facm-free-dist5-test2-public-fullsize-valid-20260901`: valid full-size app partial promoted with no app
  CAB download event, final hash and Orb PASS;
- `D:\project2\facm-free-dist5-test2-public-fullsize-invalid-20260901`: corrupted full-size partial rejected, three CAB
  downloads including app restart from byte zero, final hash and Orb PASS.

The user review copy is `D:\project2\FACM-4.0-FREE-DIST-TEST`, exactly one `FACM.exe`, with the same 3,364,691-byte
SHA-256 identity. The local single-launcher helper also passed its core one-file/default/Orb/transport checks; one rerun
hit a CurrentUser Root test-certificate-store stall during setup, so the public one-file acceptance is the final runtime
evidence. Do not infer production readiness from this prerelease: production remains FACM 3.5.15; no source push, PR
#234 merge, Formal P7 move, Gate13, production pointer change, or restart was performed.
