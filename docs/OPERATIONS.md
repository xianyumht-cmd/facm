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
