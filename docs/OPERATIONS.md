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
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj -c Release
```

## 3. Stable runtime paths

`Environment.ProcessPath` = distribution EXE；`AppContext.BaseDirectory` 可是 `%TEMP%/.net/...`。settings / ui-text / logs / runtime / diagnostics / PetHost / update replacement 只能从 distribution EXE 推导。

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

## 6. Gate 10 DPI runbook

### Manifest

必须同时保持：

```text
requestedExecutionLevel = asInvoker
dpiAware = true/pm
dpiAwareness = PerMonitorV2, PerMonitor
```

### Coordinate rules

- work-area 与 `AppWindow.MoveAndResize` 都是 Windows desktop physical pixels。
- Core `DesktopDpi.DefaultDpi = 96`。
- DPI->scale 与 DIP->physical pixel 只走 `DesktopDpi`。
- Windows adapter 负责采集 DPI facts，不拥有换算 policy。
- FloatingWindow 不允许恢复 `SurfaceSideDip * selected.DpiScale...` 之类重复计算。
- 负 X/Y 不 clamp 到 0；nearest/off-screen recovery 保留。

### Deterministic matrix

Gate10Smoke 必须覆盖：

```text
96  DPI = 100% = 1.00
120 DPI = 125% = 1.25
144 DPI = 150% = 1.50
168 DPI = 175% = 1.75
192 DPI = 200% = 2.00
```

同时覆盖 64 DIP physical conversion、mixed X/Y scale、left/right/top/negative work areas、off-screen recovery、invalid DPI fail closed。

## 7. Gate 10 Accessibility runbook

- Main navigation、Diagnostics actions、F entry 必须有稳定 AutomationId。
- Name/HelpText 必须来自 UI Text provider/defaults。
- 主要动作使用 Button/NavigationView keyboard behavior；禁止 pointer-only gesture。
- 不通过 `IsTabStop=False` 移出主要 keyboard path。
- 长正文/说明/状态允许 Wrap；关键 TextBlock 禁止固定高度裁剪。
- theme colors 继续 alias WinUI platform resources；High Contrast 不加私有硬编码 palette。

`scripts/check-facm4-accessibility.ps1` 是自动 source contract。

## 8. Gate 10 evidence boundary

Hosted runner 可以证明 source/API/manifest/geometry/WinUI wiring，但**不能**替代以下真实 evidence：

- Win10 1809 / 22H2 + Win11；
- 100/125/150/175/200% 真机；
- 左右/上下 dual monitor、负坐标、mixed DPI 屏间移动；
- keyboard-only/tab/focus；
- High Contrast；
- text scaling；
- basic screen reader。

缺证据时写 Gate 12/13 blocker，不得声称 release-ready。

## 9. Gate 11 Recovery / Feature Flags runbook

工程顺序：

```text
Core typed feature catalog
-> local allowed policy
-> remote kill-switch restriction
-> effective monotonic evaluator
-> current/candidate/LKG recovery contracts
-> validated promote only
-> deterministic recovery/flag smoke
-> source gate
```

规则：

- unknown feature = disabled；
- risky/nonessential feature default safe；
- effective feature = local allowed AND remote allowed AND recovery allowed；
- remote kill switch 只能关，不能开；
- feature flag 不得创建新 `LeagueWriteCapability`；
- candidate validation 失败不得覆盖 current/LKG；
- recovery 可以 disable/degrade，不能提升业务写权限。

Updater 继续保持 size limit、SHA-256、signature/package validation、validated receipt、wait-exit、separate replacement、failure keeps old、rollback。Gate 11 不修改 production pointer。

## 10. Release matrix / Gate 13

Gate 12 汇总自动 + 真机 evidence，包括 non-admin UAC/cancel、Defender/SmartScreen、Win10/11、DPI/multi-monitor/accessibility、3.5.15->4.0 settings migration、interrupted updater rollback。

Gate 13 cutover 前必须 fresh safety check + production/destructive authorization。证据未闭环时只能标 `release blocked`，不能退休 legacy 或修改 production pointer。

## 11. 每个 Gate 关闭流程

1. latest main -> Issue + branch + PR；
2. 同 branch 完代码/tests/canonical docs；
3. legacy + 4.0 latest-head gates 全绿；
4. merge main 并 verify；
5. 直接进入下一 Gate。

branch/tag 删除、production deploy/restart 不自动执行。
