# FACM 构建、验证与发布运行手册

## 1. 生产冻结线

FACM 3.5.15 是正式生产版本。Gate 13 前：

- 不修改 production `online/version.json` / `release/request.json`；
- legacy `FACM.sln` / Updater / ToolBundle / PetHost 持续可构建；
- 4.0 只在对应 task branch/PR 验证。

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
restore FACM4.sln
build Release x64
FACM.FoundationSmoke
FACM.WindowsSmoke
publish WinUI win-x64 self-contained single-file
verify EXE + no DLL leaks
upload facm4-x64
```

`TreatWarningsAsErrors=true` 持续开启；不为通过 Gate 降低 warning/source gate。

本地等价命令：

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
pwsh ./scripts/check-facm4-shell.ps1
pwsh ./scripts/check-facm4-desktop.ps1
pwsh ./scripts/check-facm4-league-workbench.ps1
pwsh ./scripts/check-facm4-diagnostics.ps1
pwsh ./scripts/check-facm4-accessibility.ps1
pwsh ./scripts/check-facm4-recovery.ps1
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj -c Release
```

## 3. Stable runtime paths

`Environment.ProcessPath` = distribution EXE；`AppContext.BaseDirectory` 可是 `%TEMP%/.net/...`。settings / ui-text / logs / runtime / diagnostics / recovery / PetHost / update replacement 只能从 distribution EXE 推导。

```text
<distribution>/settings.ini
<distribution>/settings.v2.json
<distribution>/runtime/recovery/state.json
<distribution>/runtime/recovery/settings.v2.lkg.json
<distribution>/runtime/recovery/feature-kill-switch.json
```

## 4. Runtime owners

固定 exactly one：

```text
WindowsLeagueTransportSessionSource
LeagueHttpGateway
LeagueGameflowMonitor
PerformanceBudgetProvider
ProductStateStore
```

MainWindow/F/ViewModel 不新建第二 runtime。League gameflow cadence 与 Product State/Performance 同源。Bench 手动；writer 只通过 capability。

## 5. Diagnostics runbook

只读输入：Product State、`facm4-events.jsonl`、`.1`。禁止 settings/lockfile/env/Registry/browser cookies/目录递归/crash dump。

导出前二次 scrub secret/Basic/Bearer/Windows/UNC path；malformed line 只计数丢弃。ZIP exactly `summary.txt/events.jsonl/manifest.json`；bounded；temp -> final；输出固定 `<distribution>/runtime/diagnostics`。

## 6. DPI / Accessibility runbook

Manifest 必须同时保持：

```text
requestedExecutionLevel = asInvoker
dpiAware = true/pm
dpiAwareness = PerMonitorV2, PerMonitor
```

DPI->scale / DIP->physical pixel 只走 Core `DesktopDpi`。Gate10Smoke 保持 96/120/144/168/192 DPI、mixed X/Y、left/right/top/negative work areas、off-screen recovery、invalid DPI fail closed。

Main navigation、Diagnostics actions、F entry 保持 stable AutomationId；Name/HelpText 来自 UI Text；主要动作使用 keyboard-capable controls；禁止 pointer-only action / `IsTabStop=False`；长正文 Wrap；theme alias WinUI platform resources。

Hosted runner 不替代真实 mixed-DPI / High Contrast / screen reader evidence。

## 7. Gate 11 Feature kill switch runbook

Feature baseline 是 Core 显式 approved list。Kill switch 只有 disable 语义，没有 enable override。

文件：`<distribution>/runtime/recovery/feature-kill-switch.json`，最大 32 KiB。唯一允许结构：

```json
{
  "schemaVersion": 1,
  "disabled": [
    "DiagnosticsExport",
    "UpdateCheck"
  ]
}
```

允许名称只来自当前 approved capability：

```text
CleanupExecute
UpdateCheck
UpdateInstall
DiagnosticsExport
LeagueApplyMySelection
LeagueCreatePerkPage
LeagueUpdatePerkPage
LeagueSetCurrentPerkPage
```

运行规则：

- 文件不存在：baseline 保持不变；
- valid disabled list：只从 baseline 减权；
- unknown property / unknown capability / bad schema / malformed JSON / over-size / read failure：**disable all approved**；
- 不允许通过新增字段表达 `enabled=true`；
- 不允许 kill switch 创建新 League writer permission。

`scripts/check-facm4-recovery.ps1` 守 explicit baseline、disable-only、gated wrappers、fail-closed parser。

## 8. Gate 11 Recovery state runbook

文件：`<distribution>/runtime/recovery/state.json`，最大 64 KiB。

phase：`Clean / Starting / Running / Failed / Recovering`。

启动：

```text
load state
-> BeginStart(current version)
-> initialize product runtime
-> success: MarkRunning
-> exception: MarkFailed(exception type only)
```

