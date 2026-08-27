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