若上次 state 停在 Starting，新启动标 `previous-start-incomplete` 并增加失败计数。成功 Running 后记录 last-known-good app version，failure count 清 0。

State save 使用 same-directory temp + WriteThrough + flush-to-disk + replace。malformed/oversized metadata 回安全 initial state。Recovery metadata 是 defense-in-depth：读写失败不得阻止本可成功的程序启动，也不得掩盖原始 launch exception。

## 9. Settings LKG runbook

严格 primary：`settings.v2.json`。LKG：`runtime/recovery/settings.v2.lkg.json`。

规则：

1. primary strict load 成功且 validator 通过 -> 正常使用并 best-effort 刷新 LKG；
2. primary `InvalidDataException` -> 尝试读取 validator-backed LKG；
3. 有有效 LKG -> `RecoveredLastKnownGood`；
4. 无有效 LKG -> `RecoveryDefaults`，并强制 `AutoUpdateEnabled=false`；
5. corrupt primary **不自动覆盖**，保留给诊断/人工判断；
6. legacy `settings.ini` Gate 13 前不删除、不由 recovery 重写。

Recovery 不得把 future schema 当旧 schema 静默接受。

## 10. Update recovery runbook

`UpdateRecoveryPolicy` 只做状态/证据约束，不取代正式 updater。

- candidate 没有 validated receipt：禁止 replacement；
- replacement 前旧版本必须仍可保留/回滚；
- replacement failed：keep current/old version；
- 有 rollback evidence 时走 rollback；
- candidate 只有 replacement 成功后才可能成为新运行基线。

正式 updater 的 size limit、SHA-256、signature/package validation、validated receipt、wait-exit、separate replacement、failure keeps old、rollback 继续是硬约束。

**GitHub Actions smoke 不能替代 interrupted replacement / rollback 真机验证。**

## 11. Gate 11 evidence

implementation head `c98432b287690813f6a82b04db924e67525e4940`：

- Foundation #204 SUCCESS：全部 source gates、restore/build、Gate11Smoke、WindowsSmoke、single-file publish/output/artifact 全绿；
- Windows Build #1354 SUCCESS；
- UI Text #475 SUCCESS；
- artifact id `9644291683`，ZIP `88,317,150` bytes，digest `sha256:b2d4e30551ae15ba2fd7345edf61d74cab12ffe4983e6aec109e4492d00547aa`。

Gate11Smoke 已证明 unknown feature fail closed、disabled writer zero-call、malformed/oversized recovery metadata、previous-start-incomplete、Settings LKG/default fallback、corrupt primary 保留、unvalidated update block、replacement failure keeps old。

## 12. Gate 12 release evidence runbook

Gate 12 要把自动化工程证据与真实设备证据放进同一结构化 matrix，但 status 必须诚实。

每项至少包含：

```text
id
category
status = passed | blocked | not-run
source/evidence reference
notes
```

规则：

- CI/source/smoke `passed` 必须有对应 run/commit/source；
- manual/real-device `passed` 必须有真实证据引用；
- required item 没 evidence 时只能 blocked/not-run；
- external blocker 未清零时 overall release readiness = BLOCKED；
- Gate 12 engineering 可以完成，但不能把 BLOCKED 写成 PASS；
- production pointer 始终保持 3.5.15。

Gate 12 自动回归必须确认：

- Gates 1～11 source/smoke 没静默删除；
- Performance Contract 不比冻结 baseline 更激进；
- League cadence 保持 ChampSelect 2s、Matchmaking/ReadyCheck 3s、InGame 10s、connected other 5s、disconnected/error 10s；
- exactly one session/gameflow owner；
- UI 不新增 polling；
- candidate 记录 EXE/hash/size/artifact digest。

当前必须保留为 external blocker：non-admin UAC + cancel、Defender/SmartScreen、Win10 1809/22H2 + Win11、100/125/150/175/200% 真机 DPI、dual/mixed DPI/negative coordinates、keyboard-only/focus、High Contrast、text scaling、basic screen reader、3.5.15 -> 4.0 settings 真机升级、interrupted updater replacement/rollback。

## 13. Gate 13 cutover

Gate 13 前必须：Gates 0～12 evidence 闭环 + fresh safety check + production/destructive authorization。证据未闭环时只能标 **release blocked**，不得退休 legacy、修改 production pointer、发布 4.0.0。

branch/tag 删除、production deploy/restart、production pointer 修改都不自动执行。

## 14. 每个 Gate 关闭流程

1. latest main -> Issue + branch + PR；
2. 同 branch 完代码/tests/canonical docs；
3. legacy + 4.0 latest-head gates 全绿；
4. merge main 并 verify；
5. 直接进入下一 Gate。
